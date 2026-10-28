using System.Net;
using System.Text;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>album worlds（ワールドの画像を R2 へ）と album world-stats（ワールドの情報を content/world-stats.json へ）。</summary>
public class WorldImageTests
{
    private const string ImageUrl = "https://api.vrchat.cloud/api/1/file/file_00000000-0000-4000-8000-0000000000aa/3/file";
    private const string W2 = "wrld_00000000-0000-4000-8000-000000000002";
    private const string W3 = "wrld_00000000-0000-4000-8000-000000000003";

    private static string Info(long visits = 123456) => $$"""
        { "id": "x", "name": "n", "authorName": "テスト作者", "authorId": "usr_00000000-0000-4000-8000-0000000000bb",
          "description": "説明", "capacity": 80, "recommendedCapacity": 40, "visits": {{visits}}, "favorites": 4321,
          "popularity": 7, "heat": 5, "occupants": 3, "publicOccupants": 3,
          "tags": ["author_tag_Chill", "system_approved", "author_tag_Field"],
          "publicationDate": "2025-01-04T15:00:00.800Z", "updated_at": "2025-03-10T03:00:00.100Z",
          "imageUrl": "{{ImageUrl}}" }
        """;

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Task NoWait(CancellationToken _) => Task.CompletedTask;

    private static void ThreeWorlds(RepoFixture repo, string firstImage = "") => RepoFixture.Write(repo.Worlds, $$"""
        {
          "{{RepoFixture.WorldOld}}": { "name": "一つ目", "aliases": []{{firstImage}} },
          "{{W2}}": { "name": "二つ目", "aliases": [] },
          "{{W3}}": { "name": "三つ目", "aliases": [] }
        }
        """);

    [Fact]
    public async Task TheClientNamesItselfAndWaitsBetweenEveryRequestButNotBeforeTheFirst()
    {
        var agents = new List<string>();
        var waits = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(r => { agents.Add(r.Headers.UserAgent.ToString()); return Json(Info()); }), _ => { waits++; return Task.CompletedTask; });

        for (var i = 0; i < 3; i++) await WorldImageService.FetchInfoAsync(http, RepoFixture.WorldOld);

        Assert.Equal(2, waits);
        Assert.All(agents, a => Assert.Equal("FaraVrcAlbum/1.0 (Fara; game_fara@outlook.com)", a));
        Assert.Equal(60, WorldImageService.Interval.TotalSeconds);
    }

    [Fact]
    public async Task TheWaitBetweenRequestsDoesNotCountTowardsTheRequestTimeout()
    {
        // 待ち時間（本番は 60 秒）が、通信の時間切れ（本番は 30 秒）を食わない
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => Json(Info())), c => Task.Delay(300, c), requestTimeout: TimeSpan.FromMilliseconds(200));
        for (var i = 0; i < 3; i++) await WorldImageService.FetchInfoAsync(http, RepoFixture.WorldOld);
    }

    [Fact]
    public async Task StatsKeepOnlyWhatTheSiteShowsAndDropTheRealtimeCountAndUserIds()
    {
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => Json(Info())), NoWait);
        var info = await WorldImageService.FetchInfoAsync(http, RepoFixture.WorldOld);

        Assert.Equal(ImageUrl, info.ImageUrl);
        var s = info.Stats;
        Assert.Equal(("テスト作者", "説明", 80, 40, 123456L, 4321L), (s.AuthorName, s.Description, s.Capacity, s.RecommendedCapacity, s.Visits, s.Favorites));
        Assert.Equal(["Chill", "Field"], s.Tags);                                         // author_tag_ だけ・接頭辞を外す
        Assert.Equal("2025-01-04T15:00:00Z", s.PublicationDate);
        Assert.Equal("2025-03-10T03:00:00Z", s.UpdatedAt);
        var json = System.Text.Json.JsonSerializer.Serialize(s, JsonFiles.Options);
        Assert.DoesNotContain("usr_", json);                                             // 作者の ID は持たない
        Assert.DoesNotContain("ccupants", json);                                         // 今いる人数は持たない
        Assert.DoesNotContain("popularity", json);
        Assert.DoesNotContain("heat", json);
    }

    [Fact]
    public async Task WorldStatsWritesAllWorldsAndStopsAt429KeepingWhatItGot()
    {
        using var repo = new RepoFixture();
        ThreeWorlds(repo);
        var calls = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => ++calls <= 2 ? Json(Info(100 + calls)) : Json("{}", HttpStatusCode.TooManyRequests)), NoWait);
        var now = new DateTimeOffset(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

        var result = await WorldImageService.UpdateStatsAsync(repo.Editor, http, now, apply: true);

        Assert.Equal(2, result.Updated);
        Assert.Equal(1, result.Remaining);
        Assert.Contains("429", Assert.Single(result.Problems));
        var stats = repo.Editor.LoadContent().WorldStats!;
        Assert.Equal("2026-10-26T12:00:00+09:00", stats.FetchedAt);
        Assert.Equal([RepoFixture.WorldOld, W2], stats.Worlds.Keys.Order());
        Assert.Equal(101, stats.Worlds[RepoFixture.WorldOld].Visits);
    }

    [Fact]
    public async Task WorldStatsWithoutApplyWritesNothing()
    {
        using var repo = new RepoFixture();
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => Json(Info())), NoWait);
        var result = await WorldImageService.UpdateStatsAsync(repo.Editor, http, DateTimeOffset.UtcNow, apply: false);
        Assert.Equal(1, result.Updated);
        Assert.False(File.Exists(Path.Combine(repo.ContentDir, "world-stats.json")));
    }

    [Fact]
    public async Task ImagesArePlannedOnlyForWorldsWithoutOneAndStopAt429()
    {
        using var repo = new RepoFixture();
        ThreeWorlds(repo, firstImage: """, "image": { "key": "worlds/00000000-0000-4000-8000-000000000001/file_00000000-0000-4000-8000-0000000000aa_3", "w": 1024, "h": 768 }""");
        var asked = new List<string>();
        using var http = WorldImageService.CreateClient(new FakeHandler(r =>
        {
            asked.Add(r.RequestUri!.ToString());
            return asked.Count == 1 ? Json(Info()) : Json("{}", HttpStatusCode.TooManyRequests);
        }), NoWait);

        var plan = await WorldImageService.PlanImagesAsync(repo.Editor, http, refresh: false);

        Assert.DoesNotContain(asked, u => u.EndsWith(RepoFixture.WorldOld));            // 画像のあるワールドは問い合わせない
        Assert.Equal(1, plan.Unchanged);
        var item = Assert.Single(plan.Items);
        Assert.Equal((W2, "worlds/00000000-0000-4000-8000-000000000002/file_00000000-0000-4000-8000-0000000000aa_3"), (item.Id, item.Key));
        Assert.Equal(1, plan.Remaining);
        Assert.Contains("429", Assert.Single(plan.Problems));
    }

    [Fact]
    public async Task ApplyingPutsBothSizesInR2AndWritesTheImageToWorldsJson()
    {
        using var repo = new RepoFixture();
        var png = FakePng(1200, 900);
        using var http = WorldImageService.CreateClient(new FakeHandler(r => r.RequestUri!.AbsolutePath.StartsWith("/api/1/worlds/")
            ? Json(Info())
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) }), NoWait);
        var store = new MemoryStore();

        var plan = await WorldImageService.PlanImagesAsync(repo.Editor, http, refresh: false);
        var (done, problems) = await WorldImageService.ApplyImagesAsync(repo.Editor, http, store, plan);

        Assert.Equal(1, done);
        Assert.Empty(problems);
        var key = "worlds/00000000-0000-4000-8000-000000000001/file_00000000-0000-4000-8000-0000000000aa_3";
        Assert.Equal([key + "_1024.webp", key + "_512.webp"], store.Objects.Keys.Order());
        var image = repo.Editor.LoadContent().Worlds[RepoFixture.WorldOld].Image!;
        Assert.Equal((key, 1024, 768), (image.Key, image.W, image.H));
        Assert.Contains("\"name\": \"旧名のワールド\"", File.ReadAllText(repo.Worlds));    // ほかの項目は保つ
    }

    [Fact]
    public async Task ANetworkFailureShowsTheInnerReasonToo()
    {
        using var repo = new RepoFixture();
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => throw new HttpRequestException("The SSL connection could not be established", new IOException("接続が切られました"))), NoWait);

        var plan = await WorldImageService.PlanImagesAsync(repo.Editor, http, refresh: false);

        var problem = Assert.Single(plan.Problems);
        Assert.Contains("The SSL connection could not be established", problem);
        Assert.Contains("接続が切られました", problem);
    }

    [Fact]
    public async Task AConnectionThatIsCutIsRetriedUpToThreeTimesWithTheUsualWait()
    {
        var calls = 0;
        var waits = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(_ =>
            ++calls < 3 ? throw new HttpRequestException("ssl", new IOException("切断")) : Json(Info())), _ => { waits++; return Task.CompletedTask; });

        var info = await WorldImageService.FetchInfoAsync(http, RepoFixture.WorldOld);

        Assert.Equal((3, 2, ImageUrl), (calls, waits, info.ImageUrl));          // 3 回目で取れる・やり直しの前に毎回待つ
    }

    [Fact]
    public async Task AConnectionThatKeepsBeingCutFailsAfterThreeTriesAndSaysSo()
    {
        var calls = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => { calls++; throw new HttpRequestException("ssl", new IOException("切断")); }), NoWait);

        var e = await Assert.ThrowsAsync<HttpRequestException>(() => WorldImageService.FetchInfoAsync(http, RepoFixture.WorldOld));

        Assert.Equal(3, calls);
        Assert.Contains("3 回試しました", e.Message);
    }

    [Fact]
    public async Task ABadAnswerIsNotRetried()
    {
        var calls = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => { calls++; return Json("{}", HttpStatusCode.NotFound); }), NoWait);

        await Assert.ThrowsAsync<HttpRequestException>(() => WorldImageService.FetchInfoAsync(http, RepoFixture.WorldOld));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RedirectsAreFollowedWithoutWaitingAndAFailureNamesTheHost()
    {
        var waits = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(r => r.RequestUri!.Host == "api.vrchat.cloud"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://files.vrchat.cloud/x.png") } }
            : throw new HttpRequestException("ssl", new IOException("切断"))), _ => { waits++; return Task.CompletedTask; });

        var e = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("https://api.vrchat.cloud/api/1/file/a/1/file"));

        Assert.Contains("files.vrchat.cloud への通信に失敗", e.Message);       // 転送先のホストが分かる
        Assert.Equal(2, waits);                                                  // 待つのは試し直しの前だけ（転送の間は待たない）
    }

    [Fact]
    public void Ipv4AddressesAreTriedBeforeIpv6ButIpv6IsKeptAsAFallback()
    {
        var v6a = System.Net.IPAddress.Parse("2600:9000:2025:6a00:1d:2ee2:7dc0:93a1");
        var v4a = System.Net.IPAddress.Parse("54.239.163.42");
        var v6b = System.Net.IPAddress.Parse("2600:9000:2025:600:1d:2ee2:7dc0:93a1");
        var v4b = System.Net.IPAddress.Parse("54.239.163.30");

        var ordered = WorldImageService.Ipv4First([v6a, v4a, v6b, v4b]).ToList();

        Assert.Equal([v4a, v4b, v6a, v6b], ordered);                     // IPv4 が先・それぞれの元の順は保つ
    }

    [Fact]
    public async Task AStalledResponseBodyTimesOutInsteadOfWaitingForever()
    {
        var stalled = new StalledStream();

        var e = await Assert.ThrowsAsync<HttpRequestException>(() => WorldImageService.ReadBodyAsync(
            t => new StreamContent(stalled).ReadAsByteArrayAsync(t), TimeSpan.FromMilliseconds(200), CancellationToken.None));

        Assert.Contains("本文", e.Message);
    }

    [Fact]
    public async Task CancellingWhileReadingTheBodyIsNotReportedAsATimeout()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorldImageService.ReadBodyAsync(
            t => new StreamContent(new StalledStream()).ReadAsByteArrayAsync(t), TimeSpan.FromSeconds(30), cts.Token));
    }

    [Fact]
    public async Task CancellationStopsTheStatsLoopInsteadOfRecordingEveryWorldAsFailed()
    {
        using var repo = new RepoFixture();
        ThreeWorlds(repo);
        using var cts = new CancellationTokenSource();
        var calls = 0;
        using var http = WorldImageService.CreateClient(new FakeHandler(_ => { if (++calls == 1) cts.Cancel(); return Json(Info()); }), NoWait);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorldImageService.UpdateStatsAsync(repo.Editor, http, DateTimeOffset.UtcNow, apply: false, ct: cts.Token));
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);                                  // 本文の途中で止まる（キャンセルされるまで返らない）
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void LongTextIsCutWithoutSplittingASurrogatePair()
    {
        var text = new string('あ', 9) + "😀" + "続き";           // 9 文字のあと、絵文字（UTF-16 で 2 つ分）
        Assert.Equal(new string('あ', 9), WorldImageService.Limit(text, 10));          // 10 で切ると絵文字の前半になるので、手前で切る
        Assert.Equal(new string('あ', 9) + "😀", WorldImageService.Limit(text, 11));
        Assert.Equal("あいう", WorldImageService.Limit("あいう", 3));
    }

    private static byte[] FakePng(int w, int h)
    {
        using var img = NetVips.Image.Black(w, h, bands: 3);
        return img.PngsaveBuffer();
    }

    private sealed class MemoryStore : IObjectStore
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
