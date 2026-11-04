using System.Text.Json;
using System.Text.RegularExpressions;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// fara-vrc-photo-server（VRChat のログを読んで、撮影時のアバターを写真に追記する常駐ソフト）の履歴ファイル（history.jsonl）の読み取り。
/// 写真の XMP に追記が無い写真（ソフトを入れる前・ソフトが動いていなかったとき）の、アバターの手がかりに使う。
/// 履歴は 1 行 1 イベントの JSON（<c>{"t":"2026-10-28T01:49:51+09:00","type":"avatar","avatarName":"…","avatarId":"avtr_…"}</c>）。
/// 読むだけ。壊れた行・知らない種類は読み飛ばす。ファイルが無ければ空。
/// </summary>
public sealed class PhotoServerHistory
{
    private readonly IReadOnlyList<(DateTimeOffset Time, string Name, string? Id)> _avatars;

    private PhotoServerHistory(IReadOnlyList<(DateTimeOffset, string, string?)> avatars) => _avatars = avatars;

    public static readonly PhotoServerHistory Empty = new([]);

    /// <summary>履歴の既定の場所（%LOCALAPPDATA%\FaraVrcPhotoServer\history.jsonl）。</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FaraVrcPhotoServer", "history.jsonl");

    public int Count => _avatars.Count;

    public static PhotoServerHistory Load(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Empty;
        var list = new List<(DateTimeOffset, string, string?)>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "avatar") continue;
                    if (!DateTimeOffset.TryParse(Text(root, "t"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)) continue;
                    if (Text(root, "avatarName") is not { Length: > 0 } name) continue;
                    list.Add((time, name, Text(root, "avatarId")));
                }
                catch (JsonException)
                {
                    // 壊れた行は読み飛ばす
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Empty;
        }
        return new PhotoServerHistory(list);
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>撮影日時のときに操作していたアバター: 撮影日時以前でいちばん新しい切り替え（24 時間より前のものは使わない）。同じ時刻なら ID があるほう。無ければ null。</summary>
    public (string Name, string? Id)? AvatarAt(DateTimeOffset taken)
    {
        var found = _avatars
            .Where(a => a.Time <= taken && taken - a.Time <= TimeSpan.FromHours(24))
            .OrderByDescending(a => a.Time)
            .ThenByDescending(a => a.Id is not null)
            .Select(a => ((string, string?)?)(a.Name, a.Id))
            .FirstOrDefault();
        return found;
    }
}

/// <summary>写真から分かったアバター名を、アバターのマスタ（content/avatars.json）の項目に合わせる。</summary>
public static partial class AvatarMatcher
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>名前の比べ方: 空白を取り、大文字小文字を区別しない（VRChat の「Fara AlmarianDress」と、prefab の「FaraAlmarianDress」が合う）。</summary>
    public static string Normalize(string name) => Spaces().Replace(name, "").ToLowerInvariant();

    /// <summary>名前（または別名）が合うアバターのマスタの id。合うものが無い・複数に合うときは null（間違った服を出さない）。</summary>
    public static string? Match(string? name, IReadOnlyDictionary<string, AvatarMasterEntry> avatars)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var wanted = Normalize(name);
        var hits = avatars
            .Where(a => Normalize(a.Value.Name) == wanted || a.Value.Aliases.Any(x => Normalize(x) == wanted))
            .Select(a => a.Key)
            .ToList();
        return hits.Count == 1 ? hits[0] : null;
    }
}
