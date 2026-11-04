using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// 内容ハッシュの記録（local/cache）。パス・サイズ・更新時刻が同じなら、読み直さずに前回の値を使う。
/// 壊れていれば捨てて作り直す（キャッシュなので失っても構わない）。
/// </summary>
public sealed class HashCache
{
    public sealed record Item(long Size, long Modified, string Sha256);

    private readonly string _path;
    private readonly Dictionary<string, Item> _items;

    private HashCache(string path, Dictionary<string, Item> items)
    {
        _path = path;
        _items = items;
    }

    public static HashCache Load(string path)
    {
        try
        {
            var items = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string, Item>>(File.ReadAllText(path)) ?? new()
                : new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            return new HashCache(path, new Dictionary<string, Item>(items, StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return new HashCache(path, new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase));
        }
    }

    public string Sha256(string file)
    {
        var info = new FileInfo(file);
        var modified = info.LastWriteTimeUtc.Ticks;
        if (_items.TryGetValue(file, out var item) && item.Size == info.Length && item.Modified == modified) return item.Sha256;

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
        var sha = Convert.ToHexStringLower(SHA256.HashData(stream));
        _items[file] = new Item(info.Length, modified, sha);
        return sha;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_items), new System.Text.UTF8Encoding(false));
    }
}

public sealed record ScanRow(string Month, int Total, int WithXmp, int Uploaded, int Removed, int Unregistered, int BeforeWindow, int Unreadable);

/// <summary>
/// 元写真のフォルダを読み取り専用で走査し、月ごとの件数を出す（計画書 §5.5.2 の scan）。何も書かない（キャッシュ以外）。
/// ファイル名は表示しない（撮影月・件数だけ）。
/// </summary>
public static class ScanService
{
    /// <summary>フォルダの下の、backup という名前のフォルダの中か（fara-vrc-photo-server の、元の写真の控え）。</summary>
    private static bool IsInBackupFolder(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[..^1].Any(d => string.Equals(d, "backup", StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<ScanRow> Run(string folder, PhotoLedger ledger, HashCache cache)
    {
        var rows = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            if (IsInBackupFolder(folder, file)) continue;                                  // fara-vrc-photo-server が残した、元の写真の控え（backup フォルダ）は数えない
            var name = Path.GetFileName(file);
            var fromName = PhotoKeys.ParseFileName(name);
            var month = fromName is { } parsed ? PhotoKeys.Normalize(parsed.TakenAt).ToString("yyyy-MM", CultureInfo.InvariantCulture) : "不明";
            var row = rows.TryGetValue(month, out var existing) ? existing : rows[month] = new int[7];

            row[0]++;
            PngHeaderInfo? header;
            try
            {
                header = PngScanner.ScanFile(file);
            }
            catch (IOException)
            {
                row[6]++;
                continue;
            }
            if (header is null)
            {
                row[6]++;
                continue;
            }
            if (header.XmpText is not null) row[1]++;
            if (fromName is { } f && PhotoKeys.Normalize(f.TakenAt) < SiteRules.EarliestPhotoDate) row[5]++;

            var sha = cache.Sha256(file);
            var entry = ledger.FindBySha(sha);
            if (entry is { State: LedgerStates.Uploaded }) row[2]++;
            else if (entry is { State: LedgerStates.Removed }) row[3]++;
            else row[4]++;
        }

        return rows.Select(p => new ScanRow(p.Key, p.Value[0], p.Value[1], p.Value[2], p.Value[3], p.Value[4], p.Value[5], p.Value[6])).ToList();
    }
}
