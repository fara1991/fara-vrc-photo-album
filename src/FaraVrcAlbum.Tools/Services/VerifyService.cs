using System.Net.Http.Headers;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>検査する写真。Cover: いずれかの公開記事の表紙（_og.jpg はカバーだけ）。Ledger: 記事には無く台帳（uploaded）だけにある写真（--ledger）。</summary>
/// <summary>検査の問題 1 件（写真のキー・接尾辞・内容）。</summary>
public sealed record VerifyProblem(string Key, string Suffix, string Message)
{
    public override string ToString() => $"{Key}{Suffix}: {Message}";
}

public sealed record VerifyPhoto(string Key, int W, int H, bool Cover = false, bool Ledger = false);

/// <summary>検査の対象と、0 件の理由などの注記（CLI と画面で同じ文言を出す）。</summary>
public sealed record VerifyScope(
    IReadOnlyList<VerifyPhoto> Photos,
    IReadOnlyList<string> Notes,
    int PostPhotoCount,
    int LedgerOnlyCount);


/// <summary>
/// 公開の画像（R2 の独自ドメイン）が、記事の JSON と合っているかを確かめる（計画書 §5.5.2 の verify）。
/// 各写真について: _640.webp・_1280.webp・_2048.webp が 200 で返る、Content-Type と Cache-Control が正しい、
/// _2048.webp の寸法が JSON の w/h と一致する（先頭 64 バイトだけを Range で読む）。
/// カバー写真だけは、さらに _og.jpg が 200 で返ることを確かめる。
/// </summary>
public static class VerifyService
{
    private static readonly string[] WebpSuffixes = ["_640.webp", "_1280.webp", "_2048.webp"];

    /// <summary>記事の写真（非公開の記事も含む。公開したときに写真が揃っているように）。同じキーで寸法が違えば、それも問題にする。</summary>
    public static IReadOnlyList<VerifyPhoto> Collect(ContentSet content) => Plan(content, null).Photos;

    /// <summary>
    /// 検査の対象を決める: 記事の写真（非公開の記事も含む）。ledger を渡すと、記事に無い台帳の uploaded の写真も加える。
    /// 0 件のときは理由を注記に入れる（記事がない・写真の無い記事だけ・台帳に無い、など）。
    /// </summary>
    public static VerifyScope Plan(ContentSet content, PhotoLedger? ledger)
    {
        var photos = new Dictionary<string, VerifyPhoto>(StringComparer.Ordinal);
        var conflicts = new List<string>();
        foreach (var post in content.Posts)
        {
            foreach (var photo in post.Photos)
            {
                if (photos.TryGetValue(photo.Key, out var seen))
                {
                    if (seen.W != photo.W || seen.H != photo.H) conflicts.Add($"{photo.Key}: 記事ごとに寸法が違います");
                    continue;
                }
                photos[photo.Key] = new VerifyPhoto(photo.Key, photo.W, photo.H);
            }
        }
        if (conflicts.Count > 0) throw new InvalidDataException(string.Join("\n", conflicts));

        var notes = new List<string>();
        var postPhotoCount = photos.Count;
        var ledgerCount = 0;
        if (ledger is not null)
        {
            foreach (var entry in ledger.BySha.Values.Where(e => e.State == LedgerStates.Uploaded).OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (photos.ContainsKey(entry.Key)) continue;
                photos[entry.Key] = new VerifyPhoto(entry.Key, entry.W, entry.H, Ledger: true);
                ledgerCount++;
            }
        }

        var covers = content.Posts.Select(p => p.Cover ?? p.Photos.FirstOrDefault()?.Key).Where(k => k is not null).ToHashSet(StringComparer.Ordinal);
        var list = photos.Values
            .Select(p => p with { Cover = covers.Contains(p.Key) })
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToList();

        if (content.Posts.Count == 0)
        {
            notes.Add("記事がありません。");
        }
        else if (postPhotoCount == 0)
        {
            notes.Add("記事に写真がありません（写真のない記事だけ）。");
        }
        if (ledger is not null)
        {
            notes.Add(ledgerCount == 0
                ? "台帳（uploaded）に、記事に無い写真はありません（--ledger の追加分は 0 件）。"
                : $"台帳（uploaded）だけにある写真 {ledgerCount} 件を加えました（記事には無い写真）。");
        }
        if (list.Count == 0 && notes.Count == 0) notes.Add("検査する写真がありません。");
        return new VerifyScope(list, notes, postPhotoCount, ledgerCount);
    }

    /// <summary>問題の一覧（空なら全部そろっている）。並びは写真のキー・接尾辞の順。</summary>
    public static async Task<IReadOnlyList<VerifyProblem>> RunAsync(IEnumerable<VerifyPhoto> photos, string baseUrl, HttpClient http, int concurrency = 8, CancellationToken ct = default)
    {
        var problems = new System.Collections.Concurrent.ConcurrentBag<VerifyProblem>();
        using var gate = new SemaphoreSlim(concurrency);
        await Parallel.ForEachAsync(photos, ct, async (photo, token) =>
        {
            await gate.WaitAsync(token);
            try
            {
                foreach (var problem in await CheckPhotoAsync(photo, baseUrl, http, token)) problems.Add(problem);
            }
            finally
            {
                gate.Release();
            }
        });
        return problems.OrderBy(p => p.Key, StringComparer.Ordinal).ThenBy(p => p.Suffix, StringComparer.Ordinal).ToList();
    }

    private static async Task<IReadOnlyList<VerifyProblem>> CheckPhotoAsync(VerifyPhoto photo, string baseUrl, HttpClient http, CancellationToken ct)
    {
        var problems = new List<VerifyProblem>();
        var suffixes = photo.Cover ? WebpSuffixes.Append("_og.jpg") : WebpSuffixes;
        foreach (var suffix in suffixes)
        {
            var (ok, bytes, message) = await FetchHeadAsync(baseUrl + photo.Key + suffix, http, ct);
            if (!ok)
            {
                problems.Add(new VerifyProblem(photo.Key, suffix, message));
                continue;
            }
            if (suffix == "_2048.webp")
            {
                var dims = WebpInspector.Dimensions(bytes);
                if (dims is null) problems.Add(new VerifyProblem(photo.Key, suffix, "WebP の寸法が読めません"));
                else if (dims.Value.Width != photo.W || dims.Value.Height != photo.H)
                {
                    problems.Add(new VerifyProblem(photo.Key, suffix,
                        $"寸法が JSON と違います（画像 {dims.Value.Width}x{dims.Value.Height} / JSON {photo.W}x{photo.H}）"));
                }
            }
        }
        return problems;
    }

    /// <summary>先頭 64 バイトと、Content-Type・Cache-Control を確かめる。</summary>
    private static async Task<(bool Ok, byte[] Bytes, string Message)> FetchHeadAsync(string url, HttpClient http, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 63);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return (false, [], $"HTTP {(int)response.StatusCode}");

        var key = url;
        var expectedType = ObjectHeaders.ContentTypeFor(key);
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type != expectedType.Split(';')[0].Trim()) return (false, [], $"Content-Type が {type ?? "（無し）"}（期待 {expectedType}）");

        var cacheControl = string.Join(", ", response.Headers.CacheControl?.ToString().Split(',').Select(s => s.Trim()) ?? []);
        var expectedCache = ObjectHeaders.ImmutableImage;
        if (!SameDirectives(cacheControl, expectedCache)) return (false, [], $"Cache-Control が {cacheControl}（期待 {expectedCache}）");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[64];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        return (true, buffer[..read], "");
    }

    private static bool SameDirectives(string actual, string expected) =>
        new HashSet<string>(actual.Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase)
            .SetEquals(expected.Split(',').Select(s => s.Trim()));
}
