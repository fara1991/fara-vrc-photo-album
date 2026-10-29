using System.Collections.ObjectModel;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>計画・実行の 1 行（ファイル単位の結果）。ファイル名は表示しない（キーと状態で示す）。</summary>
public sealed class PlanRowViewModel(string number, string keyText, string stateText, string message, string warnings, bool failed, string? previewPath = null)
{
    /// <summary>プレビューに出す写真の場所（inbox の写真、アップロード後はアップロード済みのフォルダ）。</summary>
    public string? PreviewPath { get; } = previewPath;

    public string Number { get; } = number;

    public string KeyText { get; } = keyText;

    public string StateText { get; } = stateText;

    public string Message { get; } = message;

    public string Warnings { get; } = warnings;

    public bool Failed { get; } = failed;
}

/// <summary>
/// 計画の確認（ドライラン）と実行（--apply 相当）。既定はドライラン。実行は確認ダイアログの後だけ。
/// 進捗は 1 枚ごとに出し、キャンセルは次の 1 枚の前で止まる（書きかけの 1 枚は、次の実行で不足分だけ補われる）。
/// </summary>
public sealed class PlanViewModel : PageViewModel
{
    private IReadOnlyList<string> _files = [];
    private bool _dryRunDone;
    private int _runSerial;
    private string _progress = "";
    private string _header = "写真にチェックを入れて「アップロード内容の確認」を押すと、ここに結果が出ます。";

    public PlanViewModel(AppState app, IDialogService dialogs) : base(app, dialogs)
    {
        Rows = [];
        // 計画と実行は、どちらかが動いている間は両方とも不可（二重の書き込みや、途中の対象の差し替えを防ぐ）
        DryRunCommand = Command(ct => RunAsync(apply: false, ct), () => _files.Count > 0 && ExecuteCommand is not { IsRunning: true });
        ExecuteCommand = Command(ExecuteAsync, () => _dryRunDone && HasPlannedRows && !DryRunCommand.IsRunning);
        CancelCommand = new RelayCommand(Cancel, () => DryRunCommand.IsRunning || ExecuteCommand.IsRunning);
    }

    public ObservableCollection<PlanRowViewModel> Rows { get; }

    public AsyncCommand DryRunCommand { get; }

    /// <summary>実行（R2 に書く・台帳に登録・uploaded/ へ移す）。ドライランの後だけ有効。</summary>
    public AsyncCommand ExecuteCommand { get; }

    public RelayCommand CancelCommand { get; }

    /// <summary>実行（アップロード）が最後まで終わったときに知らせる（取り込みの一覧の状態を読み直すため）。</summary>
    public event Action? Applied;

    /// <summary>計画か実行が動いている間は true。</summary>
    public bool IsBusy => DryRunCommand.IsRunning || ExecuteCommand.IsRunning;

    /// <summary>対象が変わったので、前の計画を捨てる（動いている間は何もしない）。</summary>
    public void Reset()
    {
        if (IsBusy) return;
        _files = [];
        ClearPlan();
        Header = "写真にチェックを入れて「アップロード内容の確認」を押すと、ここに結果が出ます。";
        OnPropertyChanged(nameof(FileCount));
        DryRunCommand.RaiseCanExecuteChanged();
    }

    private PlanRowViewModel? _selectedRow;

    /// <summary>一覧で選んでいる行（右のプレビューに出す）。</summary>
    public PlanRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (Set(ref _selectedRow, value)) OnPropertyChanged(nameof(PreviewPath));
        }
    }

    public string? PreviewPath => SelectedRow?.PreviewPath;

    public string Header
    {
        get => _header;
        private set => Set(ref _header, value);
    }

    public string Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    /// <summary>対象の枚数（チェックした分）。</summary>
    public int FileCount => _files.Count;

    private bool HasPlannedRows => Rows.Any(r => r.StateText == "計画");

    /// <summary>結果の要約（新規・登録済み・拒否・失敗の数と、問題のあった写真の理由）。</summary>
    public string Summary(string lead)
    {
        var problems = Rows.Where(r => r.StateText is "拒否" or "失敗" or "保留")
            .Take(3).Select(r => (r.KeyText.Length > 0 ? r.KeyText + ": " : "") + r.Message).ToList();
        var count = (string state) => Rows.Count(r => r.StateText == state);
        var text = $"{lead}（登録済み {count("登録済み")}・拒否 {count("拒否")}・失敗 {count("失敗")}・保留 {count("保留")}）";
        return problems.Count == 0 ? text + "。" : text + "。 " + string.Join(" / ", problems);
    }

    /// <summary>チェックした分（VRChat の写真格納フォルダの写真）を受け取る（前の計画は捨てる）。</summary>
    public void SetFiles(IReadOnlyList<string> files)
    {
        if (DryRunCommand.IsRunning || ExecuteCommand.IsRunning)
        {
            Status = "計画または実行の途中なので、対象は変えられません（終わるか中止してから選び直してください）。";
            return;
        }
        _files = files.ToList();
        ClearPlan();
        Header = $"対象 {_files.Count} 枚。";
        OnPropertyChanged(nameof(FileCount));
        DryRunCommand.RaiseCanExecuteChanged();
    }

    private void ClearPlan()
    {
        Rows.Clear();
        _dryRunDone = false;
        Progress = "";
        ExecuteCommand.RaiseCanExecuteChanged();
    }

    private void Cancel()
    {
        DryRunCommand.Cancel();
        ExecuteCommand.Cancel();
        Status = "中止しました。途中までにアップロードした写真はそのまま残ります（残りは、もう一度アップロードすると続きから行います）。";
    }

    private async Task ExecuteAsync(CancellationToken ct)
    {
        var confirmed = await Dialogs.ConfirmAsync(
            "写真をアップロードします",
            $"選んだ {_files.Count} 枚のうち、まだアップロードしていない {Rows.Count(r => r.StateText == "計画")} 枚を、サイトの写真の保存先にアップロードします（登録済み {Rows.Count(r => r.StateText == "登録済み")} 枚・アップロードできない {Rows.Count(r => r.StateText is "拒否" or "失敗" or "保留")} 枚は対象外）。\n" +
            "アップロードした写真は、記事に入れるとサイトに表示されます。元の写真（VRChat の写真格納フォルダ）はそのまま残ります。" +
            "\n\nアップロードしますか。",
            "アップロードする");
        if (!confirmed) return;
        await RunAsync(apply: true, ct);
    }

    private async Task RunAsync(bool apply, CancellationToken ct)
    {
        var bucket = Session.Bucket;
        var ledgerPath = Session.LedgerPath(bucket);
        var files = _files;
        Rows.Clear();
        Progress = $"0 / {files.Count}";

        DryRunCommand.RaiseCanExecuteChanged();
        ExecuteCommand.RaiseCanExecuteChanged();
        var total = files.Count;
        var done = 0;
        // Progress<T> の通知は後から届くことがあるので、この実行の番号と合うものだけ反映する（終わったら番号を進める）
        var run = ++_runSerial;
        void OnItem(UploadItem item)
        {
            if (run != _runSerial) return;
            done++;
            Progress = $"{done} / {total}";
            Rows.Add(ToRow(done, item));
        }
        // 画面（UI の同期コンテキストがある）では Progress<T> が UI スレッドに戻す。無ければ（テスト）は直接呼ぶ
        IProgress<UploadItem> reporter = SynchronizationContext.Current is null
            ? new DirectProgress<UploadItem>(OnItem)
            : new Progress<UploadItem>(OnItem);

        LockFile? lockFile = apply ? LockFile.Acquire(Session.LockPath) : null;
        try
        {
            var ledger = PhotoLedger.Load(ledgerPath);
            // 計画（書かない）は、認証情報が無ければ R2 の存在確認を省く（CLI の upload と同じ）。実行は認証情報が要る
            using var handle = apply || Session.HasCredentials || Session.IsOffline ? Session.OpenStore(bucket) : null;
            var options = new UploadOptions { Apply = apply, TestMode = false, Secrets = Secrets.Known(Session.Repo) };
            // 元の写真は VRChat の写真格納フォルダのまま読む（コピーも移動もしない）
            var report = await Task.Run(async () => await UploadService.RunSourceFilesAsync(
                files,
                Session.SourceRoot,
                options,
                ledger,
                ledgerPath,
                handle?.Store,
                DateTimeOffset.UtcNow,
                reporter.Report,
                ct), ct);

            _runSerial++;
            Rows.Clear();
            var number = 0;
            foreach (var item in report.Items) Rows.Add(ToRow(++number, item));

            _dryRunDone = !apply;
            var line = $"{(apply ? "実行" : "計画")}: 新規 {report.Count(IngestState.Uploaded)} / 登録済み {report.Count(IngestState.AlreadyUploaded)} / 計画 {report.Count(IngestState.Planned)} / 拒否 {report.Count(IngestState.Refused)} / 失敗 {report.Failed} / 保留 {report.Skipped}";
            Status = apply ? Summary($"アップロードしました: 新規 {report.Count(IngestState.Uploaded)}・登録済み {report.Count(IngestState.AlreadyUploaded)}") : line + "。";
            if (apply) Applied?.Invoke();
        }
        finally
        {
            _runSerial++;
            lockFile?.Dispose();
            ExecuteCommand.RaiseCanExecuteChanged();
            DryRunCommand.RaiseCanExecuteChanged();
        }
    }

    private PlanRowViewModel ToRow(int number, UploadItem item)
    {
        if (item.Skipped) return new PlanRowViewModel(number.ToString(), "", "保留", "書き込み中かもしれないため、後で", "", false);
        if (item.Error is not null) return new PlanRowViewModel(number.ToString(), "", "失敗", UserText.Plain(Secrets.Redact(item.Error, Session.Repo)), "", true);
        var result = item.Result!;
        var state = result.State switch
        {
            IngestState.Planned => "計画",
            IngestState.Uploaded => "新規",
            IngestState.AlreadyUploaded => "登録済み",
            IngestState.Refused => "拒否",
            _ => result.State.ToString(),
        };
        var warnings = UserText.Plain(string.Join("；", result.Warnings));
        var message = UserText.Plain(result.Message) + (item.MovedTo is not null ? "（アップロード済みのフォルダへ移動）" : "");
        var preview = item.MovedTo ?? _files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), item.SourceName, StringComparison.OrdinalIgnoreCase));
        return new PlanRowViewModel(number.ToString(), result.Key ?? "", state, message, warnings, false, preview);
    }
}

/// <summary>同期コンテキストが無いときの進捗（呼び出したスレッドでそのまま処理する）。</summary>
internal sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
