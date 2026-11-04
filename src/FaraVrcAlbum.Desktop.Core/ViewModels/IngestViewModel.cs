using System.Collections.ObjectModel;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>inbox の 1 枚の表示行。チェックした分だけ「計画の確認」に渡す。</summary>
public sealed class InboxItemViewModel : ObservableObject
{
    private bool _isChecked;

    public InboxItemViewModel(InboxRow row)
    {
        Path = row.Path;
        FileName = System.IO.Path.GetFileName(row.Path);
        TakenAt = row.TakenAt;
        MonthText = row.FolderMonth ?? "（フォルダ外）";
        TakenText = row.TakenAt is { } taken ? taken.ToOffset(PhotoKeys.Jst).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "撮影日時なし";
        WorldText = row.WorldName is { Length: > 0 } name ? name : row.HasXmp ? "（ワールド名なし）" : "（撮影情報なし）";
        XmpText = row.HasXmp ? "あり" : "なし";
        StateText = row.LedgerState;
        ProblemText = UserText.Plain(row.Problem);
        Key = row.Key ?? "";
    }

    /// <summary>写真の場所（プレビューとコピーに使う）。</summary>
    public string Path { get; }

    /// <summary>写真の名前（ファイル名）。</summary>
    public string FileName { get; }

    /// <summary>撮影日時（並び替え用）。</summary>
    public DateTimeOffset? TakenAt { get; }

    /// <summary>プレビューの下に出す、この写真の状態と注意。</summary>
    public string DetailText => string.Join("　", new[] { "状態: " + StateText, ProblemText.Length > 0 ? "注意: " + ProblemText : "", "撮影情報: " + XmpText }.Where(x => x.Length > 0));

    public string MonthText { get; }

    public string TakenText { get; }

    public string WorldText { get; }

    public string XmpText { get; }

    /// <summary>台帳の状態（未登録・アップロード済み・削除済み）。</summary>
    public string StateText { get; }

    /// <summary>アップロード済みか（一覧で一目で分かるように、状態の列に ✓ を付ける）。</summary>
    public bool IsUploaded => StateText == "アップロード済み";

    /// <summary>一覧の「状態」の列に出す文字。</summary>
    public string StateDisplay => IsUploaded ? "✓ アップロード済み" : StateText;

    /// <summary>取り込まない理由や注意（空なら問題なし）。</summary>
    public string ProblemText { get; }

    public string Key { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (Set(ref _isChecked, value)) SelectionChanged?.Invoke();
        }
    }

    internal event Action? SelectionChanged;
}

/// <summary>月の選択肢（「すべての月」は Month が null）。</summary>
public sealed record MonthOptionViewModel(string? Month)
{
    public string Text => Month is null ? "すべての月" : $"{Month[..4]}年{int.Parse(Month[5..])}月";

    public override string ToString() => Text;
}

/// <summary>
/// 取り込み: PC の VRChat 写真格納フォルダ（yyyy-mm の下）の写真を一覧にする（読むだけ。元の写真は変えない）。
/// チェックした写真を「アップロード内容の確認」（書き込みなしの計画）→「アップロード」（確認ダイアログの後だけ）で、コピーせずに直接アップロードする。
/// 写真は多いので、月ごとに読む（既定は一番新しい月）。
/// </summary>
public sealed class IngestViewModel : PageViewModel
{
    private string _summary = "";
    private MonthOptionViewModel? _selectedMonth;
    private InboxItemViewModel? _selectedItem;

    public IngestViewModel(AppState app, IDialogService dialogs) : base(app, dialogs)
    {
        Items = [];
        Months = [];
        RefreshCommand = Command(RefreshLoopAsync);
        Plan = new PlanViewModel(app, dialogs);
        UploadCommand = Command(UploadAsync, () => CheckedCount > 0 && !Plan.IsBusy);
        // 計画・アップロードの状態行はこの画面の状態行に出す。アップロードが終わったら一覧の状態を読み直す
        Plan.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Status)) Status = Plan.Status;
        };
        Plan.Applied += RequestRefresh;
    }

    /// <summary>計画（書き込みなし）とアップロード。同じ画面の中で使う。</summary>
    public PlanViewModel Plan { get; }

    public ObservableCollection<InboxItemViewModel> Items { get; }

    /// <summary>VRChat 写真格納フォルダの月（先頭は「すべての月」）。</summary>
    public ObservableCollection<MonthOptionViewModel> Months { get; }

    public AsyncCommand RefreshCommand { get; }

    private bool _refreshPending;

    /// <summary>
    /// 読み直しを頼む。読み直しの最中なら、終わったあとにもう一度読む（月を切り替えたのに前の月の一覧のままになるのを防ぐ）。
    /// </summary>
    public void RequestRefresh()
    {
        if (RefreshCommand.IsRunning)
        {
            _refreshPending = true;
            return;
        }
        BackgroundTasks.Fire(RefreshCommand.RunAsync(), Dialogs.ReportError);
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        do
        {
            _refreshPending = false;
            await RefreshAsync(ct);
        }
        while (_refreshPending && !ct.IsCancellationRequested);
    }

    /// <summary>「アップロード」: 計画を作り（書き込みなし）→ 確認ダイアログ（枚数つき）→ アップロード。</summary>
    public AsyncCommand UploadCommand { get; }

    public int CheckedCount => Items.Count(i => i.IsChecked);

    /// <summary>今読んでいる月。変えると、その月を読み直す。</summary>
    public MonthOptionViewModel? SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (Set(ref _selectedMonth, value) && value is not null) RequestRefresh();
        }
    }

    /// <summary>一覧で選んでいる写真（右のプレビューに出す）。</summary>
    public InboxItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!Set(ref _selectedItem, value)) return;
            OnPropertyChanged(nameof(PreviewPath));
            OnPropertyChanged(nameof(PreviewDetail));
        }
    }

    /// <summary>プレビューに出す写真の場所。</summary>
    public string? PreviewPath => SelectedItem?.Path;

    /// <summary>プレビューの下に出す、選んでいる写真の名前・状態・注意。</summary>
    public string PreviewDetail => SelectedItem is { } item ? item.FileName + "\n" + item.DetailText : "";

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    /// <summary>VRChat 写真格納フォルダを読み直す（選んでいる月だけ。ハッシュは local/cache を使う。写真は変えない）。</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var session = Session;
        var source = session.SourceRoot;
        var ledgerPath = session.LedgerPath(session.Bucket);
        Status = "写真を読み込み中です…（写真が多いと時間がかかります）";
        var months = InboxService.Months(source);
        // 月の一覧を作り直す（選んでいた月は残す。無ければ一番新しい月）
        var keep = _selectedMonth?.Month;
        var keepAll = _selectedMonth is not null && keep is null;
        Months.Clear();
        Months.Add(new MonthOptionViewModel(null));
        foreach (var m in months) Months.Add(new MonthOptionViewModel(m));
        _selectedMonth = keepAll ? Months[0]
            : (keep is null ? null : Months.FirstOrDefault(m => m.Month == keep)) ?? (Months.Count > 1 ? Months[1] : Months[0]);
        OnPropertyChanged(nameof(SelectedMonth));
        var month = _selectedMonth.Month;

        var rows = await Task.Run(() =>
        {
            var ledger = PhotoLedger.Load(ledgerPath);
            var cache = HashCache.Load(session.HashCachePath);
            var list = InboxService.ListMonth(source, month, ledger, cache);
            cache.Save();
            return list;
        }, ct);

        foreach (var item in Items) item.SelectionChanged -= OnSelectionChanged;
        Items.Clear();
        foreach (var row in rows)
        {
            var item = new InboxItemViewModel(row);
            item.SelectionChanged += OnSelectionChanged;
            Items.Add(item);
        }
        SelectedItem = null;
        Summary = BuildSummary(rows);
        Status = !Directory.Exists(source)
            ? $"VRChat 写真格納フォルダがありません（{source}）。VRChat が写真を保存するフォルダ（ピクチャの下の VRChat）に写真があるか確かめてください。"
            : Items.Count == 0 ? $"{_selectedMonth.Text}の写真がありません。" : $"{_selectedMonth.Text}の写真を読みました。";
        Refresh();
    }

    /// <summary>見出しのチェックボックス: アップロード済みを除く全部の写真を選んでいるか。入れると全部選び、外すと全部外す。</summary>
    public bool AllChecked
    {
        get
        {
            var selectable = Items.Where(i => !i.IsUploaded).ToList();
            return selectable.Count > 0 && selectable.All(i => i.IsChecked);
        }
        set => SetAll(value);
    }

    private void SetAll(bool value)
    {
        // 「すべて選ぶ」は、アップロード済みの写真を除く（選択を外すときは全部）
        foreach (var item in Items.Where(i => !value || !i.IsUploaded)) item.IsChecked = value;
        Refresh();
    }

    /// <summary>
    /// チェックした写真（VRChat の写真格納フォルダのまま）を、計画（書き込みなし）→ 確認ダイアログ → アップロードの順に処理する。
    /// 確認を断れば何も書かない。コピーはしない。
    /// </summary>
    private async Task UploadAsync(CancellationToken ct)
    {
        var files = Items.Where(i => i.IsChecked).Select(i => i.Path).ToList();
        if (files.Count == 0) return;
        Plan.SetFiles(files);
        await Plan.DryRunCommand.RunAsync();
        if (!Plan.ExecuteCommand.CanExecute(null))
        {
            // 計画でエラーになったときは、その表示を残す。すべて登録済み・拒否のときは理由を出す
            if (Plan.Rows.Count > 0) Status = Plan.Summary("アップロードできる写真がありません");
            return;
        }
        await Plan.ExecuteCommand.RunAsync();
    }

    private void OnSelectionChanged()
    {
        Plan.Reset();                                                                   // 選び直したら、前の計画は古いので捨てる
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(AllChecked));
        UploadCommand.RaiseCanExecuteChanged();
    }

    private static string BuildSummary(IReadOnlyList<InboxRow> rows)
    {
        var uploaded = rows.Count(r => r.LedgerState == "アップロード済み");
        var removed = rows.Count(r => r.LedgerState == "削除済み");
        var notice = rows.Count(r => r.Problem is not null);
        var fresh = rows.Count - uploaded - removed;
        return $"{rows.Count} 枚（未登録 {fresh}・アップロード済み {uploaded}・削除済み {removed}・注意あり {notice}）";
    }
}
