using System.Collections.ObjectModel;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>フォルダから読んだ名前の 1 行。登録済みの名前はチェックできない（二重に登録しない）。</summary>
public sealed class ImportCandidateViewModel(ImportEntry entry, bool registered, bool registeredCheckable = false) : ObservableObject
{
    private bool _isChecked;

    public ImportEntry Entry { get; } = entry;

    public string Name => Entry.Name;

    /// <summary>一覧の補足（お店など）。</summary>
    public string Detail => Entry.Detail;

    /// <summary>アバター名から予測した服の名前（無ければ空）。</summary>
    public string Suggestion => Entry.Suggestion;

    public bool Registered { get; } = registered;

    /// <summary>チェックできる（登録済みは、登録済みへの操作があるときだけ）。</summary>
    public bool CanCheck => !Registered || registeredCheckable;

    public string StateText => Registered ? "登録済み" : "未登録";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (!CanCheck && value) return;
            Set(ref _isChecked, value);
        }
    }
}

/// <summary>
/// 「フォルダを指定 → 読み直す → 表からチェック → 取り込む」の部品（アバター・服で共通）。
/// フォルダの中身は読むだけ。取り込みは <c>import</c> が行う（確認ダイアログとマスタへの書き込みは呼び出し側）。
/// </summary>
public sealed class FolderImportViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly string _pickTitle;
    private readonly Action<string> _save;
    private readonly Func<string, ImportScan> _scan;
    private readonly Func<ImportEntry, bool> _isRegistered;
    private readonly Func<IReadOnlyList<ImportEntry>, Task<bool>> _import;
    private readonly Func<IReadOnlyList<ImportEntry>, Task<bool>>? _applyToRegistered;
    private readonly Action<string> _report;
    private readonly bool _registeredViaImport;
    private string _folder;
    private string _summary = "";
    private string _notice = "";
    private bool _linkSuggested = true;

    public FolderImportViewModel(
        IDialogService dialogs,
        string pickTitle,
        string folder,
        Action<string> save,
        Func<string, ImportScan> scan,
        Func<ImportEntry, bool> isRegistered,
        Func<IReadOnlyList<ImportEntry>, Task<bool>> import,
        Action<string> report,
        Action<Exception> onError,
        Func<IReadOnlyList<ImportEntry>, Task<bool>>? applyToRegistered = null,
        bool registeredViaImport = false)
    {
        _applyToRegistered = applyToRegistered;
        _registeredViaImport = registeredViaImport;
        _dialogs = dialogs;
        _pickTitle = pickTitle;
        _folder = folder;
        _save = save;
        _scan = scan;
        _isRegistered = isRegistered;
        _import = import;
        _report = text =>
        {
            Notice = text;
            report(text);
        };
        Candidates = [];
        BrowseCommand = new RelayCommand(Browse);
        ScanCommand = new AsyncCommand(_ => { Scan(); return Task.CompletedTask; }, onError, () => Folder.Trim().Length > 0);
        ImportCommand = new AsyncCommand(ImportAsync, onError, () => Candidates.Any(c => c.IsChecked && (!c.Registered || _registeredViaImport)));
        ApplyRegisteredCommand = new AsyncCommand(ApplyRegisteredAsync, onError, () => Candidates.Any(c => c.IsChecked && c.Registered));
    }

    public ObservableCollection<ImportCandidateViewModel> Candidates { get; }

    public RelayCommand BrowseCommand { get; }

    public AsyncCommand ScanCommand { get; }

    public AsyncCommand ImportCommand { get; }

    /// <summary>登録済みの名前に対する操作（アバターの予測した服を着せる、など）。無いときは null。</summary>
    public AsyncCommand ApplyRegisteredCommand { get; }

    /// <summary>登録済みへの操作があるか（画面に出すか）。</summary>
    public bool HasRegisteredAction => _applyToRegistered is not null;

    /// <summary>
    /// 登録済みの名前への操作も、「インポート」のボタンで行うか（服: 登録済みの服に、BOOTH の画像・情報を取り込み直す）。
    /// true のとき、別のボタン（<see cref="ApplyRegisteredCommand"/>）は出さない。
    /// </summary>
    public bool RegisteredViaImport => _registeredViaImport;

    private bool _linkOverwrite;

    /// <summary>登録済みのアバターに、すでに服があっても予測した服に置き換える。</summary>
    public bool LinkOverwrite
    {
        get => _linkOverwrite;
        set => Set(ref _linkOverwrite, value);
    }

    /// <summary>見出しのチェックボックス（全選択）。チェックできるものがすべて選ばれていれば true、一部なら null。</summary>
    public bool? AllChecked
    {
        get
        {
            var pickable = Candidates.Where(c => c.CanCheck).ToList();
            if (pickable.Count == 0 || pickable.All(c => !c.IsChecked)) return false;
            return pickable.All(c => c.IsChecked) ? true : null;
        }
        set
        {
            foreach (var c in Candidates.Where(c => c.CanCheck)) c.IsChecked = value == true;
            Refresh();
        }
    }

    /// <summary>読むフォルダ。変えると手元に覚える。</summary>
    public string Folder
    {
        get => _folder;
        set
        {
            if (!Set(ref _folder, value)) return;
            _save(value.Trim());
            ScanCommand.RaiseCanExecuteChanged();
        }
    }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    /// <summary>登録するときに、予測した服もアバターに着せる（紐づける）か。</summary>
    public bool LinkSuggested
    {
        get => _linkSuggested;
        set => Set(ref _linkSuggested, value);
    }

    /// <summary>直前の操作の結果（読み直した・登録した・フォルダが無い、など）。ウィンドウに出す。</summary>
    public string Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    public int CheckedCount => Candidates.Count(c => c.IsChecked);

    private void Browse()
    {
        var picked = _dialogs.PickFolder(_pickTitle, Folder);
        if (picked is null) return;
        Folder = picked;
        Scan();
    }

    /// <summary>フォルダを読み直して、候補の表を作り直す（何も書かない）。</summary>
    public void Scan()
    {
        var folder = Folder.Trim();
        foreach (var c in Candidates) c.PropertyChanged -= OnCandidateChanged;
        Candidates.Clear();
        if (!Directory.Exists(folder))
        {
            Summary = "フォルダが見つかりません。";
            _report("フォルダが見つかりません: " + folder);
            Refresh();
            return;
        }
        var scan = _scan(folder);
        foreach (var entry in scan.Entries)
        {
            var candidate = new ImportCandidateViewModel(entry, _isRegistered(entry), registeredCheckable: _applyToRegistered is not null);
            candidate.PropertyChanged += OnCandidateChanged;
            Candidates.Add(candidate);
        }
        var fresh = Candidates.Count(c => !c.Registered);
        var boothWarnings = BoothLibrary.TakeWarnings();                               // 消せなかった BOOTH の一時フォルダ（購入履歴を含むコピー）を出す
        Summary = $"{Candidates.Count} 件（未登録 {fresh}・登録済み {Candidates.Count - fresh}）" + scan.Note + string.Concat(boothWarnings.Select(w => " " + w));
        _report(Candidates.Count == 0 ? "フォルダの中に該当するものがありませんでした。" : "フォルダを読みました。登録するものにチェックを入れてください。");
        Refresh();
    }

    private async Task ImportAsync(CancellationToken ct)
    {
        var entries = Candidates.Where(c => c.IsChecked && !c.Registered).Select(c => c.Entry).ToList();
        var existing = _registeredViaImport && _applyToRegistered is not null
            ? Candidates.Where(c => c.IsChecked && c.Registered).Select(c => c.Entry).ToList()
            : [];
        if (entries.Count + existing.Count == 0) return;
        _lastSaid = "";
        var registered = entries.Count > 0 && await _import(entries);
        var applied = existing.Count > 0 && await _applyToRegistered!(existing);
        if (!registered && !applied) return;                                           // 何も変えていない（断った・理由のメッセージが出ている）
        Scan();
        if (_lastSaid.Length > 0) _report(_lastSaid);
        else if (registered) _report($"{entries.Count} 件を登録しました。下の一覧で「登録済み」になっています。");
    }

    private string _lastSaid = "";

    /// <summary>結果のメッセージを、ウィンドウと状態行に出す（読み直しで消えないよう、覚えておく）。</summary>
    public void Say(string text)
    {
        _lastSaid = text;
        _report(text);
    }

    private async Task ApplyRegisteredAsync(CancellationToken ct)
    {
        if (_applyToRegistered is null) return;
        var entries = Candidates.Where(c => c.IsChecked && c.Registered).Select(c => c.Entry).ToList();
        if (entries.Count == 0) return;
        _lastSaid = "";
        var done = await _applyToRegistered(entries);
        if (!done) return;                                                              // 何も変えていないので、読み直さず、理由のメッセージを残す
        Scan();
        if (_lastSaid.Length > 0) _report(_lastSaid);
    }

    private void OnCandidateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportCandidateViewModel.IsChecked)) Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CheckedCount));
        OnPropertyChanged(nameof(AllChecked));
        ImportCommand.RaiseCanExecuteChanged();
        ApplyRegisteredCommand.RaiseCanExecuteChanged();
    }
}
