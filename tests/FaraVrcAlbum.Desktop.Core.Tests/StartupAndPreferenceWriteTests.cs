using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>
/// 起動時の設定の退避の通知、設定の書き込み（アトミック・直列化）、観測した Task の例外の報告。
/// 画面（WPF）の表示そのものは手で確かめる（PR の確認項目）。ここは Core の振る舞いだけを見る。
/// </summary>
public sealed class StartupAndPreferenceWriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "work", "prefs-" + Guid.NewGuid().ToString("N"));

    public StartupAndPreferenceWriteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task AStartupWithABrokenFileMovesItAsideAndTheNoticeIsShownOnceAfterTheWindowStarts()
    {
        using var ws = new Workspace();
        var path = Path.Combine(ws.Session.Repo.LocalDir, "desktop.json");
        Directory.CreateDirectory(ws.Session.Repo.LocalDir);
        File.WriteAllText(path, "{ broken");

        var session = NewSession(ws);

        var aside = session.StartupPreferencesAside;
        Assert.NotNull(aside);
        Assert.False(File.Exists(path));                                 // 退避された（起動のときに 1 回）
        Assert.Equal("{ broken", File.ReadAllText(aside));              // 中身は別名で残る

        var dialogs = new RecordingDialogs();
        var vm = new MainViewModel(session, dialogs);
        Assert.Empty(dialogs.Informed);                                  // 画面が出る前（作成のとき）には知らせない

        await vm.NotifyStartupPreferencesAsync();
        await vm.NotifyStartupPreferencesAsync();                        // 2 回目は知らせない

        var notice = Assert.Single(dialogs.Informed);
        Assert.Contains(Path.GetFileName(aside), notice.Message);
        Assert.Contains("既定の値で動きます", notice.Message);
    }

    [Fact]
    public void AStartupWithoutABrokenFileCreatesNothingAndNoticesNothing()
    {
        using var ws = new Workspace();
        var path = Path.Combine(ws.Session.Repo.LocalDir, "desktop.json");

        var none = NewSession(ws);
        Assert.Null(none.StartupPreferencesAside);
        Assert.False(File.Exists(path));                                 // ファイルが無くても作らない

        Directory.CreateDirectory(ws.Session.Repo.LocalDir);
        File.WriteAllText(path, "{\n  \"defaultAvatar\": \"avatar-1\"\n}\n");
        var valid = NewSession(ws);
        Assert.Null(valid.StartupPreferencesAside);
        Assert.Equal("avatar-1", new DesktopPreferences(path).DefaultAvatar);
        Assert.Empty(Directory.GetFiles(ws.Session.Repo.LocalDir, "desktop.json.broken-*"));
    }

    [Fact]
    public void AFailedWriteLeavesTheCurrentSettingsFileIntact()
    {
        var path = Path.Combine(_dir, "desktop.json");
        var prefs = new DesktopPreferences(path);
        prefs.BoothDatabase = @"D:\booth\data.db";
        prefs.AvatarFolder = @"D:\unity";
        var before = File.ReadAllText(path);

        // 一時ファイルの場所をディレクトリでふさぐ。書き込みの途中（置き換えの前）で失敗する
        Directory.CreateDirectory(path + ".tmp");
        Assert.ThrowsAny<Exception>(() => prefs.DefaultAvatar = "avatar-2");

        Assert.Equal(before, File.ReadAllText(path));                    // 元のファイルはそのまま
        var fresh = new DesktopPreferences(path);
        Assert.Equal(@"D:\booth\data.db", fresh.BoothDatabase);          // ほかの設定も残る
        Assert.Equal(@"D:\unity", fresh.AvatarFolder);
        Assert.Equal("", fresh.DefaultAvatar);
    }

    [Fact]
    public void ConcurrentSetsDoNotLoseEachOthersUpdates()
    {
        // Preferences は読むたびに新しいインスタンスを作るので、インスタンスごとの lock では防げない。static の lock で直列にする
        for (var round = 0; round < 40; round++)
        {
            var path = Path.Combine(_dir, $"round-{round}.json");
            var tag = round.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Parallel.Invoke(
                () => new DesktopPreferences(path).DefaultAvatar = "avatar-" + tag,
                () => new DesktopPreferences(path).AvatarFolder = "folder-" + tag,
                () => new DesktopPreferences(path).OutfitFolder = "outfit-" + tag,
                () => new DesktopPreferences(path).BoothDatabase = "booth-" + tag,
                () => new DesktopPreferences(path).LastBackup = "backup-" + tag);

            var check = new DesktopPreferences(path);
            Assert.Equal("avatar-" + tag, check.DefaultAvatar);
            Assert.Equal("folder-" + tag, check.AvatarFolder);
            Assert.Equal("outfit-" + tag, check.OutfitFolder);
            Assert.Equal("booth-" + tag, check.BoothDatabase);
            Assert.Equal("backup-" + tag, check.LastBackup);
        }
    }

    [Fact]
    public async Task AFiredTaskFailureReachesTheReporter()
    {
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        BackgroundTasks.Fire(Task.FromException(new InvalidOperationException("起動の読み込みの例外")), reported.SetResult);

        var error = await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public async Task AFailedRecoveryNoticeReachesTheErrorReportInsteadOfBeingDropped()
    {
        using var ws = new Workspace();
        var path = Path.Combine(ws.Session.Repo.LocalDir, "desktop.json");
        var session = NewSession(ws);                                    // 起動のときは壊れていない
        var dialogs = new RecordingDialogs { InformFailure = new InvalidOperationException("通知の失敗") };
        _ = new MainViewModel(session, dialogs);

        Directory.CreateDirectory(ws.Session.Repo.LocalDir);
        File.WriteAllText(path, "{ broken");
        session.Preferences.DefaultAvatar = "avatar-1";                  // 壊れた上に書く → 退避 → 通知（失敗）

        var error = await dialogs.Reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("通知の失敗", error.Message);                       // 捨てられず、報告の経路に届く
    }

    private static AlbumSession NewSession(Workspace ws) =>
        new(RepoContext.Discover(ws.Layout.RepoRoot), ws.Layout.UploadRoot, offline: true, ws.Layout.SourceRoot);

    /// <summary>通知と報告を記録する。通知の失敗（InformFailure）を指定できる。</summary>
    private sealed class RecordingDialogs : IDialogService
    {
        public List<(string Title, string Message)> Informed { get; } = [];

        public Exception? InformFailure { get; init; }

        public TaskCompletionSource<Exception> Reported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => Task.FromResult(false);

        public Task InformAsync(string title, string message)
        {
            Informed.Add((title, message));
            return InformFailure is null ? Task.CompletedTask : Task.FromException(InformFailure);
        }

        public void ReportError(Exception error) => Reported.TrySetResult(error);
    }
}
