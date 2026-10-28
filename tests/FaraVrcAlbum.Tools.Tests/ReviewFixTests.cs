using System.Net;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using TestImages = FaraVrcAlbum.Images.Tests.TestImages;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>PR #9 の指摘への対応: 本番以外は書かない・削除の途中失敗の表示・検査の問題の型・壊れた XMP。</summary>
public sealed class ReviewFixTests : IDisposable
{
    private const string Prod = "fara-vrc-photos";
    private const string Test = "fara-vrc-photos-test";
    private const string OtherUsr = "usr_11111111-2222-4333-8444-555555555555";

    private readonly RepoFixture _fixture = new();
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        _fixture.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public async Task PhotoRemoveCoverAndSetCoverRefuseTheTestBucketAndWriteNothing()
    {
        var ledger = _fixture.Ledger((RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"));
        var store = new MemoryObjectStore();
        var plan = RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/");      // 記事がまだ無いので、計画は作れる

        await Assert.ThrowsAsync<UsageException>(() => RemoveService.ApplyAsync(plan, Test, ledger, _fixture.LedgerPath, store, DateTimeOffset.UtcNow));
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal(LedgerStates.Uploaded, ledger.BySha["1111"].State);

        var coverPlan = new CoverService.CoverPlan(RepoFixture.Key1, ledger.BySha["1111"], Path.Combine(_dir.Path, "missing.png"), false);
        await Assert.ThrowsAsync<UsageException>(() => CoverService.ApplyAsync(coverPlan, Test, ledger, _fixture.LedgerPath, store));
        Assert.Equal(0, store.PutCount);

        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key2);
        var before = File.ReadAllText(_fixture.PostPath("old-post"));
        Assert.Throws<UsageException>(() => PostService.SetCover(_fixture.Editor, Test, "old-post", RepoFixture.Key1));
        Assert.Equal(before, File.ReadAllText(_fixture.PostPath("old-post")));
    }

    [Fact]
    public async Task PartialR2DeleteSaysHowFarItGotAndTheSameCommandFinishesIt()
    {
        var ledger = _fixture.Ledger((RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"));
        var store = new MemoryObjectStore();
        store.OnDelete = key => key.EndsWith("_1280.webp", StringComparison.Ordinal) ? new IOException("通信エラー（試験）") : null;

        var ex = await Assert.ThrowsAsync<RemoveService.RemoveIncompleteException>(() => RemoveService.ApplyAsync(
            RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/"), Prod, ledger, _fixture.LedgerPath, store, DateTimeOffset.UtcNow));

        Assert.Contains("件を削除済み", ex.Message);
        Assert.Contains("同じコマンドを再実行", ex.Message);
        Assert.Equal(LedgerStates.Uploaded, ledger.BySha["1111"].State);

        // 障害が直ったあとで同じ削除をやり直すと、完了する（削除は冪等）
        store.OnDelete = null;
        await RemoveService.ApplyAsync(RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/"), Prod, ledger, _fixture.LedgerPath, store, DateTimeOffset.UtcNow);
        Assert.Equal(LedgerStates.Removed, ledger.BySha["1111"].State);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task AFailedLedgerSaveDoesNotLeaveThePhotoMarkedRemovedInMemory()
    {
        var ledger = _fixture.Ledger((RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"));
        var store = new MemoryObjectStore();
        var plan = RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/");
        var badLedgerPath = Path.Combine(_dir.Path, "ledger-is-a-folder");       // 台帳の保存先がフォルダなので、保存は失敗する
        Directory.CreateDirectory(badLedgerPath);

        await Assert.ThrowsAnyAsync<Exception>(() => RemoveService.ApplyAsync(plan, Prod, ledger, badLedgerPath, store, DateTimeOffset.UtcNow));
        Assert.Equal(LedgerStates.Uploaded, ledger.BySha["1111"].State);         // 保存されていないのに removed と見えない
    }

    private static HttpResponseMessage Ok(HttpRequestMessage request, string contentType, string cacheControl, int width, int height) =>
        FakeImages.Response(request, contentType, cacheControl, FakeImages.WebpHeader(width, height));

    private static string TypeFor(HttpRequestMessage request) =>
        request.RequestUri!.ToString().EndsWith("_og.jpg", StringComparison.Ordinal) ? "image/jpeg" : "image/webp";

    [Fact]
    public async Task WrongContentTypeIsAProblem()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key1);
        var scope = VerifyService.Plan(ContentLoader.Load(_fixture.ContentDir), null);
        using var http = new HttpClient(new FakeHandler(request =>
            Ok(request, "image/png", "public, max-age=31536000, immutable", 640, 360)));

        var problems = await VerifyService.RunAsync(scope.Photos, "https://img.example.test/", http);

        Assert.NotEmpty(problems);
        Assert.All(problems, p => Assert.Contains("Content-Type", p.Message));
    }

    [Fact]
    public async Task WrongCacheControlIsAProblem()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key1);
        var scope = VerifyService.Plan(ContentLoader.Load(_fixture.ContentDir), null);
        using var http = new HttpClient(new FakeHandler(request =>
            FakeImages.Response(request, TypeFor(request), "no-cache", FakeImages.WebpHeader(640, 360))));

        var problems = await VerifyService.RunAsync(scope.Photos, "https://img.example.test/", http);

        Assert.NotEmpty(problems);
        Assert.All(problems, p => Assert.Contains("Cache-Control", p.Message));
    }

    [Fact]
    public async Task DimensionMismatchIsAProblem()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key1);
        var scope = VerifyService.Plan(ContentLoader.Load(_fixture.ContentDir), null);
        using var http = new HttpClient(new FakeHandler(request =>
            FakeImages.Response(request, TypeFor(request), "public, max-age=31536000, immutable",
                FakeImages.WebpHeader(100, 100))));

        var problems = await VerifyService.RunAsync(scope.Photos, "https://img.example.test/", http);

        // 2048 の寸法だけを見る（記事の 640x360 と 1280x720 の 2 枚）
        Assert.Equal(2, problems.Count(p => p.Suffix == "_2048.webp" && p.Message.Contains("寸法が JSON と違います", StringComparison.Ordinal)));
    }

    [Fact]
    public void BrokenXmpIsListedAsUnreadableInsteadOfThrowing()
    {
        var file = _dir.File("VRChat_2026-10-23_21-33-12.345_640x360.png");
        File.WriteAllBytes(file, TestImages.Png(64, 36, "<not xml"));

        var row = InboxService.Describe(file, null, PhotoLedger.Empty(), HashCache.Load(_dir.File("cache.json")));

        Assert.NotNull(row.Problem);
        Assert.Null(row.TakenAt);
        Assert.Equal("未登録", row.LedgerState);
    }
}
