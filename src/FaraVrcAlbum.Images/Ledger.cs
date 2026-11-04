using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace FaraVrcAlbum.Images;

/// <summary>台帳の 1 行（元ファイルの内容ハッシュ 1 つ分）。公開データではない（Build は読まない）。</summary>
public sealed class LedgerEntry
{
    public string Key { get; set; } = "";

    /// <summary>uploaded（R2 に在る）/ removed（削除済み。再登録はしない）。</summary>
    public string State { get; set; } = LedgerStates.Uploaded;

    public string? UploadedAt { get; set; }

    public string? RemovedAt { get; set; }

    public string TakenAt { get; set; } = "";

    public string SourceName { get; set; } = "";

    public int W { get; set; }

    public int H { get; set; }

    public string Color { get; set; } = "";

    public LedgerWorld? World { get; set; }

    /// <summary>撮影時のアバター名（写真の XMP の faratag:AvatarName（旧形式は faraxml:AvatarName）の控え。記事の下書きで、アバターを自動で入れるのに使う。公開データではない）。</summary>
    public string? AvatarName { get; set; }

    /// <summary>撮影時のアバターの ID（faratag:AvatarID。旧形式は faraxml:AvatarId。分かったときだけ）。</summary>
    public string? AvatarId { get; set; }

    /// <summary>出力ごとのバイト数（"640"・"1280"・"2048"・"og"）。</summary>
    public Dictionary<string, long> Bytes { get; set; } = new();

    public int Encoder { get; set; }
}

public sealed class LedgerWorld
{
    public string Id { get; set; } = "";

    /// <summary>撮影時点の名前（XMP の WorldDisplayName の控え。表示名はワールドのマスタの name）。</summary>
    public string Name { get; set; } = "";
}

public static class LedgerStates
{
    public const string Uploaded = "uploaded";
    public const string Removed = "removed";
}

/// <summary>
/// 台帳（content/catalog/photos.json。Git 管理）: 元ファイルの SHA-256 全体 → 行。
/// 二重アップロードの防止（uploaded）、削除後の再アップロード防止（removed。ハッシュを残す）、記事の下書きの元。
/// 保存はファイルごとに一時ファイル → 置き換え（途中で落ちても、ファイルは完全な形のまま）。キーの順に書いて差分を小さくする。
/// </summary>
public sealed class PhotoLedger
{
    private readonly Dictionary<string, LedgerEntry> _bySha;

    private PhotoLedger(Dictionary<string, LedgerEntry> bySha) => _bySha = bySha;

    public static PhotoLedger Empty() => new(new Dictionary<string, LedgerEntry>(StringComparer.Ordinal));

    /// <summary>ファイルを読む。無ければ空。壊れていれば JsonException（自動では直さない）。</summary>
    public static PhotoLedger Load(string path)
    {
        if (!File.Exists(path)) return Empty();
        var parsed = JsonSerializer.Deserialize<Dictionary<string, LedgerEntry>>(File.ReadAllText(path), JsonFiles.Options);
        return new PhotoLedger(parsed ?? new Dictionary<string, LedgerEntry>(StringComparer.Ordinal));
    }

    public IReadOnlyDictionary<string, LedgerEntry> BySha => _bySha;

    public LedgerEntry? FindBySha(string sha256) => _bySha.GetValueOrDefault(sha256);

    public LedgerEntry? FindByKey(string key) => _bySha.Values.FirstOrDefault(e => e.Key == key);

    /// <summary>そのキーを持つ行の内容ハッシュ（無ければ null）。キーの衝突の検査に使う。</summary>
    public string? ShaOfKey(string key) => _bySha.FirstOrDefault(p => p.Value.Key == key).Key;

    public void Put(string sha256, LedgerEntry entry) => _bySha[sha256] = entry;

    public void Save(string path)
    {
        var ordered = _bySha.OrderBy(p => p.Value.Key, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        JsonFiles.WriteAtomic(path, JsonSerializer.Serialize(ordered, JsonFiles.Options));
    }
}

/// <summary>JSON の読み書きの共通設定（UTF-8・BOM なし・LF・2 桁のインデント・日本語はそのまま）。</summary>
public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>末尾に改行を付け、LF にそろえる。</summary>
    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";

    /// <summary>一時ファイルに書いてから置き換える（書き込み中に落ちても、元のファイルは壊れない）。</summary>
    public static void WriteAtomic(string path, string text) => WriteAtomic(path, new System.Text.UTF8Encoding(false).GetBytes(Normalize(text)));

    /// <summary>バイト列を一時ファイルに書いてから置き換える（書き込み中に落ちても、元のファイルは壊れない）。</summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, data);
        File.Move(temp, path, overwrite: true);
    }
}
