using System.Xml;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// inbox の 1 枚の状態（読み取りのみ）。デスクトップ版の一覧に使う。ファイル名は持たない（一覧はサムネイルと撮影時刻で示す）。
/// </summary>
public sealed record InboxRow(
    string Path,
    string? FolderMonth,
    DateTimeOffset? TakenAt,
    bool HasXmp,
    string? WorldId,
    string? WorldName,
    string LedgerState,
    string? Key,
    string? Problem);

/// <summary>
/// inbox\yyyy-mm の PNG を一覧にする。書き込みはしない。ハッシュは渡された <see cref="HashCache"/>（既定は local/cache/hashes.json）を読み、新しい分だけ記録する（保存は呼び出し側の Save）。
/// 状態: 未登録（取り込める）・アップロード済み・削除済み（--force が要る）・2025-11 より前（取り込まない）・読めない。
/// </summary>
public static class InboxService
{
    /// <summary>yyyy-mm のフォルダ（新しい順）。VRChat の写真のフォルダ・inbox のどちらにも使える。</summary>
    public static IReadOnlyList<string> Months(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>()
                .Where(name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{4}-\d{2}$")).OrderDescending(StringComparer.Ordinal).ToList()
            : [];

    /// <summary>
    /// 1 か月分（root\yyyy-mm の直下の PNG）の状態。month が null なら全部の月。読むだけ（ハッシュの控えは cache に足す）。
    /// VRChat の写真のフォルダは枚数が多いので、デスクトップ版は月ごとに読む。
    /// </summary>
    public static IReadOnlyList<InboxRow> ListMonth(string root, string? month, PhotoLedger ledger, HashCache cache)
    {
        if (month is null) return Months(root).SelectMany(m => ListMonth(root, m, ledger, cache)).OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
        var dir = Path.Combine(root, month);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal)
            .Select(file => Describe(file, month, ledger, cache)).ToList();
    }

    /// <summary>
    /// VRChat の写真を inbox\yyyy-mm にコピーする（元のファイルは読むだけ）。コピーしたファイルの場所を、渡した順で返す。
    /// 同じ名前のファイルが inbox にあって中身も同じなら、コピーせずにそれを使う。中身が違えば例外（上書きしない）。
    /// </summary>
    public static IReadOnlyList<string> CopyToInbox(IReadOnlyList<string> files, string sourceRoot, string inboxRoot, HashCache cache)
    {
        var copied = new List<string>();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceRoot, file);
            var month = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (relative.StartsWith("..", StringComparison.Ordinal) || !System.Text.RegularExpressions.Regex.IsMatch(month, @"^\d{4}-\d{2}$"))
                throw new InvalidOperationException($"VRChat の写真のフォルダの yyyy-mm の中の写真ではありません: {file}");
            var target = Path.Combine(inboxRoot, month, Path.GetFileName(file));
            if (File.Exists(target))
            {
                if (cache.Sha256(target) != cache.Sha256(file))
                    throw new InvalidOperationException($"inbox に同じ名前の別の写真があります（上書きしません）: {month}\\{Path.GetFileName(file)}");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
            copied.Add(target);
        }
        return copied;
    }

    public static IReadOnlyList<InboxRow> List(string inboxRoot, PhotoLedger ledger, HashCache cache)
    {
        var rows = new List<InboxRow>();
        if (!Directory.Exists(inboxRoot)) return rows;

        foreach (var file in Directory.EnumerateFiles(inboxRoot, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(inboxRoot, file);
            var firstFolder = relative.Split(Path.DirectorySeparatorChar)[0];
            var folderMonth = System.Text.RegularExpressions.Regex.IsMatch(firstFolder, @"^\d{4}-\d{2}$") ? firstFolder : null;
            rows.Add(Describe(file, folderMonth, ledger, cache));
        }
        return rows;
    }

    /// <summary>1 枚の状態を調べる。PNG として読めない・ファイルが消えた等は Problem に入れて返す（例外にしない）。</summary>
    public static InboxRow Describe(string file, string? folderMonth, PhotoLedger ledger, HashCache cache)
    {
        try
        {
            var header = PngScanner.ScanFile(file);
            if (header is null) return Broken(file, folderMonth, "PNG ではありません");

            var xmp = header.XmpText is null ? null : XmpReader.Parse(header.XmpText);
            var (takenAt, _) = PhotoIngestor.ResolveTakenAt(xmp, Path.GetFileName(file));
            var sha = cache.Sha256(file);
            var entry = ledger.FindBySha(sha);

            string state;
            string? problem = null;
            if (entry is { State: LedgerStates.Uploaded }) state = "アップロード済み";
            else if (entry is { State: LedgerStates.Removed }) state = "削除済み";
            else state = "未登録";

            if (takenAt is null) problem = "撮影日時が分かりません";
            else if (takenAt.Value < SiteRules.EarliestPhotoDate) problem = "2025 年 11 月より前（取り込みません）";

            var key = takenAt is null ? null : PhotoKeys.Build(takenAt.Value, sha);
            return new InboxRow(file, folderMonth, takenAt is null ? null : PhotoKeys.Normalize(takenAt.Value), header.XmpText is not null,
                xmp?.WorldId, xmp?.WorldName, state, key ?? entry?.Key, problem);
        }
        catch (IOException)
        {
            return Broken(file, folderMonth, "読めませんでした（書き込み中かもしれません）");
        }
        catch (Exception e) when (e is XmlException or InvalidDataException or FormatException)
        {
            // 壊れた XMP（XML として読めない）や、想定外の形式は「読めない」として扱い、一覧を止めない
            return Broken(file, folderMonth, "中身が読めませんでした（XMP の形式が壊れているなど）");
        }
    }

    private static InboxRow Broken(string file, string? folderMonth, string problem) =>
        new(file, folderMonth, null, false, null, null, "未登録", null, problem);
}
