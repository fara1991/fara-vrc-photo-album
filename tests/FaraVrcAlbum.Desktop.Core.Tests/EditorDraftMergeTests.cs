using System.Text.Json.Nodes;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>記事の画面（日付で選ぶ）。公開日時は日本時間で扱い、表紙は本文の最初の写真に合わせ、まだ無い日付は保存すると作られる。</summary>
public sealed class EditorDraftMergeTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private string PostFile(string slug) => Path.Combine(_ws.Layout.RepoRoot, "content", "posts", slug + ".json");

    /// <summary>下書きの記事のファイルを消して、その日付を「未作成」にする（その日の写真はアップロード済みのまま）。</summary>
    private string RemoveTheDraftPost()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        var draft = editor.Posts.First(p => p.Draft);
        File.Delete(PostFile(draft.Slug));
        return draft.Slug;
    }

    private static string EditorTitle(string date) => PostEditorViewModel.DefaultTitle(date);

    [Fact]
    public void PublishedAtIsShownInJapanTimeWithoutAnOffsetAndReadAsJapanTime()
    {
        Assert.Equal("2026-10-24 20:00", JstDate.DisplayOf("2026-10-24T20:00:30+09:00"));
        Assert.Equal("2026-10-24 20:00", JstDate.DisplayOf("2026-10-24T11:00:00Z"));          // 時差つきは日本時間に直して出す

        Assert.True(JstDate.TryParse("2026-10-24 20:00", out var jst));
        Assert.Equal("2026-10-24T20:00:00+09:00", JstDate.ToIso(jst));                      // 時差なしは日本時間とみなす
        Assert.True(JstDate.TryParse("2026-10-24", out var dateOnly));
        Assert.Equal("2026-10-24T00:00:00+09:00", JstDate.ToIso(dateOnly));
        Assert.False(JstDate.TryParse("あした", out _));
    }

    [Fact]
    public async Task SavingKeepsTheStoredSecondsUnlessThePublishedAtIsEdited()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        Assert.DoesNotContain("+09:00", editor.PublishedAt);                                 // 画面には時差を出さない
        var before = JsonNode.Parse(File.ReadAllText(PostFile("demo-first")))!["publishedAt"]!.GetValue<string>();

        editor.Title += "！";
        _ws.Dialogs.Answer = true;
        await editor.SaveCommand.RunAsync();
        Assert.Equal(before, JsonNode.Parse(File.ReadAllText(PostFile("demo-first")))!["publishedAt"]!.GetValue<string>());

        editor.PublishedAt = "2026-11-01 09:30";
        await editor.SaveCommand.RunAsync();
        Assert.Equal("2026-11-01T09:30:00+09:00", JsonNode.Parse(File.ReadAllText(PostFile("demo-first")))!["publishedAt"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheFirstPhotoInTheBodyBecomesTheCoverAndItsShareImageIsMade()
    {
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        var lines = editor.Document.Split('\n').ToList();
        var target = lines.First(l => l.Contains("20261025-091530", StringComparison.Ordinal));   // 元の PNG が VRChat のフォルダにある写真
        lines.Remove(target);
        lines.Insert(0, target);
        editor.Document = string.Join("\n", lines);

        _ws.Dialogs.Answer = true;
        await editor.SaveCommand.RunAsync();

        var post = JsonNode.Parse(File.ReadAllText(PostFile("demo-first")))!;
        Assert.Contains("20261025-091530", post["cover"]!.GetValue<string>());
        Assert.Contains("表紙を最初の写真にしました", editor.Status);
    }

    [Fact]
    public void TheDateListShowsWhetherEachDaysArticleIsCreatedAndHasNoNewArticleButton()
    {
        var removed = RemoveTheDraftPost();
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();

        Assert.NotEmpty(editor.Dates);
        Assert.Contains(editor.Dates, d => d.Exists && d.StateText.StartsWith("作成済み", StringComparison.Ordinal));
        var missing = editor.Dates.First(d => !d.Exists);
        Assert.Equal("未作成", missing.StateText);
        Assert.Contains("未作成", missing.Text);
        Assert.Contains(editor.Dates, d => d.Post?.Draft == true || d.Post?.Draft == false);
        Assert.Null(typeof(PostEditorViewModel).GetProperty("NewPostCommand"));            // 「新しい記事」ボタンは無い
        Assert.Null(typeof(PostEditorViewModel).GetProperty("Draft"));
        Assert.False(File.Exists(PostFile(removed)));
        // 日付の新しい順
        Assert.Equal(editor.Dates.OrderByDescending(d => d.Date, StringComparer.Ordinal).Select(d => d.Date), editor.Dates.Select(d => d.Date));
    }

    [Fact]
    public async Task AnUncreatedDateOpensANewArticleWithThatDaysPhotosAndSavingCreatesItWithTheDateAsSlug()
    {
        RemoveTheDraftPost();
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        var missing = editor.Dates.First(d => !d.Exists);

        editor.SelectedDate = missing;

        Assert.True(editor.IsNewArticle);
        Assert.True(editor.IsDraft);
        Assert.Equal(missing.PhotoCount, editor.PhotoAvatars.Count);
        Assert.Equal(missing.PhotoCount, editor.Document.Split('\n').Count(l => l.StartsWith("![", StringComparison.Ordinal)));
        Assert.DoesNotContain("+09:00", editor.PublishedAt);
        Assert.True(editor.SaveCommand.CanExecute(null), "新しい記事は、最初から保存できる");

        Assert.Equal(EditorTitle(missing.Date), editor.Title);                            // 題の既定は日付（2026年10月24日）
        Assert.DoesNotContain("-", editor.Title);

        // 題を空にすると作らない（確認も出さない）
        editor.Title = "";
        _ws.Dialogs.Answer = true;
        await editor.SaveCommand.RunAsync();
        Assert.False(string.IsNullOrEmpty(editor.Status) || editor.Status.StartsWith("エラー（"), editor.Status);   // 型名は付けず、文だけ出す
        Assert.Empty(_ws.Dialogs.Asked);
        Assert.False(File.Exists(PostFile(missing.Date)));

        editor.Title = "その日の記録";
        await editor.SaveCommand.RunAsync();

        var post = JsonNode.Parse(File.ReadAllText(PostFile(missing.Date)))!;                // slug（ファイル名）は日付
        Assert.Equal(missing.Date, post["slug"]!.GetValue<string>());
        Assert.Equal("その日の記録", post["title"]!.GetValue<string>());
        Assert.True(post["draft"]!.GetValue<bool>());
        Assert.Equal(missing.PhotoCount, post["photos"]!.AsArray().Count);
        // 作ったあとは、その日付が「作成済み」になり、開いたままになる
        Assert.False(editor.IsNewArticle);
        Assert.True(editor.Dates.Single(d => d.Date == missing.Date).Exists);
        Assert.Equal(missing.Date, editor.SelectedPost!.Slug);
    }

    [Fact]
    public async Task PerPhotoAvatarsAreChosenInTheEditorAndSavedForNewAndExistingArticles()
    {
        _ws.Dialogs.Answer = true;
        var masters = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        masters.Refresh();
        masters.NewAvatarCommand.Execute(null);
        masters.AvatarName = "Fairy_Dress_v2";
        await masters.SaveAvatarCommand.RunAsync();
        var avatarId = masters.Avatars.Single().Id;

        // 既存の記事: 1 枚だけアバターを変えて保存
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        editor.SelectedPost = editor.Posts.Single(p => p.Slug == "demo-first");
        var fairy = editor.AvatarOptions.Single(a => a.Id == avatarId);
        editor.PhotoAvatars[0].Avatar = fairy;
        Assert.True(editor.IsDirty);
        await editor.SaveCommand.RunAsync();
        var photos = JsonNode.Parse(File.ReadAllText(PostFile("demo-first")))!["photos"]!.AsArray();
        Assert.Equal(avatarId, photos[0]!["avatar"]!.GetValue<string>());
        Assert.Null(photos[1]!["avatar"]);

        // いつものアバターは手元に覚え、新しい記事の写真に最初から入る。全部の写真に入れることもできる
        editor.DefaultAvatar = fairy;
        Assert.Equal(avatarId, _ws.Session.Preferences.DefaultAvatar);
        RemoveTheDraftPost();
        editor.Refresh();
        editor.SelectedDate = editor.Dates.First(d => !d.Exists);
        Assert.All(editor.PhotoAvatars, r => Assert.Equal(fairy, r.Avatar));
        editor.PhotoAvatars[0].Avatar = AvatarOptionViewModel.None;
        editor.SelectedAvatarRow = editor.PhotoAvatars[0];
        editor.ApplyDefaultToAllCommand.Execute(null);
        Assert.All(editor.PhotoAvatars, r => Assert.Equal(fairy, r.Avatar));
        editor.Title = "アバター付きの記事";
        var date = editor.SelectedDate!.Date;
        await editor.SaveCommand.RunAsync();
        var created = JsonNode.Parse(File.ReadAllText(PostFile(date)))!["photos"]!.AsArray();
        Assert.All(created, p => Assert.Equal(avatarId, p!["avatar"]!.GetValue<string>()));
    }

    private async Task<string> RegisterAvatarAsync(string name)
    {
        var masters = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        masters.Refresh();
        masters.AvatarName = name;
        await masters.SaveAvatarCommand.RunAsync();
        return masters.Avatars.Single(a => a.Name == name).Id;
    }

    [Fact]
    public async Task ANewArticleGetsEachPhotosAvatarFromThePhotoRecordThenTheServerHistoryThenTheDefault()
    {
        _ws.Dialogs.Answer = true;
        var fairyId = await RegisterAvatarAsync("Fairy_Dress_v2");
        var otherId = await RegisterAvatarAsync("Other Dress");
        RemoveTheDraftPost();
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        var missing = editor.Dates.First(d => !d.Exists && d.PhotoCount >= 1);

        var ledgerPath = _ws.Session.Repo.ProductionLedgerPath;
        void SetHint(string? name)
        {
            var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!.AsObject();
            foreach (var entry in ledger.Select(p => p.Value!.AsObject()).Where(e => e["takenAt"]!.GetValue<string>().StartsWith(missing.Date, StringComparison.Ordinal)))
            {
                if (name is null) entry.Remove("avatarName");
                else entry["avatarName"] = name;
            }
            File.WriteAllText(ledgerPath, ledger.ToJsonString());
        }
        var history = Path.GetFullPath(Path.Combine(_ws.Layout.RepoRoot, "..", "server-history-" + Guid.NewGuid().ToString("N")[..6] + ".jsonl"));
        File.WriteAllText(history, "{\"t\":\"" + missing.Date + "T00:00:00+09:00\",\"type\":\"avatar\",\"avatarName\":\"Fairy_Dress_v2\",\"avatarId\":\"avtr_00000000-0000-4000-8000-000000000002\"}\n" + "壊れた行\n");
        File.WriteAllText(Path.Combine(_ws.Layout.RepoRoot, "local", "album.local.json"),
            new JsonObject { ["uploadRoot"] = _ws.Layout.UploadRoot, ["photoServerHistory"] = history }.ToJsonString());
        _ws.Session.Reload();

        // 写真の記録（XMP から取った名前。空白・大文字小文字が違っても合う）が、先に使われる
        SetHint("other dress");
        editor.SelectedDate = missing;
        Assert.All(editor.PhotoAvatars, r => Assert.Equal(otherId, r.Avatar.Id));
        Assert.Contains("写真の記録から", editor.Status);
        Assert.True(editor.IsNewArticle);

        // 記録が無い写真は、履歴ファイルの切り替えから（壊れた行は読み飛ばす）
        SetHint(null);
        editor.SelectedDate = editor.Dates.First(d => d.Exists);
        editor.SelectedDate = missing;
        Assert.All(editor.PhotoAvatars, r => Assert.Equal(fairyId, r.Avatar.Id));

        // 合わない名前・履歴の無いときは、いつものアバター（無ければ「なし」）のまま
        SetHint("Nobody");
        File.Delete(history);
        editor.SelectedDate = editor.Dates.First(d => d.Exists);
        editor.SelectedDate = missing;
        Assert.All(editor.PhotoAvatars, r => Assert.Equal(AvatarOptionViewModel.None, r.Avatar));
        Assert.DoesNotContain("写真の記録から", editor.Status);
    }

    [Fact]
    public async Task ThePreviewShowsTheOutfitOfThePhotosAvatarNextToTheWorld()
    {
        _ws.Dialogs.Answer = true;
        var masters = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        masters.Refresh();
        masters.NewOutfitCommand.Execute(null);
        masters.OutfitId = "dress";
        masters.OutfitName = "白いドレス";
        masters.OutfitUrl = "https://booth.pm/ja/items/1";
        await masters.SaveOutfitCommand.RunAsync();
        masters.NewAvatarCommand.Execute(null);
        masters.AvatarId = "fairy";
        masters.AvatarName = "Fairy_Dress_v2";
        masters.SelectedOutfitPick = masters.OutfitPicks.Single(p => p.Id == "dress");
        await masters.SaveAvatarCommand.RunAsync();

        RemoveTheDraftPost();
        var editor = new PostEditorViewModel(_ws.App, _ws.Dialogs);
        editor.Refresh();
        var missing = editor.Dates.First(d => !d.Exists && d.PhotoCount >= 1);
        var ledgerPath = _ws.Session.Repo.ProductionLedgerPath;
        var ledger = JsonNode.Parse(File.ReadAllText(ledgerPath))!.AsObject();
        foreach (var entry in ledger.Select(p => p.Value!.AsObject()).Where(e => e["takenAt"]!.GetValue<string>().StartsWith(missing.Date, StringComparison.Ordinal)))
            entry["avatarName"] = "FAIRY_Dress_v2";
        File.WriteAllText(ledgerPath, ledger.ToJsonString());

        editor.SelectedDate = missing;

        var photo = editor.PreviewBlocks.OfType<PreviewPhotoBlock>().First();
        Assert.Contains("服：白いドレス", photo.AutoText);
        Assert.Contains("ワールド：", photo.AutoText);
        Assert.True(photo.AutoText.IndexOf("服：", StringComparison.Ordinal) < photo.AutoText.IndexOf("ワールド：", StringComparison.Ordinal));   // 服はワールドの前

        // アバターが分からない写真は、服を出さない（今までと同じ）
        foreach (var entry in ledger.Select(p => p.Value!.AsObject())) entry.Remove("avatarName");
        File.WriteAllText(ledgerPath, ledger.ToJsonString());
        editor.SelectedDate = editor.Dates.First(d => d.Exists);
        editor.SelectedDate = missing;
        Assert.DoesNotContain("服：", editor.PreviewBlocks.OfType<PreviewPhotoBlock>().First().AutoText);
    }
}
