using System.Text.Json.Nodes;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>記事の編集・プレビュー・設定のフォルダがすぐ反映されること。</summary>
public sealed class EditorAndPreviewTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private string PostPath(string slug) => Path.Combine(_ws.Layout.RepoRoot, "content", "posts", slug + ".json");

    [Fact]
    public async Task TheEditorWritesOneDocumentWithPhotoLinesAndSavesItIntoTheParts()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        Assert.False(editor.IsDirty);
        Assert.Matches(@"!\[[^\]]*\]\(https://demo\.invalid/2026-10/[^)]+_1280\.webp\)", editor.Document);   // 写真は ![説明](URL) の行
        Assert.Contains(editor.PreviewBlocks, b => b is PreviewPhotoBlock);
        Assert.DoesNotContain(editor.PreviewBlocks.OfType<PreviewPhotoBlock>(), b => b.AutoText.Contains("服:"));   // 服の紐づけが無ければ出さない

        // 写真の行の前後に文章を足す
        var lines = editor.Document.Split('\n').ToList();
        var lastPhoto = lines.FindLastIndex(l => l.StartsWith("![", StringComparison.Ordinal));
        lines.Insert(lastPhoto, "ここから **夜**");
        lines.Insert(lastPhoto + 1, "");
        lines.Insert(0, "はじめの文章\n");
        lines.Add("\nおわり");
        editor.Document = string.Join("\n", lines);
        Assert.Equal("", editor.DocumentError);
        Assert.Contains(editor.PreviewBlocks.OfType<PreviewTextBlock>(), b => b.Text == "ここから **夜**");
        editor.TagsText = "夜景、雪景色";
        Assert.True(editor.IsDirty);

        var before = File.ReadAllText(PostPath("demo-first"));
        _ws.Dialogs.Answer = false;
        await editor.SaveCommand.RunAsync();
        Assert.Equal(before, File.ReadAllText(PostPath("demo-first")));                   // 断ったら書かない

        _ws.Dialogs.Answer = true;
        await editor.SaveCommand.RunAsync();
        var post = JsonNode.Parse(File.ReadAllText(PostPath("demo-first")))!;
        Assert.StartsWith("はじめの文章\n\n", post["body"]!.GetValue<string>());          // 元の本文の前に足した
        Assert.Equal("おわり", post["afterword"]!.GetValue<string>());
        Assert.Equal("ここから **夜**", post["photos"]!.AsArray()[^1]!["text"]!.GetValue<string>());
        Assert.Equal(2, post["tags"]!.AsArray().Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void RemovingAPhotoLineIsReportedAndBlocksSaving()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        editor.Document = string.Join("\n", editor.Document.Split('\n').Where(l => !l.StartsWith("![", StringComparison.Ordinal)).Skip(0)) + "\n" +
                          editor.Document.Split('\n').First(l => l.StartsWith("![", StringComparison.Ordinal));
        Assert.Contains("行がありません", editor.DocumentError);
        Assert.False(editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task UncheckingDraftPublishesThePostAndTheDialogSaysSo()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        var draft = editor.Posts.First(p => p.Draft);
        editor.SelectedPost = draft;
        Assert.True(editor.IsDraft);

        editor.IsDraft = false;
        _ws.Dialogs.Answer = true;
        await editor.SaveCommand.RunAsync();

        Assert.False(JsonNode.Parse(File.ReadAllText(PostPath(draft.Slug)))!["draft"]!.GetValue<bool>());
        Assert.Contains("公開する記事にしました", editor.Status);
    }

    [Fact]
    public async Task AnInvalidEditIsRejectedBeforeAsking()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        editor.PublishedAt = "あした";
        Assert.True(editor.SaveCommand.CanExecute(null));
        _ws.Dialogs.Answer = true;
        await editor.SaveCommand.RunAsync();

        Assert.Empty(_ws.Dialogs.Asked);
        Assert.False(string.IsNullOrEmpty(editor.Status) || editor.Status.StartsWith("エラー（"), editor.Status);   // 型名は付けず、文だけ出す
    }

    [Fact]
    public async Task PreviewsFollowTheSelection()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        Assert.Null(ingest.PreviewPath);
        ingest.SelectedItem = ingest.Items[0];
        Assert.Equal(ingest.Items[0].Path, ingest.PreviewPath);

        // アップロード済みの写真は、保存先の URL（デモは接続しないので出さない）
        var online = new AlbumSession(RepoContext.Discover(_ws.Layout.RepoRoot), _ws.Layout.UploadRoot, offline: false, sourceRoot: _ws.Layout.SourceRoot);
        Assert.Equal("https://demo.invalid/2026-10/x_1280.webp", online.PhotoPreviewUrl("2026-10/x"));
        Assert.Null(_ws.Session.PhotoPreviewUrl("2026-10/x"));
    }

    [Fact]
    public void ChangingTheUploadFolderInSettingsTakesEffectWithoutRestarting()
    {
        var session = new AlbumSession(RepoContext.Discover(_ws.Layout.RepoRoot), null, offline: true);
        var before = session.UploadRoot;
        var next = Path.Combine(_ws.Layout.RepoRoot, "..", "other-upload");
        LocalSettings.SetLocalConfig(session.Repo.LocalConfigPath, new Dictionary<string, string?> { ["uploadRoot"] = next });
        session.Reload();

        Assert.NotEqual(before, session.UploadRoot);
        Assert.Equal(next, session.UploadRoot);
        Assert.Equal(Path.Combine(next, "inbox"), session.InboxRoot);
    }
}

/// <summary>本文の 1 つの入力欄の形（写真の行 ![説明](URL)）。</summary>
public sealed class ArticleDocumentTests
{
    private const string Base = "https://img.example/";
    private const string K1 = "2026-10/20261023-213312-345_a1b2c3d4";
    private const string K2 = "2026-10/20261023-214001-120_0f1e2d3c";

    private static FaraVrcAlbum.Tools.Services.PostEditModel Model() => new("s", "題", "2026-10-24T20:00:00+09:00", false, [], "はじめ", "おわり",
    [
        new(K1, null, null, [], "一枚目", ""),
        new(K2, null, null, [], "", "夜の写真"),
    ]);

    [Fact]
    public void ComposeAndParseRoundTrip()
    {
        var doc = FaraVrcAlbum.Desktop.Services.ArticleDocument.Compose(Model(), Base);
        Assert.Equal($"はじめ\n\n![一枚目]({Base}{K1}_1280.webp)\n\n夜の写真\n\n![]({Base}{K2}_1280.webp)\n\nおわり\n", doc);   // 最後に 1 行の改行
        var parts = FaraVrcAlbum.Desktop.Services.ArticleDocument.Parse(doc, [K1, K2], Base);
        Assert.Empty(parts.Errors);
        Assert.Equal(("はじめ", "おわり"), (parts.Body, parts.Afterword));
        Assert.Equal([(K1, "一枚目", ""), (K2, "", "夜の写真")], parts.Photos);
    }

    [Fact]
    public void MovingAPhotoLineReordersAndForeignUrlsAreErrors()
    {
        var doc = $"![]({Base}{K2}_1280.webp)\n文\n![]({Base}{K1}_640.webp)";
        var parts = FaraVrcAlbum.Desktop.Services.ArticleDocument.Parse(doc, [K1, K2], Base);
        Assert.Empty(parts.Errors);
        Assert.Equal([K2, K1], parts.Photos.Select(p => p.Key));
        Assert.Equal("文", parts.Photos[1].Text);

        var bad = FaraVrcAlbum.Desktop.Services.ArticleDocument.Parse($"![]({Base}{K1}_1280.webp)\n![](https://evil.example/x.png)", [K1, K2], Base);
        Assert.Contains(bad.Errors, e => e.Contains("この記事の写真ではありません"));
        Assert.Contains(bad.Errors, e => e.Contains("行がありません"));
    }

    [Fact]
    public void EachPhotoGetsItsDateOutfitAndWorldLinesUnderItAndUnknownOnesAreLeftOut()
    {
        var model = new FaraVrcAlbum.Tools.Services.PostEditModel("s", "題", "2026-10-24T20:00:00+09:00", false, [], "", "",
        [
            new(K1, "2026-10-28T01:41:11+09:00", "Crystal Garden", ["白いドレス", "リボン"], "", ""),
            new(K2, "2026-10-28T01:50:00+09:00", null, [], "", ""),                              // 服もワールドも不明
        ]);

        var doc = FaraVrcAlbum.Desktop.Services.ArticleDocument.Compose(model, Base);

        Assert.Equal($"![]({Base}{K1}_1280.webp)\n日付：2026/10/28 01:41:11\n服：白いドレス、リボン\nワールド：Crystal Garden\n\n![]({Base}{K2}_1280.webp)\n日付：2026/10/28 01:50:00\n", doc);

        // 保存のときは、この自動の行を読み捨てる（記事の項目にも、写真の前の文章にもならない）
        var parts = FaraVrcAlbum.Desktop.Services.ArticleDocument.Parse(doc, [K1, K2], Base);
        Assert.Empty(parts.Errors);
        Assert.Equal(("", ""), (parts.Body, parts.Afterword));
        Assert.Equal([(K1, "", ""), (K2, "", "")], parts.Photos);
        Assert.Equal(doc, FaraVrcAlbum.Desktop.Services.ArticleDocument.Compose(model with { Body = parts.Body, Afterword = parts.Afterword }, Base));   // 何度開いて保存しても、同じ形

        // プレビューは、本文の行をそのまま使う
        var blocks = FaraVrcAlbum.Desktop.Services.ArticleDocument.Blocks(doc, Base);
        Assert.Equal("日付：2026/10/28 01:41:11\n服：白いドレス、リボン\nワールド：Crystal Garden", blocks[0].Auto);
        Assert.Equal("日付：2026/10/28 01:50:00", blocks[1].Auto);

        // 文章を書き足したあとの行は、読み捨てない
        var written = doc.Replace("ワールド：Crystal Garden\n", "ワールド：Crystal Garden\nここは自分の文章\n");
        var rewritten = FaraVrcAlbum.Desktop.Services.ArticleDocument.Parse(written, [K1, K2], Base);
        Assert.Equal("ここは自分の文章", rewritten.Photos[1].Text);
    }
}
