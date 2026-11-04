using System.Net;
using System.Text;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>ワールドの画像・情報を、足りない分だけまとめて取る（アプリの「ワールドの画像・情報を取得する」）。</summary>
public class WorldFetchTests
{
    private const string ImageUrl = "https://api.vrchat.cloud/api/1/file/file_00000000-0000-4000-8000-0000000000aa/3/file";
    private const string W2 = "wrld_00000000-0000-4000-8000-000000000002";
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

    private static string Info(long visits = 123456) => $$"""
        { "id": "x", "name": "n", "authorName": "テスト作者", "description": "説明", "capacity": 80, "recommendedCapacity": 40,
          "visits": {{visits}}, "favorites": 4321, "tags": ["author_tag_Chill"],
          "publicationDate": "2025-01-04T15:00:00.800Z", "updated_at": "2025-03-10T03:00:00.100Z", "imageUrl": "{{ImageUrl}}" }
        """;

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Task NoWaitAsync(CancellationToken _) => Task.CompletedTask;

    private static byte[] Png()
    {
        using var img = NetVips.Image.Black(1200, 900, bands: 3);
        return img.PngsaveBuffer();
    }

    /// <summary>情報の問い合わせは Info()、画像は PNG を返す。問い合わせの記録つき。</summary>
    private static (HttpClient Http, List<string> Asked) Client(Func<string, HttpResponseMessage?>? custom = null)
    {
        var asked = new List<string>();
        var png = Png();
#pragma warning disable CA2000 // 作った HttpClient（とハンドラー）を呼び出し側のテストへ返す。短命のテスト用オブジェクトで、各テストの終わりまで使う（破棄しても結果は変わらない）
        var http = WorldImageService.CreateClient(new FakeHandler(r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            asked.Add(path);
            if (custom?.Invoke(path) is { } response) return response;
            return path.StartsWith("/api/1/worlds/", StringComparison.Ordinal) ? Json(Info()) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) };
        }), NoWaitAsync);
#pragma warning restore CA2000
        return (http, asked);
    }

    private static string StateFile(RepoFixture repo) => Path.Combine(repo.ContentDir, "..", "local-test", "world-fetch-state.json");

    private static void Worlds(RepoFixture repo, bool secondWorld = false) => RepoFixture.Write(repo.Worlds, secondWorld
        ? $$"""
            { "{{RepoFixture.WorldOld}}": { "name": "一つ目", "aliases": [] }, "{{W2}}": { "name": "二つ目", "aliases": [] } }
            """
        : $$"""
            { "{{RepoFixture.WorldOld}}": { "name": "一つ目", "aliases": [] } }
            """);

    private static Func<(IObjectStore Store, IDisposable? Owner)> Store(out MemoryStoreHandle store)
    {
        var s = new MemoryStoreHandle();
        store = s;
        return () => (s, null);
    }

    [Fact]
    public async Task OneInfoRequestPerWorldGivesBothTheImageAndTheStats()
    {
        using var repo = new RepoFixture();
        Worlds(repo);
        var (http, asked) = Client();
        var log = new List<string>();

        var result = await WorldFetchService.FetchAsync(repo.Editor, http, Store(out var store), StateFile(repo), Now, retryFailed: false, log.Add);

        Assert.Equal((1, 1), (result.Images, result.Stats));
        Assert.Equal(1, asked.Count(p => p.StartsWith("/api/1/worlds/", StringComparison.Ordinal)));       // 情報の問い合わせは 1 回だけ（画像と情報で 2 回取らない）
        Assert.Equal(2, store.Objects.Count);                                                               // 512・1024
        var content = repo.Editor.LoadContent();
        Assert.NotNull(content.Worlds[RepoFixture.WorldOld].Image);
        Assert.Equal(123456, content.WorldStats!.Worlds[RepoFixture.WorldOld].Visits);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public async Task WorldsThatHaveEverythingAreNotAskedAbout()
    {
        using var repo = new RepoFixture();
        Worlds(repo);
        var (http, _) = Client();
        await WorldFetchService.FetchAsync(repo.Editor, http, Store(out _), StateFile(repo), Now, false, _ => { });

        var (http2, asked2) = Client();
        var log = new List<string>();
        var result = await WorldFetchService.FetchAsync(repo.Editor, http2, () => throw new InvalidOperationException("R2 は開かない"), StateFile(repo), Now, false, log.Add);

        Assert.Equal((0, 0), (result.Images, result.Stats));
        Assert.Empty(asked2);
        Assert.Contains(log, l => l.Contains("取得するものはありません"));
    }

    [Fact]
    public async Task OnlyTheMissingPartIsFetchedAndTheDateOfTheLastFullFetchIsKept()
    {
        using var repo = new RepoFixture();
        Worlds(repo, secondWorld: true);
        var (first, _) = Client();
        await WorldFetchService.FetchAsync(repo.Editor, first, Store(out _), StateFile(repo), Now.AddDays(-8), false, _ => { });
        var fetchedAt = repo.Editor.LoadContent().WorldStats!.FetchedAt;

        // 3 つ目を足す（1・2 つ目は、画像も情報もあるので、そのまま）
        var worlds = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(repo.Worlds))!.AsObject();
        worlds["wrld_00000000-0000-4000-8000-000000000003"] = new System.Text.Json.Nodes.JsonObject { ["name"] = "三つ目", ["aliases"] = new System.Text.Json.Nodes.JsonArray() };
        File.WriteAllText(repo.Worlds, worlds.ToJsonString());
        var (http, asked) = Client();
        var result = await WorldFetchService.FetchAsync(repo.Editor, http, Store(out _), StateFile(repo), Now, false, _ => { });

        Assert.Equal(1, result.Stats);
        Assert.Equal(fetchedAt, repo.Editor.LoadContent().WorldStats!.FetchedAt);                          // 足りない分だけ取っても、日時は前の全体の取得のまま
        Assert.Equal(1, asked.Count(p => p.StartsWith("/api/1/worlds/", StringComparison.Ordinal)));
        Assert.NotNull(repo.Editor.LoadContent().Worlds[RepoFixture.WorldOld].Image);
    }

    [Fact]
    public async Task AFailedWorldIsRememberedAndNotAskedAboutAgainUntilRetriedOrOldEnough()
    {
        using var repo = new RepoFixture();
        Worlds(repo);
        var state = StateFile(repo);
        var (failing, askedFailing) = Client(path => path.StartsWith("/api/1/worlds/", StringComparison.Ordinal) ? Json("{}", HttpStatusCode.NotFound) : null);
        var log = new List<string>();

        var first = await WorldFetchService.FetchAsync(repo.Editor, failing, Store(out _), state, Now, false, log.Add);

        Assert.Single(first.Problems);
        Assert.Contains("404", first.Problems[0]);
        Assert.True(WorldFetchService.State.Load(state).IsWaiting(RepoFixture.WorldOld, Now));

        // すぐ後（待ち時間の内）は、問い合わせない
        var (again, askedAgain) = Client();
        log.Clear();
        var second = await WorldFetchService.FetchAsync(repo.Editor, again, Store(out _), state, Now.AddHours(1), false, log.Add);
        Assert.Empty(askedAgain);
        Assert.Contains(log, l => l.Contains("問い合わせません"));
        Assert.Equal((0, 0), (second.Images, second.Stats));
        var pending = WorldFetchService.CountPending(repo.Editor, state, Now.AddHours(1), retryFailed: false);
        Assert.Equal((0, 1), (pending.Worlds, pending.Waiting));

        // 「もう一度取得する」なら、待たずに取り、成功したら記録を消す
        var (retry, askedRetry) = Client();
        var third = await WorldFetchService.FetchAsync(repo.Editor, retry, Store(out _), state, Now.AddHours(1), retryFailed: true, _ => { });
        Assert.Equal(1, third.Images);
        Assert.NotEmpty(askedRetry);
        Assert.False(WorldFetchService.State.Load(state).Failures.ContainsKey(RepoFixture.WorldOld));
    }

    [Fact]
    public async Task AFailureOlderThanTheWaitIsTriedAgain()
    {
        using var repo = new RepoFixture();
        Worlds(repo);
        var state = StateFile(repo);
        var (failing, _) = Client(path => path.StartsWith("/api/1/worlds/", StringComparison.Ordinal) ? Json("{}", HttpStatusCode.NotFound) : null);
        await WorldFetchService.FetchAsync(repo.Editor, failing, Store(out _), state, Now, false, _ => { });

        var (http, asked) = Client();
        var result = await WorldFetchService.FetchAsync(repo.Editor, http, Store(out _), state, Now + WorldFetchService.RetryAfter + TimeSpan.FromMinutes(1), false, _ => { });

        Assert.NotEmpty(asked);
        Assert.Equal(1, result.Images);
    }

    [Fact]
    public async Task Status429StopsWithoutRecordingAFailure()
    {
        using var repo = new RepoFixture();
        Worlds(repo, secondWorld: true);
        var (http, asked) = Client(path => path.StartsWith("/api/1/worlds/", StringComparison.Ordinal) ? Json("{}", HttpStatusCode.TooManyRequests) : null);

        var result = await WorldFetchService.FetchAsync(repo.Editor, http, Store(out _), StateFile(repo), Now, false, _ => { });

        Assert.True(result.Stopped);
        Assert.Single(asked);                                                                               // 2 つ目には進まない
        Assert.Contains("429", Assert.Single(result.Problems));
        Assert.Empty(WorldFetchService.State.Load(StateFile(repo)).Failures);                               // 429 は失敗に数えない（次はすぐ取れる）
    }

    [Fact]
    public async Task WhenR2CannotBeOpenedOnlyTheStatsAreFetchedAndNothingIsRecordedAsFailed()
    {
        using var repo = new RepoFixture();
        Worlds(repo);
        var (http, asked) = Client();
        var log = new List<string>();

        var result = await WorldFetchService.FetchAsync(repo.Editor, http,
            () => throw new UsageException("R2_ACCESS_KEY_ID がありません"), StateFile(repo), Now, false, log.Add);

        Assert.Equal((0, 1), (result.Images, result.Stats));
        Assert.Single(asked);                                                                       // 画像は取りに行かない
        Assert.Contains(log, l => l.Contains("R2_ACCESS_KEY_ID") && l.Contains("情報だけ"));
        Assert.Null(repo.Editor.LoadContent().Worlds[RepoFixture.WorldOld].Image);
        Assert.Empty(WorldFetchService.State.Load(StateFile(repo)).Failures);
    }

    [Fact]
    public async Task AMalformedAnswerIsRecordedAsAFailureAndTheFetchContinues()
    {
        using var repo = new RepoFixture();
        Worlds(repo);
        var (http, _) = Client(path => path.StartsWith("/api/1/worlds/", StringComparison.Ordinal)
            ? Json("{ not json")
            : null);

        var result = await WorldFetchService.FetchAsync(repo.Editor, http, Store(out _), StateFile(repo), Now, false, _ => { });

        Assert.Single(result.Problems);                                                                     // 壊れた応答も、失敗として記録して続ける
        Assert.True(WorldFetchService.State.Load(StateFile(repo)).Failures.ContainsKey(RepoFixture.WorldOld));
    }

    [Fact]
    public void CountingPendingWorkDoesNotAskVrchatAndEstimatesTheMinutes()
    {
        using var repo = new RepoFixture();
        Worlds(repo, secondWorld: true);

        var pending = WorldFetchService.CountPending(repo.Editor, StateFile(repo), Now, retryFailed: false);

        Assert.Equal((2, 2, 2, 0), (pending.Worlds, pending.Images, pending.Stats, pending.Waiting));
        Assert.Equal(4, pending.Minutes);                                                                   // 問い合わせ 2 回 + 画像 2 回
    }

    private sealed class MemoryStoreHandle : IObjectStore
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(Objects.ContainsKey(key));
        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult(Objects.GetValueOrDefault(key));
        public Task PutAsync(string key, byte[] data, string contentType, string cacheControl, CancellationToken ct = default) { Objects[key] = data; return Task.CompletedTask; }
        public Task DeleteAsync(string key, CancellationToken ct = default) { Objects.Remove(key); return Task.CompletedTask; }
        public async IAsyncEnumerable<(string Key, long Size)> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var (k, v) in Objects.Where(o => o.Key.StartsWith(prefix, StringComparison.Ordinal))) yield return (k, v.Length);
            await Task.CompletedTask;
        }
    }
}
