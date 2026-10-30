using System.Text.RegularExpressions;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

public sealed record AuditReport(
    int ObjectCount,
    long TotalBytes,
    IReadOnlyDictionary<string, int> CountByMonth,
    IReadOnlyList<string> Orphans,
    IReadOnlyList<string> Missing);

/// <summary>
/// R2 の写真のオブジェクトと、記事・台帳の参照を突き合わせる（読み取りだけ・計画書 §5.5.2 の audit）。
/// 対象は写真のキー（yyyy-mm/…）で始まるものだけ。写真以外のオブジェクトは数えず、孤児にもしない。
/// 孤児: どの記事・台帳からも参照されない WebP、表紙（cover・省略時は先頭の写真）でなくなった写真の _og.jpg、写真のキーの形でないオブジェクト。
/// 欠け: 記事（下書きを含む）にある写真の _2048.webp が無い、表紙の写真の _og.jpg が無い。
/// </summary>
public static partial class AuditService
{
    [GeneratedRegex(@"^(\d{4}-\d{2}/\d{8}-\d{6}-\d{3}_[0-9a-f]{8}(?:-r\d+)?)(_640\.webp|_1280\.webp|_2048\.webp|_og\.jpg)$")]
    private static partial Regex ObjectPattern();

    [GeneratedRegex(@"^\d{4}-\d{2}/")]
    private static partial Regex PhotoPrefix();

    /// <summary>突き合わせに使う参照の集合（記事の写真と台帳の uploaded・表紙・記事の写真）。CLI と画面で同じ決め方にする。</summary>
    public static (IReadOnlySet<string> Referenced, IReadOnlySet<string> Covers, IReadOnlySet<string> PostKeys) References(ContentEditor editor, PhotoLedger ledger)
    {
        var postKeys = PostService.PhotoKeys(editor);
        var referenced = postKeys.Concat(ledger.BySha.Values.Where(e => e.State == LedgerStates.Uploaded).Select(e => e.Key)).ToHashSet(StringComparer.Ordinal);
        return (referenced, PostService.CoverKeys(editor), postKeys);
    }

    /// <param name="referencedKeys">記事（下書きを含む）と台帳（uploaded）が参照している写真のキー。</param>
    /// <param name="coverKeys">いずれかの記事の表紙（cover、省略時は先頭の写真）になっている写真のキー。_og.jpg はこれだけが対象。</param>
    /// <param name="postKeys">記事（下書きを含む全件）の写真のキー。_2048.webp の欠けの検査に使う。台帳にだけあって記事に無い写真は欠けとしない。</param>
    public static async Task<AuditReport> RunAsync(IObjectStore store, IReadOnlySet<string> referencedKeys, IReadOnlySet<string> coverKeys, IReadOnlySet<string> postKeys, CancellationToken ct = default)
    {
        var count = 0;
        long bytes = 0;
        var byMonth = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var present = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var orphans = new SortedSet<string>(StringComparer.Ordinal);

        await foreach (var (objectKey, size) in store.ListAsync("", ct))
        {
            if (!PhotoPrefix().IsMatch(objectKey)) continue; // 写真以外（数えない・孤児にしない）
            count++;
            bytes += size;
            var match = ObjectPattern().Match(objectKey);
            if (!match.Success)
            {
                orphans.Add(objectKey);
                continue;
            }
            var key = match.Groups[1].Value;
            var suffix = match.Groups[2].Value;
            var month = key[..7];
            byMonth[month] = byMonth.GetValueOrDefault(month) + 1;
            if (suffix == "_og.jpg" && !coverKeys.Contains(key))
            {
                orphans.Add(objectKey);
                continue;
            }
            if (!present.TryGetValue(key, out var suffixes)) present[key] = suffixes = new HashSet<string>(StringComparer.Ordinal);
            suffixes.Add(suffix);
        }

        foreach (var key in present.Keys.Where(k => !referencedKeys.Contains(k))) orphans.Add(key);

        var missing = postKeys.Where(k => !present.TryGetValue(k, out var s) || !s.Contains("_2048.webp"))
            .Concat(coverKeys.Where(k => !present.TryGetValue(k, out var s) || !s.Contains("_og.jpg")))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

        return new AuditReport(count, bytes, byMonth, orphans.ToList(), missing);
    }
}
