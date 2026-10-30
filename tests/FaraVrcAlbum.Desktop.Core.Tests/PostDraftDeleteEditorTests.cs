using System.Text.Json.Nodes;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>記事の「公開する」「非公開にする」と「削除」（デモの記事: demo-first は公開、demo-draft は下書き）。</summary>
public sealed class PostDraftDeleteEditorTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private string PostPath(string slug) => Path.Combine(_ws.Layout.RepoRoot, "content", "posts", slug + ".json");

    [Fact]
    public async Task UnpublishingAPublishedPostAsksFirstAndChangesOnlyTheDraftMark()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        Assert.True(editor.UnpublishCommand.CanExecute(null));
        Assert.False(editor.PublishCommand.CanExecute(null));                                                   // もう公開している
        var before = JsonNode.Parse(File.ReadAllText(PostPath("demo-first")))!;

        _ws.Dialogs.Answer = false;
        await editor.UnpublishCommand.RunAsync();
        Assert.False(JsonNode.Parse(File.ReadAllText(PostPath("demo-first")))!["draft"]!.GetValue<bool>());     // 断ったら変えない

        _ws.Dialogs.Answer = true;
        await editor.UnpublishCommand.RunAsync();

        var after = JsonNode.Parse(File.ReadAllText(PostPath("demo-first")))!;
        Assert.True(after["draft"]!.GetValue<bool>());
        Assert.Equal(before["body"]!.GetValue<string>(), after["body"]!.GetValue<string>());
        Assert.True(editor.Posts.Single(p => p.Slug == "demo-first").Draft);
        Assert.Contains("非公開にしました", editor.Status);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("非公開にします"));
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        Assert.False(editor.UnpublishCommand.CanExecute(null));
        Assert.True(editor.PublishCommand.CanExecute(null));                                                    // 今度は「公開する」を押せる
    }

    [Fact]
    public async Task PublishingAnUnpublishedPostAsksFirstAndRemovesOnlyTheDraftMark()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-draft");
        Assert.True(editor.PublishCommand.CanExecute(null));
        var before = JsonNode.Parse(File.ReadAllText(PostPath("demo-draft")))!;

        _ws.Dialogs.Answer = false;
        await editor.PublishCommand.RunAsync();
        Assert.True(JsonNode.Parse(File.ReadAllText(PostPath("demo-draft")))!["draft"]!.GetValue<bool>());     // 断ったら変えない

        _ws.Dialogs.Answer = true;
        await editor.PublishCommand.RunAsync();

        var after = JsonNode.Parse(File.ReadAllText(PostPath("demo-draft")))!;
        Assert.NotEqual(true, after["draft"]?.GetValue<bool>());
        Assert.Equal(before["body"]!.GetValue<string>(), after["body"]!.GetValue<string>());
        Assert.False(editor.Posts.Single(p => p.Slug == "demo-draft").Draft);
        Assert.Contains("公開にしました", editor.Status);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("記事を公開します"));
    }

    [Fact]
    public void NeitherButtonIsOfferedWithUnsavedChangesAndOnlyTheOneThatFitsTheStateIsOffered()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();

        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-draft");
        Assert.True(editor.PublishCommand.CanExecute(null));
        Assert.False(editor.UnpublishCommand.CanExecute(null));

        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        Assert.False(editor.PublishCommand.CanExecute(null));
        Assert.True(editor.UnpublishCommand.CanExecute(null));
        editor.TagsText = "あとで直した";
        Assert.False(editor.PublishCommand.CanExecute(null));
        Assert.False(editor.UnpublishCommand.CanExecute(null));                                                 // 先に保存するか取り消す
    }

    [Fact]
    public async Task DeletingAPostAsksFirstKeepsItsContentAndKeepsThePhotos()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-draft");
        var original = File.ReadAllText(PostPath("demo-draft"));
        Assert.True(editor.DeletePostCommand.CanExecute(null));

        _ws.Dialogs.Answer = false;
        await editor.DeletePostCommand.RunAsync();
        Assert.True(File.Exists(PostPath("demo-draft")));                                                       // 断ったら消さない

        _ws.Dialogs.Answer = true;
        await editor.DeletePostCommand.RunAsync();

        Assert.False(File.Exists(PostPath("demo-draft")));
        Assert.DoesNotContain(editor.Posts, p => p.Slug == "demo-draft");
        var kept = Directory.EnumerateFiles(Path.Combine(_ws.Layout.RepoRoot, "local", "deleted-posts"), "demo-draft.json", SearchOption.AllDirectories).Single();
        Assert.Equal(original, File.ReadAllText(kept));                                                         // 消す前の内容が残る
        Assert.Contains("削除しました", editor.Status);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("記事を削除します"));
    }
}
