using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>記事の「下書きにする・戻す」と「削除」。</summary>
public sealed class PostDraftDeleteTests : IDisposable
{
    private readonly RepoFixture _repo = new();
    private readonly TempDir _work = new();

    public PostDraftDeleteTests() => _repo.WriteExistingPost("old-post");

    public void Dispose()
    {
        _repo.Dispose();
        _work.Dispose();
    }

    private ContentEditor Editor => new(_repo.ContentDir);

    [Fact]
    public void SettingDraftChangesOnlyTheDraftMarkAndTheLoaderSeesIt()
    {
        var before = JsonNode.Parse(File.ReadAllText(_repo.PostPath("old-post")))!.AsObject();

        PostEditService.SetDraft(Editor, "old-post", draft: true);

        var after = JsonNode.Parse(File.ReadAllText(_repo.PostPath("old-post")))!.AsObject();
        Assert.True(after["draft"]!.GetValue<bool>());
        Assert.Equal(before["body"]!.GetValue<string>(), after["body"]!.GetValue<string>());                     // 本文などは変えない
        Assert.Equal(before["title"]!.GetValue<string>(), after["title"]!.GetValue<string>());
        Assert.True(ContentLoader.Load(_repo.ContentDir).Posts.Single(p => p.Slug == "old-post").Draft);
    }

    [Fact]
    public void ClearingDraftPublishesTheArticleAgain()
    {
        PostEditService.SetDraft(Editor, "old-post", draft: true);

        PostEditService.SetDraft(Editor, "old-post", draft: false);

        Assert.False(ContentLoader.Load(_repo.ContentDir).Posts.Single(p => p.Slug == "old-post").Draft);
    }

    [Fact]
    public void SettingDraftOnAMissingPostIsAnError()
    {
        Assert.Throws<FileNotFoundException>(() => PostEditService.SetDraft(Editor, "no-such-post", draft: true));
    }

    [Fact]
    public void DeletingARemovesThePostFileAfterKeepingItsContentAndLeavesTheOthersAlone()
    {
        _repo.WriteExistingPost("other-post", cover: null);
        var original = File.ReadAllText(_repo.PostPath("old-post"));
        var backup = _work.File("deleted");

        PostEditService.Delete(Editor, "old-post", backup);

        Assert.False(File.Exists(_repo.PostPath("old-post")));
        Assert.Equal(original, File.ReadAllText(Path.Combine(backup, "old-post.json")));                         // 消す前の内容が残る
        Assert.True(File.Exists(_repo.PostPath("other-post")));
        Assert.DoesNotContain(ContentLoader.Load(_repo.ContentDir).Posts, p => p.Slug == "old-post");
    }

    [Fact]
    public void DeletingAMissingPostIsAnErrorAndLeavesNoBackup()
    {
        var backup = _work.File("deleted");

        Assert.Throws<FileNotFoundException>(() => PostEditService.Delete(Editor, "no-such-post", backup));

        Assert.False(Directory.Exists(backup));
    }
}
