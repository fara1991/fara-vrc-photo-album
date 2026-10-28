using FaraVrcAlbum.Desktop.Demo;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>取り込み（一覧）→ 計画（ドライラン）→ 実行の流れ。既定はドライランで、確認を断れば何も書かない。</summary>
public sealed class IngestPlanTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    /// <summary>取り込みの画面はコピーせず、VRChat の写真格納フォルダの写真をそのまま渡す。</summary>
    private static IReadOnlyList<string> ToInbox(IReadOnlyList<string> files) => files;

    private string SourceSnapshot() =>
        string.Join("\n", Directory.EnumerateFiles(_ws.Session.SourceRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(_ws.Session.SourceRoot, f) + ":" + new FileInfo(f).Length));

    /// <summary>「すべての月」を読む（月を選ぶと読み直しが始まるので、終わるまで待つ）。</summary>
    private static async Task AllMonthsAsync(IngestViewModel ingest)
    {
        await ingest.RefreshAsync();
        ingest.SelectedMonth = ingest.Months[0];
        while (ingest.RefreshCommand.IsRunning) await Task.Delay(10);
    }

    [Fact]
    public void DemoFolderIsRefusedWhenItIsNotADemoAndNotEmpty()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var keep = Path.Combine(root, "keep.txt");
        File.WriteAllText(keep, "実データ");

        Assert.Throws<InvalidOperationException>(() => DemoWorkspace.Create(root));
        Assert.Equal("実データ", File.ReadAllText(keep));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void DemoFolderNeverDeletesARepository()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "FaraVrcAlbum.sln"), "");
        File.WriteAllText(Path.Combine(root, "marker-check.txt"), "x");

        Assert.Throws<InvalidOperationException>(() => DemoWorkspace.Create(root));
        Assert.True(File.Exists(Path.Combine(root, "marker-check.txt")));
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task InboxListingShowsStatesAndWritesNothing()
    {
        var ledgerBefore = File.ReadAllText(_ws.ProductionLedgerPath);
        var uploadBefore = _ws.UploadSnapshot();
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);

        await ingest.RefreshAsync();
        Assert.Equal(["すべての月", "2026年10月", "2025年10月"], ingest.Months.Select(m => m.Text));
        Assert.Equal("2026-10", ingest.SelectedMonth!.Month);                         // 既定は一番新しい月
        Assert.Equal(6, ingest.Items.Count);
        await AllMonthsAsync(ingest);

        Assert.Equal(7, ingest.Items.Count);
        Assert.Equal(1, ingest.Items.Count(i => i.StateText == "アップロード済み"));
        Assert.Equal(1, ingest.Items.Count(i => i.StateText == "削除済み"));
        Assert.Equal(1, ingest.Items.Count(i => i.XmpText == "なし"));
        Assert.Contains(ingest.Items, i => i.ProblemText == "2025 年 11 月より前（取り込みません）");
        Assert.Equal(ledgerBefore, File.ReadAllText(_ws.ProductionLedgerPath));
        Assert.Equal(uploadBefore, _ws.UploadSnapshot());
    }

    [Fact]
    public async Task DecliningTheConfirmationWritesNothingAndCopiesNothing()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        var picked = ingest.Items.Where(i => i.StateText == "未登録" && i.ProblemText.Length == 0).Take(2).ToList();
        var ledgerBefore = File.ReadAllText(_ws.ProductionLedgerPath);
        var sourceBefore = SourceSnapshot();
        var uploadBefore = _ws.UploadSnapshot();

        foreach (var item in picked) item.IsChecked = true;
        Assert.True(ingest.UploadCommand.CanExecute(null));
        _ws.Dialogs.Answer = false;
        await ingest.UploadCommand.RunAsync();                                          // 1 回押すだけで、計画 → 確認ダイアログ

        Assert.Equal(["写真をアップロードします"], _ws.Dialogs.Asked);
        Assert.Equal(2, ingest.Plan.Rows.Count(r => r.StateText == "計画"));
        Assert.False(Directory.Exists(Path.Combine(_ws.Session.InboxRoot, "2026-10")));  // コピーしない
        Assert.Equal(sourceBefore, SourceSnapshot());
        Assert.Equal(uploadBefore, _ws.UploadSnapshot());
        Assert.Equal(ledgerBefore, File.ReadAllText(_ws.ProductionLedgerPath));

        // 選び直すと、前の計画は古いので捨てる
        picked[0].IsChecked = false;
        Assert.Empty(ingest.Plan.Rows);
    }

    [Fact]
    public async Task UploadingDirectlyKeepsTheOriginalPhotosAndMarksThemUploaded()
    {
        await _ws.SeedProductionAsync();
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        var picked = ingest.Items.Where(i => i.StateText == "未登録" && i.ProblemText.Length == 0).Take(1).ToList();
        picked[0].IsChecked = true;
        var sourceBefore = SourceSnapshot();

        _ws.Dialogs.Answer = true;
        await ingest.UploadCommand.RunAsync();

        Assert.Equal(sourceBefore, SourceSnapshot());                                   // 元の写真は移動も削除もされない
        Assert.True(File.Exists(picked[0].Path));
        Assert.False(Directory.Exists(Path.Combine(_ws.Session.UploadedRoot, "2026-10")) &&
                     Directory.EnumerateFiles(Path.Combine(_ws.Session.UploadedRoot, "2026-10")).Any());
        Assert.Contains(PhotoLedger.Load(_ws.ProductionLedgerPath).BySha.Values, e => e.State == LedgerStates.Uploaded);

        while (ingest.RefreshCommand.IsRunning) await Task.Delay(10);                  // アップロード後の自動の読み直しが終わるまで待つ
        await ingest.RefreshAsync();
        // 一覧を読み直すと、その写真は「✓ アップロード済み」と出て、「すべて選ぶ」の対象から外れる
        var again = ingest.Items.Single(i => i.Path == picked[0].Path);
        Assert.Equal("✓ アップロード済み", again.StateDisplay);
        ingest.AllChecked = true;
        Assert.False(again.IsChecked);
    }

    [Fact]
    public async Task ChangingTheMonthWhileARefreshIsRunningStillShowsTheNewMonth()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        Assert.Equal("2026-10", ingest.SelectedMonth!.Month);

        ingest.RequestRefresh();                                                        // 読み直しが動いている最中に…
        ingest.SelectedMonth = ingest.Months.Single(m => m.Month == "2025-10");          // …月を切り替える
        while (ingest.RefreshCommand.IsRunning) await Task.Delay(10);

        Assert.Equal("2025-10", ingest.SelectedMonth!.Month);
        Assert.All(ingest.Items, i => Assert.Equal("2025-10", i.MonthText));              // 切り替えた月の写真だけ
        Assert.True(ingest.RefreshCommand.CanExecute(null));                             // 「読み直す」は押せる
    }

    [Fact]
    public async Task DirectUploadOnlyAcceptsPngsInsideTheSourceFolder()
    {
        var outside = Path.Combine(_ws.Layout.UploadRoot, "uploaded", "outside.png");
        File.WriteAllBytes(outside, FaraVrcAlbum.Desktop.Demo.SyntheticImages.Png(64, 36, 10, null));

        await Assert.ThrowsAsync<ArgumentException>(() => UploadService.RunSourceFilesAsync(
            [outside], _ws.Session.SourceRoot, new UploadOptions { Apply = true }, PhotoLedger.Empty(), "unused.json", null, DateTimeOffset.UtcNow));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public async Task DryRunWritesNothingAndDeclinedApplyWritesNothing()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        var files = ingest.Items.Where(i => i.StateText == "未登録" && i.ProblemText.Length == 0).Select(i => i.Path).ToList();
        var plan = new PlanViewModel(_ws.App, _ws.Dialogs);
        plan.SetFiles(ToInbox(files));
        var ledgerBefore = File.ReadAllText(_ws.ProductionLedgerPath);
        var uploadBefore = _ws.UploadSnapshot();

        await plan.DryRunCommand.RunAsync();

        Assert.True(files.Count == plan.Rows.Count, $"files={files.Count} rows=[{string.Join(" / ", plan.Rows.Select(r => r.StateText + ":" + r.KeyText))}] status={plan.Status}");
        Assert.All(plan.Rows, row => Assert.Equal("計画", row.StateText));
        Assert.True(plan.ExecuteCommand.CanExecute(null));
        Assert.Equal(uploadBefore, _ws.UploadSnapshot());

        _ws.Dialogs.Answer = false;
        await plan.ExecuteCommand.RunAsync();

        Assert.Equal(["写真をアップロードします"], _ws.Dialogs.Asked);
        Assert.Equal(ledgerBefore, File.ReadAllText(_ws.ProductionLedgerPath));
        Assert.Equal(uploadBefore, _ws.UploadSnapshot());
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_ws.Layout.UploadRoot, "uploaded"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task FileListMustBeInsideTheInbox()
    {
        var outside = Path.Combine(_ws.Layout.UploadRoot, "uploaded", "outside.png");
        File.WriteAllBytes(outside, FaraVrcAlbum.Desktop.Demo.SyntheticImages.Png(64, 36, 10, null));

        await Assert.ThrowsAsync<ArgumentException>(() => UploadService.RunFilesAsync(
            [outside], _ws.Session.InboxRoot, _ws.Session.UploadedRoot, new UploadOptions(), PhotoLedger.Empty(), "unused.json", null, DateTimeOffset.UtcNow));
        Assert.True(File.Exists(outside), "inbox の外のファイルは触らない");
    }

    [Fact]
    public async Task ARunningExecuteBlocksTheDryRunAndChangingTheFiles()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        var files = ingest.Items.Where(i => i.StateText == "未登録" && i.ProblemText.Length == 0).Select(i => i.Path).ToList();
        var gate = new TaskCompletionSource<bool>();
        var plan = new PlanViewModel(new AppState(_ws.Session), new GatedDialogs(gate.Task));
        plan.SetFiles(ToInbox(files));
        await plan.DryRunCommand.RunAsync();

        // 実行は確認ダイアログの答えを待って止まる（その間は実行中）
        var running = plan.ExecuteCommand.RunAsync();
        Assert.False(plan.DryRunCommand.CanExecute(null));
        plan.SetFiles(files.Take(1).ToList());
        Assert.Equal(files.Count, plan.FileCount);

        gate.SetResult(false);
        await running;
        Assert.True(plan.DryRunCommand.CanExecute(null));
    }

    private sealed class GatedDialogs(Task<bool> answer) : IDialogService
    {
        public Task<bool> ConfirmAsync(string title, string message, string confirmLabel) => answer;
    }

    [Fact]
    public async Task ExecuteIsRefusedUntilTheDryRunHasRun()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        var plan = new PlanViewModel(_ws.App, _ws.Dialogs);
        plan.SetFiles(ingest.Items.Select(i => i.Path).Take(1).ToList());

        Assert.False(plan.ExecuteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyOnProductionWritesToTheProductionLedgerPath()
    {
        await _ws.SeedProductionAsync();
        var uploadedBefore = PhotoLedger.Load(_ws.ProductionLedgerPath).BySha.Values.Count(e => e.State == LedgerStates.Uploaded);
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await ingest.RefreshAsync();
        var files = ingest.Items.Where(i => i.StateText == "未登録" && i.ProblemText.Length == 0).Take(1).Select(i => i.Path).ToList();
        var plan = new PlanViewModel(_ws.App, _ws.Dialogs);
        plan.SetFiles(ToInbox(files));
        await plan.DryRunCommand.RunAsync();

        _ws.Dialogs.Answer = true;
        await plan.ExecuteCommand.RunAsync();

        var uploadedAfter = PhotoLedger.Load(_ws.ProductionLedgerPath).BySha.Values.Count(e => e.State == LedgerStates.Uploaded);
        Assert.True(uploadedBefore + 1 == uploadedAfter, $"before={uploadedBefore} after={uploadedAfter} status={plan.Status} rows=[{string.Join(" / ", plan.Rows.Select(r => r.StateText + ":" + r.Message))}]");
        Assert.All(files, f => Assert.True(File.Exists(f), "元の写真は残る"));
    }

    [Fact]
    public async Task TheHeaderCheckboxSelectsEveryPhotoThatIsNotUploadedYetAndClearsThemAgain()
    {
        var ingest = new IngestViewModel(_ws.App, _ws.Dialogs);
        await AllMonthsAsync(ingest);
        var selectable = ingest.Items.Count(i => !i.IsUploaded);
        Assert.True(selectable > 0);
        Assert.False(ingest.AllChecked);

        ingest.AllChecked = true;

        Assert.Equal(selectable, ingest.CheckedCount);
        Assert.True(ingest.AllChecked);
        Assert.All(ingest.Items.Where(i => i.IsUploaded), i => Assert.False(i.IsChecked));        // アップロード済みは選ばない

        ingest.Items.First(i => i.IsChecked).IsChecked = false;                                    // 1 枚外すと、見出しは外れる
        Assert.False(ingest.AllChecked);

        ingest.AllChecked = true;
        ingest.AllChecked = false;
        Assert.Equal(0, ingest.CheckedCount);
    }
}
