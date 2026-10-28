using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>verify の対象: 0 件の理由・サンプルの注記・台帳の写真（--ledger）。読み取りだけ。</summary>
public sealed class VerifyScopeTests : IDisposable
{
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void DraftPostsAreCheckedLikeAnyOtherPostBecauseTheyAreShownInDevelop()
    {
        RepoFixture.Write(_fixture.PostPath("draft-only"), """
            {
              "schema": 1, "slug": "draft-only", "title": "下書き", "publishedAt": "2026-10-24T20:00:00+09:00",
              "draft": true, "noindex": false, "tags": [], "body": "", "photos": []
            }
            """);

        var scope = VerifyService.Plan(ContentLoader.Load(_fixture.ContentDir), null);

        Assert.Empty(scope.Photos);                                                          // この下書きには写真が無い
        Assert.Contains(scope.Notes, n => n.Contains("記事に写真がありません", StringComparison.Ordinal));      // 「記事がありません」ではない（下書きも記事として数える）
    }

    [Fact]
    public void LedgerOptionAddsUploadedPhotosThatAreNotInAnyPost()
    {
        _fixture.WriteExistingPost("old-post");
        var extraKey = "2026-10/20261025-091530-500_aaaaaaaa";
        var ledger = _fixture.Ledger(
            (RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"),
            (RepoFixture.Key2, "2222", LedgerStates.Uploaded, "2026-10-23T21:40:01+09:00"),
            (extraKey, "3333", LedgerStates.Uploaded, "2026-10-25T09:15:30+09:00"),
            ("2026-10/20261026-120000-000_bbbbbbbb", "4444", LedgerStates.Removed, "2026-10-26T12:00:00+09:00"));
        var content = ContentLoader.Load(_fixture.ContentDir);

        var without = VerifyService.Plan(content, null);
        var with = VerifyService.Plan(content, ledger);

        Assert.Equal(2, without.Photos.Count);
        Assert.Equal(3, with.Photos.Count);
        Assert.Equal(1, with.LedgerOnlyCount);
        Assert.Contains(with.Photos, p => p.Key == extraKey && p.Ledger);
        Assert.DoesNotContain(with.Photos, p => p.Key.EndsWith("bbbbbbbb", StringComparison.Ordinal));
        Assert.Contains(with.Notes, n => n.Contains("台帳（uploaded）だけにある写真 1 件", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RealPhotoNotFoundIsARealProblem()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key1);
        var scope = VerifyService.Plan(ContentLoader.Load(_fixture.ContentDir), null);
        using var http = new HttpClient(new FakeHandler(request =>
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { RequestMessage = request }));

        var problems = await VerifyService.RunAsync(scope.Photos, "https://img.example.test/", http);

        Assert.NotEmpty(problems);
    }

    [Fact]
    public void LedgerOptionSaysWhenThereIsNothingExtra()
    {
        _fixture.WriteExistingPost("old-post");
        var content = ContentLoader.Load(_fixture.ContentDir);

        var scope = VerifyService.Plan(content, _fixture.Ledger());

        Assert.Equal(0, scope.LedgerOnlyCount);
        Assert.Contains(scope.Notes, n => n.Contains("記事に無い写真はありません", StringComparison.Ordinal));
    }
}
