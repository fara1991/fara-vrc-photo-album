using System.Reflection;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>安全策: 試験用では本番だけの操作を出さない・削除は確認を経る・設定に値を出さない。</summary>
public sealed class SafetyTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void ThereIsNoTestBucketChoiceAndTheScreenAlwaysUsesTheProductionBucket()
    {
        // 確認用も本番も同じ R2 を見るので、画面には試験用の選択肢が無い
        Assert.Equal(RepoContext.ProductionBucket, _ws.Session.Bucket);
        var main = new MainViewModel(_ws.Session, _ws.Dialogs);
        // 操作の画面に出る説明には、バケット・リポジトリなどの言葉を出さない
        foreach (var word in new[] { "バケット", "リポジトリ", "R2", RepoContext.ProductionBucket })
            Assert.DoesNotContain(word, main.AppDescription);
        Assert.Null(typeof(MainViewModel).GetProperty("IsProduction"));
        Assert.Null(typeof(AppState).GetProperty("Bucket"));
    }

    [Fact]
    public void TheTabsAreTheSixWorkScreensAndSettingsIsOpenedFromTheMenu()
    {
        var main = new MainViewModel(_ws.Session, _ws.Dialogs);
        Assert.Equal(["取り込み", "記事", "アバター・服", "管理", "同期", "公開"], main.Pages.Select(p => p.Title));
        Assert.DoesNotContain(main.Pages, p => p.Page is SettingsViewModel);
        main.OpenSettings();                                                            // 開くときに読み直す（例外を出さない）
        Assert.NotEmpty(main.Settings.Rows);
    }

    [Fact]
    public void RemoveNeedsASelectionFirst()
    {
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);

        Assert.False(manage.RemoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task RemovableListLoadsFromTheProductionLedger()
    {
        await _ws.SeedProductionAsync();
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);
        await manage.LoadRemovableAsync();

        Assert.True(manage.RemoveRefreshCommand.CanExecute(null));
        Assert.Equal(4, manage.Removable.Count);
        Assert.False(manage.RemovePlanCommand.CanExecute(null), "写真を選ぶまでは計画できない");
    }

    [Fact]
    public async Task RemoveDoesNothingUnlessConfirmed()
    {
        await _ws.SeedProductionAsync();
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);
        await manage.LoadRemovableAsync();
        manage.SelectedRemovable = manage.Removable.Single(r => r.TakenText.StartsWith("2026-10-21", StringComparison.Ordinal));
        var ledgerBefore = File.ReadAllText(_ws.ProductionLedgerPath);
        var postBefore = File.ReadAllText(Path.Combine(_ws.Layout.RepoRoot, "content", "posts", "demo-first.json"));
        var key = manage.SelectedRemovable!.Key;

        _ws.Dialogs.Answer = false;
        await manage.RemoveCommand.RunAsync();

        Assert.Equal(["写真を削除します"], _ws.Dialogs.Asked);
        Assert.Equal(ledgerBefore, File.ReadAllText(_ws.ProductionLedgerPath));
        Assert.Equal(postBefore, File.ReadAllText(Path.Combine(_ws.Layout.RepoRoot, "content", "posts", "demo-first.json")));
        Assert.True(_ws.Session.DemoStore(_ws.Session.ProductionBucket).Objects.ContainsKey(key + "_640.webp"));
    }

    [Fact]
    public async Task RemoveAfterConfirmationRemovesFromR2LedgerAndPost()
    {
        await _ws.SeedProductionAsync();
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);
        await manage.LoadRemovableAsync();
        manage.SelectedRemovable = manage.Removable.Single(r => r.TakenText.StartsWith("2026-10-21", StringComparison.Ordinal));
        var key = manage.SelectedRemovable!.Key;

        _ws.Dialogs.Answer = true;
        await manage.RemoveCommand.RunAsync();

        var store = _ws.Session.DemoStore(_ws.Session.ProductionBucket);
        Assert.False(store.Objects.ContainsKey(key + "_640.webp"));
        Assert.Equal(LedgerStates.Removed, PhotoLedger.Load(_ws.ProductionLedgerPath).FindByKey(key)!.State);
        var post = ContentEditor.ReadObject(Path.Combine(_ws.Layout.RepoRoot, "content", "posts", "demo-first.json"));
        Assert.DoesNotContain(post["photos"]!.AsArray(), p => p?["key"]?.GetValue<string>() == key);
    }

    [Fact]
    public void SettingsNeverShowSecretValuesUnlessRevealed()
    {
        const string accountId = "acct-fake-0123456789";
        const string accessKey = "AKIAFAKEFAKEFAKE0000";
        const string secret = "FakeSecretValueXYZ0123456789abcdefABCDEF";
        File.WriteAllText(Path.Combine(_ws.Layout.RepoRoot, ".env.local"),
            $"R2_ACCOUNT_ID={accountId}\nR2_ACCESS_KEY_ID={accessKey}\nR2_SECRET_ACCESS_KEY={secret}\nR2_BUCKET=fara-vrc-photos-test\n");
        Directory.CreateDirectory(Path.Combine(_ws.Layout.RepoRoot, "local"));
        File.WriteAllText(Path.Combine(_ws.Layout.RepoRoot, "local", "album.local.json"), "{ \"bucket\": \"test-own-photos\" }");   // バケット名は設定で決める（既定は空）
        var session = new AlbumSession(RepoContext.Discover(_ws.Layout.RepoRoot), _ws.Layout.UploadRoot, offline: false, sourceRoot: _ws.Layout.SourceRoot);
        var settings = new SettingsViewModel(new AppState(session), _ws.Dialogs);

        Assert.All(settings.Credentials, c => Assert.Equal("設定あり", c.PresenceText));
        Assert.Equal("写真の保存先（Cloudflare R2）に接続する設定がそろっています。", settings.ConnectionText);

        var everything = typeof(SettingsViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetValue(settings))
            .SelectMany(v => v is System.Collections.IEnumerable e && v is not string ? e.Cast<object?>() : [v])
            .Select(v => v?.ToString() ?? "")
            .Concat(settings.Credentials.Select(c => c.Name + c.PresenceText))
            // 押していない間は、編集の行のどのプロパティにも値が出ない
            .Concat(settings.Rows.SelectMany(r => typeof(SettingRowViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.GetValue(r)?.ToString() ?? "")))
            .ToList();
        foreach (var value in new[] { accountId, accessKey, secret })
        {
            Assert.DoesNotContain(everything, text => text.Contains(value, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AsyncCommandReportsErrorsAndResetsIsRunning()
    {
        var errors = new List<Exception>();
        var command = new AsyncCommand(_ => throw new InvalidOperationException("失敗のテスト"), errors.Add);

        command.RunAsync().GetAwaiter().GetResult();

        Assert.Single(errors);
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncCommandCancelStopsTheRun()
    {
        var started = new TaskCompletionSource();
        var command = new AsyncCommand(async ct =>
        {
            started.SetResult();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }, _ => { });

        var run = command.RunAsync();
        await started.Task;
        Assert.True(command.IsRunning);
        command.Cancel();
        await run;

        Assert.False(command.IsRunning);
    }

    [Fact]
    public async Task VerifyScopeMatchesTheCliRuleAndIncludesLedgerOnlyPhotosWhenAsked()
    {
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);

        // 下書きの写真も、記事の写真として確認する（確認用に出るため）
        await manage.ScopeCommand.RunAsync();
        Assert.Contains("確認する写真 4 枚（記事の写真 4・記事に入れていない写真 0）", manage.VerifySummary);

        // 下書きの記事を消すと、その写真は「アップロード済みの記録だけにある写真」になる
        File.Delete(Path.Combine(_ws.Layout.RepoRoot, "content", "posts", "demo-draft.json"));
        await manage.ScopeCommand.RunAsync();
        Assert.Contains("確認する写真 3 枚", manage.VerifySummary);

        manage.IncludeLedger = true;
        await manage.ScopeCommand.RunAsync();
        Assert.Contains("確認する写真 4 枚", manage.VerifySummary);
        Assert.Contains(manage.VerifyNotes, n => n.Contains("アップロード済みの記録だけにある写真 1 件", StringComparison.Ordinal));
    }

    [Fact]
    public void DemoRepositoryLoadsAsValidContent()
    {
        var content = new ContentEditor(_ws.Session.Repo.ContentDir).LoadContent();

        Assert.Equal(["demo-draft", "demo-first"], content.Posts.Select(p => p.Slug));             // 下書きも読む（新しい順）
        Assert.True(content.Posts[0].Draft);
        Assert.False(content.Posts[1].Draft);
    }
}
