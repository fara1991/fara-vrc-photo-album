using System.Collections.ObjectModel;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>選べる操作 1 つ（画面の表記つき）。</summary>
public sealed record SyncChoice(SyncAction Action, string Label)
{
    public override string ToString() => Label;
}

/// <summary>同期の一覧の 1 行（1 ファイル）。操作は、ファイルごとに選ぶ。</summary>
public sealed class SyncRowViewModel : ObservableObject
{
    private SyncChoice _selected;

    public SyncRowViewModel(SyncItem item)
    {
        Item = item;
        Choices = item.Status switch
        {
            SyncStatus.LocalOnly =>
            [
                None,
                new SyncChoice(SyncAction.Upload, "R2 へ送る"),
                new SyncChoice(SyncAction.DeleteLocal, "ローカルから消す（R2 には無い）"),
            ],
            SyncStatus.RemoteOnly =>
            [
                None,
                new SyncChoice(SyncAction.Download, "ローカルへ取り込む"),
                new SyncChoice(SyncAction.DeleteRemote, "R2 から消す（ローカルには無い）"),
            ],
            SyncStatus.Different =>
            [
                None,
                new SyncChoice(SyncAction.Upload, "ローカルの内容を R2 へ送る（R2 を上書き）"),
                new SyncChoice(SyncAction.Download, "R2 の内容をローカルへ取り込む（ローカルを上書き）"),
            ],
            _ => [None],
        };
        _selected = Choices[0];
    }

    private static readonly SyncChoice None = new(SyncAction.None, "何もしない");

    public SyncItem Item { get; }

    public string Path => Item.Path;

    public string StatusText => Item.Status switch
    {
        SyncStatus.Same => "同じ",
        SyncStatus.LocalOnly => "ローカルにだけある",
        SyncStatus.RemoteOnly => "R2 にだけある",
        _ => "内容が違う",
    };

    public string LocalText => Item.LocalBytes is { } n ? $"{n:N0} バイト" : "なし";

    public string RemoteText => Item.RemoteBytes is { } n ? $"{n:N0} バイト" : "なし";

    public IReadOnlyList<SyncChoice> Choices { get; }

    /// <summary>選べるものが 2 つ以上あるとき（同じファイルは選べない）。</summary>
    public bool CanChoose => Choices.Count > 1;

    public SyncChoice Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public SyncAction Action => _selected.Action;

    /// <summary>足りない側へ、そのまま写す操作（ローカルにだけ → 送る・R2 にだけ → 取り込む）。内容が違うものは、自分で選ぶ。消す操作は選ばない。</summary>
    public void SelectCopyToMissingSide() => Select(Item.Status switch
    {
        SyncStatus.LocalOnly => SyncAction.Upload,
        SyncStatus.RemoteOnly => SyncAction.Download,
        _ => SyncAction.None,
    });

    /// <summary>
    /// 片方に合わせる操作。toRemote が true なら R2 をローカルに合わせる（ローカルにだけ → 送る・R2 にだけ → R2 から消す・内容が違う → 送る）。
    /// false ならローカルを R2 に合わせる（R2 にだけ → 取り込む・ローカルにだけ → ローカルから消す・内容が違う → 取り込む）。
    /// </summary>
    public void SelectMirror(bool toRemote) => Select(Item.Status switch
    {
        SyncStatus.LocalOnly => toRemote ? SyncAction.Upload : SyncAction.DeleteLocal,
        SyncStatus.RemoteOnly => toRemote ? SyncAction.DeleteRemote : SyncAction.Download,
        SyncStatus.Different => toRemote ? SyncAction.Upload : SyncAction.Download,
        _ => SyncAction.None,
    });

    private void Select(SyncAction wanted) => Selected = Choices.FirstOrDefault(c => c.Action == wanted) ?? Choices[0];
}

/// <summary>
/// 同期の画面: 記事の中身（content/ の JSON）が、ローカルと R2（控え用のバケット）で揃っているかを一覧にして、
/// どちらかに無い・中身が違うファイルについて、ファイルごとに「送る・取り込む・何もしない」を選んで同期する。消す操作は無い。
/// </summary>
public sealed class SyncViewModel : PageViewModel
{
    private readonly Func<string, StoreHandle> _openStore;
    private bool _busy;
    private string _summary = "「R2 と比べる」を押すと、ローカルと R2 の記事の中身を比べます。";

    /// <param name="openStore">控え用のバケットを開く（テストが差し替える）。既定は、設定の R2。</param>
    public SyncViewModel(AppState app, IDialogService dialogs, Func<string, StoreHandle>? openStore = null) : base(app, dialogs)
    {
        _openStore = openStore ?? (bucket => Session.OpenStore(bucket));
        CompareCommand = Command(CompareAsync, () => !_busy);
        SyncMissingCommand = new RelayCommand(SelectMissing, () => !_busy && Rows.Any(r => r.Item.Status is SyncStatus.LocalOnly or SyncStatus.RemoteOnly));
        MirrorToRemoteCommand = new RelayCommand(() => SelectMirror(toRemote: true), () => !_busy && Rows.Any(r => r.Item.Status != SyncStatus.Same));
        MirrorToLocalCommand = new RelayCommand(() => SelectMirror(toRemote: false), () => !_busy && Rows.Any(r => r.Item.Status != SyncStatus.Same));
        ApplyCommand = Command(ApplyAsync, () => !_busy && Rows.Any(r => r.Action != SyncAction.None));
    }

    public ObservableCollection<SyncRowViewModel> Rows { get; } = [];

    public AsyncCommand CompareCommand { get; }

    /// <summary>ローカルにだけ・R2 にだけあるファイルを、すべて足りない側へ写す操作にする（実行はしない。内容が違うものは変えない）。</summary>
    public RelayCommand SyncMissingCommand { get; }

    /// <summary>R2 をローカルに合わせる操作を選ぶ（実行はしない）。R2 にだけあるファイルは「R2 から消す」になる。</summary>
    public RelayCommand MirrorToRemoteCommand { get; }

    /// <summary>ローカルを R2 に合わせる操作を選ぶ（実行はしない）。ローカルにだけあるファイルは「ローカルから消す」になる。</summary>
    public RelayCommand MirrorToLocalCommand { get; }

    public AsyncCommand ApplyCommand { get; }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public bool IsBusy => _busy;

    private string ContentDir => Session.Repo.ContentDir;

    private string BackupBucketName() => ContentBackupService.RequireBackupBucket(Session.BackupBucket, Session.ProductionBucket);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        OnPropertyChanged(nameof(IsBusy));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        CompareCommand.RaiseCanExecuteChanged();
        SyncMissingCommand.RaiseCanExecuteChanged();
        MirrorToRemoteCommand.RaiseCanExecuteChanged();
        MirrorToLocalCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
    }

    private async Task CompareAsync(CancellationToken ct)
    {
        if (Session.IsOffline)
        {
            Status = "デモでは、R2 と同期しません。";
            return;
        }
        var bucket = BackupBucketName();
        SetBusy(true);
        try
        {
            Status = "R2 と比べています…";
            IReadOnlyList<SyncItem> items;
            using (var handle = _openStore(bucket))
                items = await SettingsViewModel.Guard(bucket, () => ContentSyncService.CompareAsync(ContentDir, handle.Store, ct));
            foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
            Rows.Clear();
            foreach (var item in items)
            {
                var row = new SyncRowViewModel(item);
                row.PropertyChanged += OnRowChanged;
                Rows.Add(row);
            }
            var counts = items.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
            int Count(SyncStatus s) => counts.GetValueOrDefault(s);
            Summary = $"{items.Count} ファイル: 同じ {Count(SyncStatus.Same)}・ローカルにだけ {Count(SyncStatus.LocalOnly)}・R2 にだけ {Count(SyncStatus.RemoteOnly)}・内容が違う {Count(SyncStatus.Different)}（R2 は {bucket} の sync/）";
            Status = items.All(i => i.Status == SyncStatus.Same) ? "ローカルと R2 は同じです。" : "違いがあります。ファイルごとに操作を選んでください。";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SyncRowViewModel.Selected)) ApplyCommand.RaiseCanExecuteChanged();
    }

    private void SelectMissing()
    {
        foreach (var row in Rows) row.SelectCopyToMissingSide();
        RaiseCommands();
        Status = "足りないファイルを、足りない側へ写す操作を選びました。内容が違うファイルは、自分で選んでください。「選んだ操作を実行する」で実行します。";
    }

    private void SelectMirror(bool toRemote)
    {
        foreach (var row in Rows) row.SelectMirror(toRemote);
        RaiseCommands();
        Status = (toRemote ? "R2 をローカルに合わせる操作を選びました（R2 にだけあるファイルは、R2 から消します）。" : "ローカルを R2 に合わせる操作を選びました（ローカルにだけあるファイルは、ローカルから消します）。")
            + "「選んだ操作を実行する」で実行します（実行の前に確認が出ます）。";
    }

    private async Task ApplyAsync(CancellationToken ct)
    {
        var chosen = Rows.Where(r => r.Action != SyncAction.None).ToList();
        if (chosen.Count == 0)
        {
            Status = "操作が選ばれていません。";
            return;
        }
        var bucket = BackupBucketName();
        var ups = chosen.Count(r => r.Action == SyncAction.Upload);
        var downs = chosen.Count(r => r.Action == SyncAction.Download);
        var delLocal = chosen.Count(r => r.Action == SyncAction.DeleteLocal);
        var delRemote = chosen.Count(r => r.Action == SyncAction.DeleteRemote);
        var overwrites = chosen.Count(r => r.Item.Status == SyncStatus.Different);
        var safety = Path.Combine(Session.Repo.LocalDir, "sync-backups", DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMdd-HHmmss"));
        var confirmed = await Dialogs.ConfirmAsync(
            "選んだ操作で同期します",
            $"R2 へ送る {ups} 件・ローカルへ取り込む {downs} 件（そのうち、上書きするもの {overwrites} 件）・ローカルから消す {delLocal} 件・R2 から消す {delRemote} 件を行います。\n"
            + $"上書き・消すローカルのファイルは、先に {safety} に残します（R2 から消すファイルは、その中の {ContentSyncService.RemoteBackupFolder} に残します）。変えたあとの記事の中身が読めなければ、ローカルは元に戻します。\n"
            + (delLocal + delRemote > 0 ? "消す操作が含まれています。内容をよく確かめてから実行してください。" : "ファイルを消す操作は含まれていません。"),
            delLocal + delRemote > 0 ? "消すことを含めて同期する" : "同期する");
        if (!confirmed) return;

        var done = "";
        SetBusy(true);
        try
        {
            Status = "同期しています…";
            var actions = chosen.Select(r => (r.Path, r.Action)).ToList();
            SyncResult result;
            using (var handle = _openStore(bucket))
                result = await SettingsViewModel.Guard(bucket, () => ContentSyncService.ApplyAsync(ContentDir, handle.Store, actions, safety, ct));
            done = $"同期しました（R2 へ送った {result.Uploaded} 件・ローカルへ取り込んだ {result.Downloaded} 件・ローカルから消した {result.DeletedLocal} 件・R2 から消した {result.DeletedRemote} 件）。";
        }
        finally
        {
            SetBusy(false);
        }
        await CompareAsync(ct);                                                              // 結果を比べ直す
        Status = done + " " + Status;
        await Dialogs.InformAsync("同期が終わりました", done + "\n" + Summary);
    }
}
