using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// VRChat の公開 API（https://api.vrchat.cloud/api/1/worlds/{id}。ログイン不要）から、ワールドの情報と画像を取る。
///   album worlds       … 画像の無いワールドの画像を R2 に置き、content/worlds.json の image に書く（手元。R2 の鍵が要る）
///   album world-stats  … 全ワールドの情報（訪問数など）を content/world-stats.json に書く（手元のアプリの「サイトの画像情報を更新する」ボタンも同じ）
/// VRChat のガイドラインに合わせて:
///   User-Agent でアプリ名・版・連絡先を名乗る（無いと 403）。
///   VRChat へのリクエスト（情報・画像とも）の間は <see cref="Interval"/>（＋ゆらぎ）空ける（コミュニティの目安: 60 秒に 1 回まで）。
///   429（多すぎる）が返ったら、そこで止める（それまでに取れた分は書ける）。
/// ビルド・公開（CI の build / deploy）では VRChat を読まない。閲覧者のブラウザも VRChat に接続しない（画像は R2 から配る）。
/// </summary>
public static class WorldImageService
{
    public const string UserAgent = "FaraVrcAlbum/1.0 (Fara; game_fara@outlook.com)";

    /// <summary>VRChat へのリクエストの間隔。実際は 0〜5 秒のゆらぎを足す（決まった時刻に揃えない）。</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public sealed class TooManyRequestsException() : Exception("VRChat の API が 429（多すぎる）を返しました");

    /// <summary>VRChat の API の、ワールド 1 件の応答のうち使うもの。</summary>
    public sealed record WorldInfo(string? ImageUrl, WorldStats Stats);

    /// <summary>
    /// VRChat 用の HttpClient。リクエストの間を必ず空ける（最初の 1 回は待たない）。
    /// wait はテスト用（null は Interval ＋ゆらぎ）。
    /// </summary>
    public static HttpClient CreateClient(HttpMessageHandler? inner = null, Func<CancellationToken, Task>? wait = null, TimeSpan? requestTimeout = null)
    {
        var throttle = new Throttle(wait ?? (c => Task.Delay(Interval + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 5000)), c)), requestTimeout ?? RequestTimeout)
        {
            InnerHandler = inner ?? new SocketsHttpHandler { AllowAutoRedirect = false, ConnectCallback = ConnectIpv4FirstAsync },   // 転送（302）は Throttle が自分で追う（どのホストで失敗したかを出すため。転送先は待たない）
        };
        // HttpClient の Timeout は待ち時間まで含むので使わない（通信だけの時間切れは Throttle がかける）
        var http = new HttpClient(throttle) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return http;
    }

    /// <summary>
    /// 1 つのワールドの取得・保存で、想定内の失敗（通信・応答の形・画像の変換・R2・ファイル）。これだけを「そのワールドの問題」として次へ進む。
    /// それ以外（NullReference などバグ）は握らず投げる（黙って続けないため）。
    /// </summary>
    public static bool IsPerWorldFailure(Exception e) =>
        e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException or UnauthorizedAccessException
            or Amazon.Runtime.AmazonServiceException or Amazon.Runtime.AmazonClientException;

    /// <summary>通信の失敗の理由。「SSL 接続を確立できません」のような外側の文だけでは原因が分からないので、内側の例外（接続が切られた・証明書など）の文も並べる。</summary>
    internal static string Describe(Exception e)
    {
        var parts = new List<string>();
        for (var x = e; x is not null; x = x.InnerException!)
        {
            if (!parts.Contains(x.Message)) parts.Add(x.Message);
            if (x.InnerException is null) break;
        }
        return string.Join(" ← ", parts);
    }

    /// <summary>
    /// 接続は IPv4 を先にする（IPv6 は後ろ）。回線によっては、画像の置き場 files.vrchat.cloud（CloudFront）への IPv6 の TLS だけが途中で切られ（「既存の接続はリモート ホストに強制的に切断されました」）、
    /// IPv4 なら通る（curl -6 は失敗・-4 は 200 で確かめた）。.NET は IPv6 を先に試すので、IPv4 を先にして、繋がらないときだけ IPv6 を試す。
    /// </summary>
    public static IEnumerable<IPAddress> Ipv4First(IEnumerable<IPAddress> addresses) =>
        addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1);

    private static async ValueTask<Stream> ConnectIpv4FirstAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        Exception? last = null;
        foreach (var address in Ipv4First(addresses))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // アドレスごとに短い時間切れをかける（IPv4 が無応答でも、IPv6 へ進めるように）
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (e is SocketException || (e is OperationCanceledException && !ct.IsCancellationRequested))
            {
                socket.Dispose();
                last = e;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    /// <summary>1 つのアドレスへの接続の時間切れ（繋がらなければ、次のアドレスを試す）。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>1 回の通信（待ち時間を除く。応答のヘッダーが来るまで）の時間切れ。本文の読み込みは <see cref="ReadBodyAsync"/> が別にかける。</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>本文（画像）の読み込みの時間切れ。</summary>
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(60);

    /// <summary>応答の本文の読み込みにも時間切れをかける（途中で止まっても、公開が終わらなくならないように）。呼び出し側のキャンセルは、そのまま伝える。</summary>
    internal static async Task<T> ReadBodyAsync<T>(Func<CancellationToken, Task<T>> read, TimeSpan limit, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(limit);
        try
        {
            return await read(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException($"VRChat の応答の本文が {limit.TotalSeconds:0} 秒以内に読めませんでした");
        }
    }

    private sealed class Throttle(Func<CancellationToken, Task> wait, TimeSpan requestTimeout) : DelegatingHandler
    {
        private bool _first = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // 接続が切られた（IOException・SocketException が原因）ときは、同じ決まり（間を空ける）で、最大 3 回まで試す。一時的な切断で、ワールドの画像が取れないままにならないように
            for (var attempt = 1; ; attempt++)
            {
                if (!_first) await wait(ct);
                _first = false;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(requestTimeout);
                HttpResponseMessage response;
                try
                {
                    response = await SendFollowingAsync(attempt == 1 ? request : Clone(request), timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new HttpRequestException($"VRChat の応答が {requestTimeout.TotalSeconds:0} 秒以内にありませんでした");
                }
                catch (HttpRequestException e) when (attempt < MaxAttempts && IsConnectionFailure(e))
                {
                    continue;
                }
                catch (HttpRequestException e) when (attempt > 1)
                {
                    throw new HttpRequestException($"{e.Message}（{attempt} 回試しました）", e.InnerException ?? e);
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    response.Dispose();
                    throw new TooManyRequestsException();
                }
                return response;
            }
        }

        private const int MaxAttempts = 3;

        /// <summary>転送（301・302・303・307・308）を、最大 5 回まで追う（画像は api.vrchat.cloud から files.vrchat.cloud へ転送される）。通信の失敗には、どのホストかを付ける。</summary>
        private async Task<HttpResponseMessage> SendFollowingAsync(HttpRequestMessage request, CancellationToken ct)
        {
            for (var hop = 0; ; hop++)
            {
                HttpResponseMessage response;
                try
                {
                    response = await base.SendAsync(request, ct);
                }
                catch (HttpRequestException e) when (request.RequestUri is { } uri)
                {
                    throw new HttpRequestException($"{uri.Host} への通信に失敗: {e.Message}", e.InnerException ?? e);
                }
                var location = response.Headers.Location;
                if (hop >= 5 || location is null || (int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
                response.Dispose();
                request = new HttpRequestMessage(HttpMethod.Get, location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location));
                request.Headers.UserAgent.ParseAdd(UserAgent);
            }
        }

        /// <summary>接続が切られた・つながらなかった（原因のどこかが IOException か SocketException）。応答の中身の問題（404 など）は含まない。</summary>
        private static bool IsConnectionFailure(HttpRequestException e)
        {
            for (var x = e.InnerException; x is not null; x = x.InnerException)
                if (x is IOException or SocketException) return true;
            return false;
        }

        private static HttpRequestMessage Clone(HttpRequestMessage request)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return copy;
        }
    }

    /// <summary>1 つのワールドの情報。取れなければ例外（429 は <see cref="TooManyRequestsException"/>）。</summary>
    public static async Task<WorldInfo> FetchInfoAsync(HttpClient http, string worldId, CancellationToken ct = default)
    {
        if (!WorldId.IsValid(worldId)) throw new ArgumentException($"ワールド ID の形式が不正です: {worldId}");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.vrchat.cloud/api/1/worlds/{worldId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"VRChat の API が {(int)response.StatusCode} を返しました");
        using var doc = JsonDocument.Parse(await ReadBodyAsync(t => response.Content.ReadAsStringAsync(t), RequestTimeout, ct));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("応答がオブジェクトではありません");
        string Text(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        long Number(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : 0;
        string Date(string name) => ContentDate(Text(name));
        var tags = root.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
                .Where(x => x.StartsWith("author_tag_", StringComparison.Ordinal)).Select(x => x["author_tag_".Length..])
                .Where(x => x.Length is > 0 and <= 64).Distinct(StringComparer.Ordinal).Take(20).ToList()
            : [];
        var stats = new WorldStats
        {
            AuthorName = Limit(Text("authorName"), 64),
            Description = Limit(Text("description"), 2000),
            Capacity = (int)Math.Min(Number("capacity"), int.MaxValue),
            RecommendedCapacity = (int)Math.Min(Number("recommendedCapacity"), int.MaxValue),
            Visits = Number("visits"),
            Favorites = Number("favorites"),
            Tags = tags,
            PublicationDate = Date("publicationDate"),
            UpdatedAt = Date("updated_at"),
        };
        var image = Text("imageUrl");
        return new WorldInfo(VrchatLinks.IsWorldImageUrl(image) ? image : null, stats);
    }

    /// <summary>長すぎる文を切る（UTF-16 の長さ。絵文字などのサロゲートペアの途中では切らない）。</summary>
    internal static string Limit(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = max > 0 && char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..cut];
    }

    /// <summary>VRChat の日時（2024-11-01T16:21:27.800Z）→ content/ の形（オフセット付き・秒まで）。読めなければ空。"none" などの値も空。</summary>
    private static string ContentDate(string text) =>
        DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)
            : "";

    // ------------------------------------------------------------------ album world-stats

    public sealed record StatsResult(int Updated, IReadOnlyList<string> Problems, int Remaining);

    /// <summary>
    /// 全ワールドの情報を取り、content/world-stats.json に書く（apply のとき）。429 で止めたら、取れた分だけ書く（残りは前回の値のまま）。
    /// </summary>
    public static async Task<StatsResult> UpdateStatsAsync(ContentEditor editor, HttpClient http, DateTimeOffset now, bool apply, Action<string>? log = null, CancellationToken ct = default)
    {
        var content = editor.LoadContent();
        var file = content.WorldStats ?? new WorldStatsFile();
        var ids = content.Worlds.Keys.Order(StringComparer.Ordinal).ToList();
        var problems = new List<string>();
        var updated = 0;
        var remaining = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var id = ids[i];
            try
            {
                var info = await FetchInfoAsync(http, id, ct);
                file.Worlds[id] = info.Stats;
                updated++;
                log?.Invoke($"  {content.Worlds[id].Name}: 訪問 {info.Stats.Visits:N0}・お気に入り {info.Stats.Favorites:N0}");
            }
            catch (TooManyRequestsException)
            {
                remaining = ids.Count - i;
                problems.Add($"VRChat の API が 429（多すぎる）を返したので止めました。残り {remaining} 件は前回の値のままです");
                break;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                problems.Add($"{content.Worlds[id].Name}（{id}）: {Describe(e)}");
            }
        }
        // worlds.json から消えたワールドの情報は消す
        foreach (var gone in file.Worlds.Keys.Where(k => !content.Worlds.ContainsKey(k)).ToList()) file.Worlds.Remove(gone);
        // 途中で止まった・取れなかったワールドがあるときは、全体の取得日時を新しくしない（残りは前の値のままのため）
        if (apply && updated > 0) WriteStats(editor, file, now, keepDate: problems.Count > 0);
        return new StatsResult(updated, problems, remaining);
    }

    /// <summary>
    /// content/world-stats.json を書く（検査に通らなければ元に戻す）。<paramref name="keepDate"/> が true のときは、前に全体を取った日時を保つ
    /// （数日前の他のワールドの値が「今日時点」にならないように。ファイルが無いときだけ今の日時）。
    /// </summary>
    internal static void WriteStats(ContentEditor editor, WorldStatsFile file, DateTimeOffset now, bool keepDate)
    {
        if (!keepDate || string.IsNullOrEmpty(file.FetchedAt))
            file.FetchedAt = now.ToOffset(PhotoKeys.Jst).ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture);
        var path = Path.Combine(editor.ContentDir, "world-stats.json");
        var snapshot = new FileSnapshot();
        snapshot.Capture(path);
        try
        {
            var sorted = new WorldStatsFile { FetchedAt = file.FetchedAt };
            foreach (var key in file.Worlds.Keys.Order(StringComparer.Ordinal)) sorted.Worlds[key] = file.Worlds[key];
            JsonFiles.WriteAtomic(path, JsonSerializer.Serialize(sorted, JsonFiles.Options));
            editor.ValidateOrThrow();
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
    }

    // ------------------------------------------------------------------ album worlds（画像を R2 へ）

    public sealed record ImagePlanItem(string Id, string Name, string ImageUrl, string Key);

    public sealed record ImagePlan(IReadOnlyList<ImagePlanItem> Items, IReadOnlyList<string> Problems, int Unchanged, int Remaining);

    /// <summary>
    /// 画像の無いワールド（refresh なら全部）について、VRChat の情報を読み、置く画像のキーを決める。R2 にも content/ にも書かない。
    /// 既に image があるワールドは問い合わせない。
    /// </summary>
    public static async Task<ImagePlan> PlanImagesAsync(ContentEditor editor, HttpClient http, bool refresh, CancellationToken ct = default)
    {
        var content = editor.LoadContent();
        var targets = content.Worlds.OrderBy(w => w.Key, StringComparer.Ordinal).ToList();
        var unchanged = targets.RemoveAll(w => !refresh && w.Value.Image is not null);
        var items = new List<ImagePlanItem>();
        var problems = new List<string>();
        for (var i = 0; i < targets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (id, entry) = targets[i];
            try
            {
                var info = await FetchInfoAsync(http, id, ct);
                if (info.ImageUrl is null)
                {
                    problems.Add($"{entry.Name}（{id}）: 応答に画像（imageUrl）がありません");
                    continue;
                }
                var key = VrchatLinks.WorldImageKey(id, info.ImageUrl);
                if (entry.Image?.Key == key) unchanged++;
                else items.Add(new ImagePlanItem(id, entry.Name, info.ImageUrl, key));
            }
            catch (TooManyRequestsException)
            {
                problems.Add($"VRChat の API が 429（多すぎる）を返したので止めました。残り {targets.Count - i} 件は、時間をおいて再実行してください");
                return new ImagePlan(items, problems, unchanged, targets.Count - i);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                problems.Add($"{entry.Name}（{id}）: {Describe(e)}");
            }
        }
        return new ImagePlan(items, problems, unchanged, 0);
    }

    /// <summary>画像を 1 枚読む（応答のヘッダーにも本文にも時間切れがある。200 以外は例外。429 は <see cref="TooManyRequestsException"/>）。</summary>
    internal static async Task<byte[]> DownloadAsync(HttpClient http, string imageUrl, CancellationToken ct)
    {
        using var response = await http.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"画像の取得が {(int)response.StatusCode} でした");
        return await ReadBodyAsync(t => response.Content.ReadAsByteArrayAsync(t), BodyTimeout, ct);
    }

    /// <summary>
    /// 計画の画像を VRChat から読み、WebP（512・1024）にして R2 に置き、worlds.json の image を書く。1 件ずつ書く（途中で 429 になっても、それまでの分は残る）。
    /// </summary>
    public static async Task<(int Done, IReadOnlyList<string> Problems)> ApplyImagesAsync(ContentEditor editor, HttpClient http, IObjectStore store, ImagePlan plan, CancellationToken ct = default)
    {
        var done = 0;
        var problems = new List<string>();
        foreach (var item in plan.Items)
        {
            ct.ThrowIfCancellationRequested();
            byte[] bytes;
            try
            {
                bytes = await DownloadAsync(http, item.ImageUrl, ct);
            }
            catch (TooManyRequestsException)
            {
                problems.Add("VRChat が 429（多すぎる）を返したので止めました。時間をおいて再実行すると、続きから行います");
                break;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                problems.Add($"{item.Name}: {Describe(e)}");
                continue;
            }
            try
            {
                await StoreImageAsync(editor, store, item.Id, item.Key, bytes, ct);
                done++;
            }
            catch (Exception e) when (!ct.IsCancellationRequested && IsPerWorldFailure(e))
            {
                problems.Add($"{item.Name}: 画像を保存できませんでした（{Describe(e)}）");        // 1 件の失敗で止めず、次へ進む（この件は次の実行でやり直せる）
            }
        }
        return (done, problems);
    }

    /// <summary>
    /// 画像（VRChat から読んだ元のバイト列）を WebP（512・1024）にして R2 に置き、worlds.json の image を書く。
    /// 取得（VRChat への問い合わせ）は呼び出し側。ApplyImagesAsync と WorldFetchService の共通。
    /// </summary>
    internal static async Task StoreImageAsync(ContentEditor editor, IObjectStore store, string worldId, string key, byte[] bytes, CancellationToken ct)
    {
        var encoded = WorldImageEncoder.Encode(bytes);
        foreach (var variant in encoded.Variants)
            await store.PutAsync(key + variant.Suffix, variant.Data, variant.ContentType, ObjectHeaders.ImmutableImage, ct);
        WriteImage(editor, worldId, new WorldImage { Key = key, W = encoded.W, H = encoded.H });
    }

    /// <summary>worlds.json の 1 つのワールドの image を書く（ほかの項目・順序は保つ）。書いたあと、ビルドと同じ検査を通す（通らなければ元に戻す）。</summary>
    public static void WriteImage(ContentEditor editor, string worldId, WorldImage image)
    {
        var snapshot = new FileSnapshot();
        snapshot.Capture(editor.WorldsPath);
        try
        {
            var worlds = ContentEditor.ReadObject(editor.WorldsPath);
            if (worlds[worldId] is not JsonObject master) throw new InvalidDataException($"worlds.json に {worldId} がありません");
            master.Remove("imageUrl");
            master["image"] = new JsonObject { ["key"] = image.Key, ["w"] = image.W, ["h"] = image.H };
            ContentEditor.WriteNode(editor.WorldsPath, worlds);
            editor.ValidateOrThrow();
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
    }
}
