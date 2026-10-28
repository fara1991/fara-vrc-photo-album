using System.Collections.ObjectModel;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>服のマスタの 1 行。</summary>
public sealed class OutfitRowViewModel(string id, OutfitMasterEntry entry, int users) : ObservableObject
{
    private bool _isChecked;

    /// <summary>まとめて削除するために選んでいる。</summary>
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    public string Id { get; } = id;
    public string Name { get; } = entry.Name;
    public string Shop { get; } = entry.Shop ?? "";
    public string Url { get; } = entry.Url ?? "";
    public string Note { get; } = entry.Note ?? "";

    /// <summary>一覧に出すサムネイル画像の URL（BOOTH のもの。無ければ null）。</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>この服を着せているアバターの数（画面の一覧に出す）。</summary>
    public int Users { get; } = users;

    public override string ToString() => $"{Name}（{Id}）";
}

/// <summary>アバターのマスタの 1 行。</summary>
public sealed class AvatarRowViewModel(string id, AvatarMasterEntry entry, IReadOnlyDictionary<string, OutfitMasterEntry> outfits) : ObservableObject
{
    private bool _isChecked;

    /// <summary>まとめて削除するために選んでいる。</summary>
    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    public string Id { get; } = id;
    public string Name { get; } = entry.Name;
    public IReadOnlyList<string> Aliases { get; } = entry.Aliases;
    public IReadOnlyList<string> OutfitIds { get; } = entry.OutfitIds;
    public string Note { get; } = entry.Note ?? "";

    /// <summary>一覧に出すサムネイル画像のファイル（手元の Unity のプロジェクトにあるもの。見つからなければ null）。</summary>
    public string? ThumbnailPath { get; init; }

    public string AliasesText => string.Join("、", Aliases);

    /// <summary>着せている服（名前・順序つき）。</summary>
    public string OutfitsText => OutfitIds.Count == 0 ? "（服なし）" : string.Join("、", OutfitIds.Select(o => outfits.TryGetValue(o, out var e) ? e.Name : o));

    /// <summary>選ぶときに出す文（アバター名と id。名前・別名で検索できるように、先頭は名前）。</summary>
    public string Text => $"{Name}（{Id}）";

    public override string ToString() => Text;
}

/// <summary>アバターに着せる服の選択肢（1 つだけ選ぶ。「（なし）」は Id が空）。</summary>
public sealed record OutfitPickViewModel(string Id, string Name)
{
    public static readonly OutfitPickViewModel None = new("", "（なし）");

    public override string ToString() => Name;
}

/// <summary>
/// アバター・服のマスタ（content/avatars.json・content/outfits.json）の画面。
/// 一覧・追加・編集・削除と、アバターへの服の紐づけ。書くのは content/ だけ（R2 には触らない）。
/// どの操作も、先に検査（AvatarOutfitService の apply:false）を通してから確認し、書いたあとにもう一度検査する（CLI と同じ）。
/// アバター名は VRChat の表記をそのまま貼り付けて登録する（サイトには出ない）。
/// </summary>
public sealed class AvatarOutfitViewModel : PageViewModel
{
    private OutfitRowViewModel? _selectedOutfit;
    private AvatarRowViewModel? _selectedAvatar;
    private string _outfitId = "";
    private string _outfitName = "";
    private string _outfitShop = "";
    private string _outfitUrl = "";
    private string _outfitNote = "";
    private string _avatarId = "";
    private string _avatarName = "";
    private string _avatarAliases = "";
    private string _avatarNote = "";
    private OutfitPickViewModel _selectedOutfitPick = OutfitPickViewModel.None;
    private string _extraOutfitsNote = "";

    private readonly Func<IReadOnlyDictionary<long, AvatarOutfitService.BoothInfo>> _boothDetails;
    private readonly Func<string, ImportScan>? _outfitScan;

    /// <param name="boothDetails">BOOTH の商品 ID → 商品の情報を読む（テストが差し替える）。既定は、BOOTH Library Manager のデータ。</param>
    /// <param name="outfitScan">服のフォルダを読む（テストが差し替える）。既定は、BOOTH Library Manager のフォルダの読み方。</param>
    public AvatarOutfitViewModel(AppState app, IDialogService dialogs, Func<IReadOnlyDictionary<long, AvatarOutfitService.BoothInfo>>? boothDetails = null, Func<string, ImportScan>? outfitScan = null) : base(app, dialogs)
    {
        _outfitScan = outfitScan;
        _boothDetails = boothDetails ?? (() => BoothLibrary.LoadDetails(Session.Preferences.BoothDatabase is { Length: > 0 } custom ? custom : null));
        Outfits = [];
        Avatars = [];
        OutfitPicks = [OutfitPickViewModel.None];
        RefreshCommand = Command(_ => { Refresh(); return Task.CompletedTask; });
        // 2 件以上チェックしているあいだは、編集の保存はできない（保存できるのは、1 件を選んで編集しているときだけ）
        SaveOutfitCommand = Command(_ => SaveOutfitAsync(), () => OutfitName.Trim().Length > 0 && Outfits.Count(o => o.IsChecked) <= 1);
        RemoveOutfitCommand = Command(_ => RemoveOutfitsAsync(), () => TargetOutfits().Count > 0);
        NewOutfitCommand = new RelayCommand(() => SelectedOutfit = null);
        SaveAvatarCommand = Command(_ => SaveAvatarAsync(), () => AvatarName.Trim().Length > 0 && Avatars.Count(a => a.IsChecked) <= 1);
        // Unity のフォルダから名前の一覧を読み、チェックしたものをまとめて登録する（フォルダは手元に覚える）
        AvatarImport = new FolderImportViewModel(Dialogs, "アバターの prefab があるフォルダを選ぶ", Session.Preferences.AvatarFolder,
            folder => Session.Preferences.AvatarFolder = folder,
            folder => new ImportScan(FolderScan.PrefabNames(folder).Select(AvatarEntry).ToList(), "（アバター名から、着ている服を予測して出しています。当たるとは限らないので、確かめてください）"),
            entry => Avatars.Any(a => a.Name == entry.Name || a.Aliases.Contains(entry.Name)),
            ImportAvatarsAsync, text => Status = text, ReportError, ApplySuggestionsToRegisteredAsync);
        OutfitImport = new FolderImportViewModel(Dialogs, "服のフォルダを選ぶ", Session.Preferences.OutfitFolder,
            folder => Session.Preferences.OutfitFolder = folder,
            folder => _outfitScan?.Invoke(folder) ?? FolderScan.OutfitCandidates(folder, Session.Preferences.BoothDatabase is { Length: > 0 } db ? db : null),
            entry => Outfits.Any(o => o.Name == entry.Name || (entry.Url is not null && o.Url == entry.Url)),
            ImportOutfitsAsync, text => Status = text, ReportError, ReimportOutfitsAsync, registeredViaImport: true);
        RemoveAvatarCommand = Command(_ => RemoveAvatarsAsync(), () => TargetAvatars().Count > 0);
        NewAvatarCommand = new RelayCommand(() => SelectedAvatar = null);
    }

    public ObservableCollection<OutfitRowViewModel> Outfits { get; }

    /// <summary>アバター名の取り込み（Unity のフォルダの prefab 名）。</summary>
    public FolderImportViewModel AvatarImport { get; }

    /// <summary>服の名前の取り込み（フォルダの一覧）。</summary>
    public FolderImportViewModel OutfitImport { get; }

    public ObservableCollection<AvatarRowViewModel> Avatars { get; }

    /// <summary>アバターに着せる服の選択肢（先頭は「（なし）」）。</summary>
    public ObservableCollection<OutfitPickViewModel> OutfitPicks { get; }

    /// <summary>このアバターに着せる服（1 つ）。</summary>
    public OutfitPickViewModel SelectedOutfitPick
    {
        get => _selectedOutfitPick;
        set => Set(ref _selectedOutfitPick, value ?? OutfitPickViewModel.None);
    }

    /// <summary>服が 2 つ以上紐づいているアバターを開いたときの注記（保存すると 1 つだけになる）。</summary>
    public string ExtraOutfitsNote
    {
        get => _extraOutfitsNote;
        private set => Set(ref _extraOutfitsNote, value);
    }

    /// <summary>服の一覧の見出しのチェックボックス（全選択）。</summary>
    public bool AllOutfitsChecked
    {
        get => Outfits.Count > 0 && Outfits.All(o => o.IsChecked);
        set
        {
            foreach (var o in Outfits) o.IsChecked = value;
            OnPropertyChanged();
        }
    }

    /// <summary>アバターの一覧の見出しのチェックボックス（全選択）。</summary>
    public bool AllAvatarsChecked
    {
        get => Avatars.Count > 0 && Avatars.All(a => a.IsChecked);
        set
        {
            foreach (var a in Avatars) a.IsChecked = value;
            OnPropertyChanged();
        }
    }

    /// <summary>服・アバターを 2 件以上チェックしているときの注記（そのあいだは保存できず、削除だけできる）。</summary>
    public string OutfitMultiNote => Outfits.Count(o => o.IsChecked) >= 2 ? "2 件以上チェックしているので、編集・保存はできません（削除だけできます）。" : "";

    public string AvatarMultiNote => Avatars.Count(a => a.IsChecked) >= 2 ? "2 件以上チェックしているので、編集・保存はできません（削除だけできます）。" : "";

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveOutfitCommand { get; }
    /// <summary>選んだ服（チェックした服。無ければ一覧で選んでいる服）をまとめて削除する。</summary>
    public AsyncCommand RemoveOutfitCommand { get; }

    /// <summary>服の URL（BOOTH の商品ページ）から、BOOTH Library Manager にある商品の画像・説明・タグ・公開日・更新日を取り込む（サイトの服のページ・一覧に出す）。</summary>

    public RelayCommand NewOutfitCommand { get; }
    public AsyncCommand SaveAvatarCommand { get; }
    public AsyncCommand RemoveAvatarCommand { get; }
    public RelayCommand NewAvatarCommand { get; }

    // ------------------------------------------------------------------ 服

    public OutfitRowViewModel? SelectedOutfit
    {
        get => _selectedOutfit;
        set
        {
            if (!Set(ref _selectedOutfit, value)) return;
            OutfitId = value?.Id ?? "";
            OutfitName = value?.Name ?? "";
            OutfitShop = value?.Shop ?? "";
            OutfitUrl = value?.Url ?? "";
            OutfitNote = value?.Note ?? "";
            OnPropertyChanged(nameof(IsNewOutfit));
            RemoveOutfitCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>一覧で選んでいなければ「追加」、選んでいれば「保存（編集）」。</summary>
    public bool IsNewOutfit => SelectedOutfit is null;

    public string OutfitId { get => _outfitId; set { if (Set(ref _outfitId, value)) SaveOutfitCommand.RaiseCanExecuteChanged(); } }
    public string OutfitName { get => _outfitName; set { if (Set(ref _outfitName, value)) SaveOutfitCommand.RaiseCanExecuteChanged(); } }
    public string OutfitShop { get => _outfitShop; set => Set(ref _outfitShop, value); }
    public string OutfitUrl { get => _outfitUrl; set => Set(ref _outfitUrl, value); }
    public string OutfitNote { get => _outfitNote; set => Set(ref _outfitNote, value); }

    // ------------------------------------------------------------------ アバター

    public AvatarRowViewModel? SelectedAvatar
    {
        get => _selectedAvatar;
        set
        {
            if (!Set(ref _selectedAvatar, value)) return;
            AvatarId = value?.Id ?? "";
            AvatarName = value?.Name ?? "";
            AvatarAliases = value?.AliasesText ?? "";
            AvatarNote = value?.Note ?? "";
            SetChoices(value?.OutfitIds ?? []);
            OnPropertyChanged(nameof(IsNewAvatar));
            RemoveAvatarCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsNewAvatar => SelectedAvatar is null;

    public string AvatarId { get => _avatarId; set { if (Set(ref _avatarId, value)) SaveAvatarCommand.RaiseCanExecuteChanged(); } }

    /// <summary>VRChat 上のアバター名（貼り付け）。サイトには出ない。</summary>
    public string AvatarName { get => _avatarName; set { if (Set(ref _avatarName, value)) SaveAvatarCommand.RaiseCanExecuteChanged(); } }

    /// <summary>表記ゆれ（読点・カンマ・改行で区切る）。</summary>
    public string AvatarAliases { get => _avatarAliases; set => Set(ref _avatarAliases, value); }

    public string AvatarNote { get => _avatarNote; set => Set(ref _avatarNote, value); }

    /// <summary>着せる服の id（選んでいなければ空）。</summary>
    public IReadOnlyList<string> SelectedOutfitIds => SelectedOutfitPick.Id.Length > 0 ? [SelectedOutfitPick.Id] : [];

    /// <summary>マスタを読み直す（何も書かない）。</summary>
    public void Refresh()
    {
        var content = Session.Editor.LoadContent();
        var keepOutfit = SelectedOutfit?.Id;
        var keepAvatar = SelectedAvatar?.Id;

        var boothThumbnails = Session.IsOffline ? new Dictionary<long, string>() : BoothLibrary.LoadThumbnails(Session.Preferences.BoothDatabase is { Length: > 0 } db ? db : null);
        var avatarThumbnails = Session.IsOffline ? AvatarThumbnails.Empty : AvatarThumbnails.Build(Session.AvatarThumbnailFolder, Session.Preferences.AvatarFolder);
        var boothWarnings = BoothLibrary.TakeWarnings();                              // 消せなかった一時フォルダ（購入履歴を含むコピー）は、下で状態の行に出す

        Outfits.Clear();
        foreach (var (id, entry) in content.Outfits.OrderBy(o => o.Key, StringComparer.Ordinal))
            Outfits.Add(new OutfitRowViewModel(id, entry, content.Avatars.Values.Count(a => a.OutfitIds.Contains(id)))
            {
                ThumbnailUrl = BoothLibrary.ItemIdOfUrl(entry.Url) is { } item && boothThumbnails.TryGetValue(item, out var url) ? url : null,
            });
        Avatars.Clear();
        foreach (var (id, entry) in content.Avatars.OrderBy(a => a.Key, StringComparer.Ordinal))
            Avatars.Add(new AvatarRowViewModel(id, entry, content.Outfits) { ThumbnailPath = avatarThumbnails.Find(entry.Name, entry.Aliases) });

        foreach (var o in Outfits) o.PropertyChanged += OnRowChecked;
        foreach (var a in Avatars) a.PropertyChanged += OnRowChecked;
        _selectedOutfit = null;
        _selectedAvatar = null;
        SelectedOutfit = Outfits.FirstOrDefault(o => o.Id == keepOutfit);
        SelectedAvatar = Avatars.FirstOrDefault(a => a.Id == keepAvatar);
        if (SelectedOutfit is null) { OutfitId = OutfitName = OutfitShop = OutfitUrl = OutfitNote = ""; OnPropertyChanged(nameof(IsNewOutfit)); }
        if (SelectedAvatar is null) { AvatarId = AvatarName = AvatarAliases = AvatarNote = ""; SetChoices([]); OnPropertyChanged(nameof(IsNewAvatar)); }
        RemoveOutfitCommand.RaiseCanExecuteChanged();
        RemoveAvatarCommand.RaiseCanExecuteChanged();
        Status = $"服 {Outfits.Count} 件・アバター {Avatars.Count} 件を読みました。" + (boothWarnings.Count > 0 ? " " + string.Join(" ", boothWarnings) : "");
    }

    private void OnRowChecked(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OutfitRowViewModel.IsChecked)) return;
        RemoveOutfitCommand.RaiseCanExecuteChanged();
        RemoveAvatarCommand.RaiseCanExecuteChanged();
        SaveOutfitCommand.RaiseCanExecuteChanged();
        SaveAvatarCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(OutfitMultiNote));
        OnPropertyChanged(nameof(AvatarMultiNote));
        OnPropertyChanged(nameof(AllOutfitsChecked));
        OnPropertyChanged(nameof(AllAvatarsChecked));
    }

    private IReadOnlyList<OutfitRowViewModel> TargetOutfits()
    {
        var checkedRows = Outfits.Where(o => o.IsChecked).ToList();
        return checkedRows.Count > 0 ? checkedRows : SelectedOutfit is null ? [] : [SelectedOutfit];
    }

    private IReadOnlyList<AvatarRowViewModel> TargetAvatars()
    {
        var checkedRows = Avatars.Where(a => a.IsChecked).ToList();
        return checkedRows.Count > 0 ? checkedRows : SelectedAvatar is null ? [] : [SelectedAvatar];
    }

    private void SetChoices(IReadOnlyList<string> linked)
    {
        OutfitPicks.Clear();
        OutfitPicks.Add(OutfitPickViewModel.None);
        foreach (var o in Outfits.OrderBy(o => o.Name, StringComparer.Ordinal)) OutfitPicks.Add(new OutfitPickViewModel(o.Id, o.Name));
        var first = linked.FirstOrDefault(id => Outfits.Any(o => o.Id == id));
        SelectedOutfitPick = OutfitPicks.FirstOrDefault(p => first is not null && p.Id == first) ?? OutfitPickViewModel.None;
        ExtraOutfitsNote = linked.Count > 1 ? $"このアバターには服が {linked.Count} 件紐づいています。保存すると、ここで選んだ 1 つだけになります。" : "";
    }

    private static IReadOnlyList<string> SplitAliases(string text) =>
        text.Split(['、', ',', '，', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>先に検査（書いて戻す）→ 確認 → 書く。検査で落ちたら確認を出さない。</summary>
    private async Task<bool> PreviewConfirmApplyAsync(Action<bool> run, string title, string message, string label)
    {
        run(false);
        if (!await Dialogs.ConfirmAsync(title, message + "\nサイトへの反映は、アプリの「公開」で公開したあとです。", label)) return false;
        run(true);
        Refresh();
        return true;
    }

    private async Task SaveOutfitAsync()
    {
        var editor = Session.Editor;
        var isNew = IsNewOutfit;
        // id は画面に出さず、新しい服には自動で番号を振る（今ある数字の id の最大 + 1）
        var id = isNew && OutfitId.Trim().Length == 0 ? FolderScan.NextId(Outfits.Select(o => o.Id)) : OutfitId.Trim();
        string name = OutfitName.Trim(), shop = OutfitShop.Trim(), url = OutfitUrl.Trim(), note = OutfitNote.Trim();
        var done = await PreviewConfirmApplyAsync(
            apply =>
            {
                if (isNew) AvatarOutfitService.OutfitAdd(editor, apply, id, name, shop, url, note);
                else AvatarOutfitService.OutfitEdit(editor, apply, id, name, shop, url, note);
            },
            isNew ? "服を追加します" : "服を直します",
            $"服 {id}: {name}" + (shop.Length > 0 ? $"（{shop}）" : "") + (url.Length > 0 ? $"\n{url}" : ""),
            isNew ? "追加する" : "保存する");
        if (done) Status = $"服 {id} を{(isNew ? "追加" : "保存")}しました。";
    }

    private static string Names(IEnumerable<string> names, int take = 8)
    {
        var list = names.ToList();
        return string.Join("\n", list.Take(take).Select(n => "・" + n)) + (list.Count > take ? $"\n…ほか {list.Count - take} 件" : "");
    }

    /// <summary>選んだ服（まとめて）を削除する。使われている服が 1 つでもあれば、何も消さずに理由を出す。</summary>
    private async Task RemoveOutfitsAsync()
    {
        var targets = TargetOutfits();
        var editor = Session.Editor;
        var used = targets.Select(t => (Row: t, Users: AvatarOutfitService.OutfitUsers(editor, t.Id))).Where(x => x.Users.Count > 0).ToList();
        if (used.Count > 0)
        {
            Status = $"使われている服があるので、削除できません（{used.Count} 件）: " + string.Join("、", used.Take(3).Select(x => $"{x.Row.Name}（{string.Join(", ", x.Users)}）")) + (used.Count > 3 ? " ほか" : "");
            return;
        }
        var ids = targets.Select(t => t.Id).ToList();
        var done = await PreviewConfirmApplyAsync(
            apply => RemoveAll(editor, apply, OutfitsPathOf(editor), ids, id => AvatarOutfitService.OutfitRemove(editor, true, id)),
            "服を削除します", $"服 {targets.Count} 件をマスタから外します。\n" + Names(targets.Select(t => t.Name)), "削除する");
        if (done) Status = $"服 {targets.Count} 件を削除しました。";
    }

    private async Task SaveAvatarAsync()
    {
        var editor = Session.Editor;
        var isNew = IsNewAvatar;
        var id = isNew && AvatarId.Trim().Length == 0 ? FolderScan.NextId(Avatars.Select(a => a.Id)) : AvatarId.Trim();
        string name = AvatarName.Trim(), note = AvatarNote.Trim();
        var aliases = SplitAliases(AvatarAliases);
        var outfits = SelectedOutfitIds;
        var outfitNames = outfits.Count == 0 ? "（服なし）" : string.Join("、", outfits.Select(o => Outfits.First(x => x.Id == o).Name));
        var done = await PreviewConfirmApplyAsync(
            apply =>
            {
                // 追加（または名前などの編集）と服の紐づけを、どちらも検査してから書く。紐づけの失敗で追加だけが残らないよう、プレビューでは続けて確かめる
                var snapshot = new FileSnapshot();
                snapshot.Capture(Path.Combine(editor.ContentDir, AvatarOutfitService.AvatarsFile));
                try
                {
                    if (isNew) AvatarOutfitService.AvatarAdd(editor, true, id, name, aliases, note);
                    else AvatarOutfitService.AvatarEdit(editor, true, id, name, aliases, note);
                    AvatarOutfitService.AvatarLink(editor, true, id, outfits);
                }
                catch
                {
                    snapshot.Restore();
                    throw;
                }
                if (!apply) snapshot.Restore();
            },
            isNew ? "アバターを追加します" : "アバターを直します",
            $"アバター {id}: {name}（サイトには出ません）\n着せている服: {outfitNames}",
            isNew ? "追加する" : "保存する");
        if (done) Status = $"アバター {id} を{(isNew ? "追加" : "保存")}しました。";
    }

    /// <summary>チェックした prefab 名を、アバターとしてまとめて登録する（検査 → 1 回の確認 → 書く。途中で落ちたら全部戻す）。</summary>
    /// <summary>アバター名の候補。登録済みの服から、着ている服を予測する（OutfitGuess）。</summary>
    private ImportEntry AvatarEntry(string name)
    {
        var guess = OutfitGuess.Suggest(name, Outfits.Select(o => (o.Id, o.Name)).ToList(), max: 1);   // 着る服は 1 つ
        return new ImportEntry(name, "", null, null, guess.Select(g => g.Id).ToList(), string.Join("、", guess.Select(g => g.Name)));
    }

    /// <summary>登録済みのアバターに、アバター名から予測した服を着せる（すでに服があるものは、置き換えを選んだときだけ）。</summary>
    private async Task<bool> ApplySuggestionsToRegisteredAsync(IReadOnlyList<ImportEntry> entries)
    {
        var editor = Session.Editor;
        var overwrite = AvatarImport.LinkOverwrite;
        var plan = new List<(AvatarRowViewModel Avatar, string OutfitId, string OutfitName)>();
        var skipped = 0;
        foreach (var entry in entries)
        {
            var avatar = Avatars.FirstOrDefault(a => a.Name == entry.Name || a.Aliases.Contains(entry.Name));
            if (avatar is null || entry.SuggestedOutfitIds is not { Count: > 0 } ids || (avatar.OutfitIds.Count > 0 && !overwrite))
            {
                skipped++;
                continue;
            }
            plan.Add((avatar, ids[0], entry.Suggestion));
        }
        if (plan.Count == 0)
        {
            AvatarImport.Say($"服を着せられるアバターがありません（予測が無い・すでに服がある: {skipped} 件。すでに服があるものを置き換えるには、「すでに服があるものも置き換える」にチェックを入れてください）");
            return false;
        }
        var ids2 = plan.Select(p => p.Avatar.Id).ToList();
        var done = await PreviewConfirmApplyAsync(
            apply => AddAll(editor, apply, AvatarsPathOf(editor), ids2, (i, id) => AvatarOutfitService.AvatarLink(editor, true, id, [plan[i].OutfitId])),
            "予測した服を着せます",
            $"登録済みのアバター {plan.Count} 件に、予測した服を着せます。" + (skipped > 0 ? $"（対象外 {skipped} 件）" : "") + "\n" + Names(plan.Select(p => p.Avatar.Name + " → " + p.OutfitName)),
            "着せる");
        if (done) AvatarImport.Say($"アバター {plan.Count} 件に、予測した服を着せました。");
        return done;
    }

    private async Task<bool> ImportAvatarsAsync(IReadOnlyList<ImportEntry> entries)
    {
        var names = entries.Select(e => e.Name).ToList();
        var link = AvatarImport.LinkSuggested;
        var editor = Session.Editor;
        var firstId = int.Parse(FolderScan.NextId(Avatars.Select(a => a.Id)));
        var done = await PreviewConfirmApplyAsync(
            apply => AddAll(editor, apply, AvatarsPathOf(editor), names, (i, name) =>
            {
                var id = (firstId + i).ToString();
                AvatarOutfitService.AvatarAdd(editor, true, id, name, [], null);
                if (link && entries[i].SuggestedOutfitIds is { Count: > 0 } outfitIds) AvatarOutfitService.AvatarLink(editor, true, id, outfitIds);
            }),
            "アバターをまとめて登録します",
            $"フォルダの prefab から、アバター {names.Count} 件を登録します（サイトには出ません）。" + (link && entries.Any(e => e.SuggestedOutfitIds is { Count: > 0 }) ? $"\n予測した服も着せます（{entries.Count(e => e.SuggestedOutfitIds is { Count: > 0 })} 件）。" : "") + "\n" + string.Join("\n", entries.Take(15).Select(e => "・" + e.Name + (link && e.Suggestion.Length > 0 ? " → " + e.Suggestion : ""))) + (names.Count > 15 ? $"\n…ほか {names.Count - 15} 件" : ""),
            "登録する");
        if (done) Status = $"アバター {names.Count} 件を登録しました。";
        return done;
    }

    /// <summary>BOOTH Library Manager のデータ（商品 ID → 商品の情報）。読めなければ空。デモでは読まない。</summary>
    private IReadOnlyDictionary<long, AvatarOutfitService.BoothInfo> LoadBoothDetails() =>
        Session.IsOffline ? new Dictionary<long, AvatarOutfitService.BoothInfo>() : _boothDetails();

    /// <summary>
    /// チェックした名前を、服としてまとめて登録する。お店と商品ページの URL は BOOTH の情報から入る。
    /// BOOTH Library Manager のデータが読めれば、<b>商品の画像・説明・タグ・公開日・更新日も、あわせて取り込む</b>（成人向けの商品は取り込まない）。
    /// </summary>
    private async Task<bool> ImportOutfitsAsync(IReadOnlyList<ImportEntry> entries)
    {
        var names = entries.Select(e => e.Name).ToList();
        var editor = Session.Editor;
        var firstId = int.Parse(FolderScan.NextId(Outfits.Select(o => o.Id)));
        var newIds = names.Select((_, i) => (firstId + i).ToString()).ToHashSet(StringComparer.Ordinal);
        var details = LoadBoothDetails();
        AvatarOutfitService.BoothFillResult? filled = null;
        var done = await PreviewConfirmApplyAsync(
            apply =>
            {
                // 登録と、画像・情報の取り込みを、1 つの操作として書く（落ちるか、プレビューなら、全部元に戻す）
                var file = OutfitsPathOf(editor);
                var snapshot = new FileSnapshot();
                snapshot.Capture(file);
                try
                {
                    for (var i = 0; i < names.Count; i++)
                        AvatarOutfitService.OutfitAdd(editor, true, (firstId + i).ToString(), names[i], entries[i].Shop, entries[i].Url, null);
                    if (details.Count > 0)
                        filled = AvatarOutfitService.OutfitFillFromBooth(editor, true, id => details.GetValueOrDefault(id), DateTimeOffset.UtcNow, overwrite: false, onlyIds: newIds);
                }
                catch
                {
                    snapshot.Restore();
                    throw;
                }
                if (!apply) snapshot.Restore();
            },
            "服をまとめて登録します",
            $"フォルダの一覧から、服 {names.Count} 件を登録します。お店と商品ページの URL は、BOOTH の情報から自動で入ります（情報が無いときは、登録後に服を選んで入れられます）。\n" + string.Join("\n", names.Take(15).Select(n => "・" + n)) + (names.Count > 15 ? $"\n…ほか {names.Count - 15} 件" : "")
            + (details.Count > 0
                ? "\nBOOTH Library Manager にある商品の画像・説明・タグ・公開日・更新日も、あわせて取り込みます（成人向けの商品は取り込みません）。"
                : "\n（BOOTH Library Manager のデータを読めないので、画像・情報は取り込みません。あとで、同じウィンドウで登録済みの服を選んで「インポート」すると取り込めます）"),
            "登録する");
        if (done) OutfitImport.Say($"服 {names.Count} 件を登録しました" + (filled is { } f ? $"（BOOTH の画像・情報を {f.Updated} 件に取り込みました）" : "") + "。");
        return done;
    }

    /// <summary>
    /// 登録済みの服に、BOOTH の画像・説明・タグ・公開日・更新日を取り込む（「フォルダから服を登録」ウィンドウの「インポート」）。
    /// <b>差分</b>（既定）は、服にまだ無い項目だけを足す。<b>上書き</b>（「すでに画像・情報があるものも上書きする」）は、BOOTH の今の内容にそろえる（再インポート）。
    /// 服の URL（BOOTH の商品ページ）から商品を探す。成人向けの商品は取り込まない。
    /// </summary>
    private async Task<bool> ReimportOutfitsAsync(IReadOnlyList<ImportEntry> entries)
    {
        var details = LoadBoothDetails();
        if (details.Count == 0)
        {
            var text = Session.IsOffline ? "デモでは、BOOTH から取り込みません。" : "BOOTH Library Manager のデータを読めませんでした（インストールされているか、データの場所を確かめてください）。";
            OutfitImport.Say(text);
            return false;
        }
        var ids = entries
            .Select(e => Outfits.FirstOrDefault(o => o.Name == e.Name || (e.Url is not null && o.Url == e.Url))?.Id)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var overwrite = OutfitImport.LinkOverwrite;
        var editor = Session.Editor;
        var now = DateTimeOffset.UtcNow;
        AvatarOutfitService.BoothFillResult? result = null;
        var done = await PreviewConfirmApplyAsync(
            apply => result = AvatarOutfitService.OutfitFillFromBooth(editor, apply, id => details.GetValueOrDefault(id), now, overwrite, ids),
            overwrite ? "BOOTH の画像・情報を上書きして取り込みます（再インポート）" : "BOOTH の画像・情報を足します（差分インポート）",
            $"登録済みの服 {ids.Count} 件に、BOOTH Library Manager にある商品の画像・説明・タグ・公開日・更新日を取り込みます（サイトの服のページ・一覧に出ます）。\n"
            + (overwrite
                ? "すでにある画像・説明・タグ・日付も、BOOTH の今の内容に置き換えます（手で直した説明も置き換わります）。"
                : "服にまだ無い項目だけを足します（すでにある画像・説明・タグ・日付は変えません）。")
            + "\n成人向けの商品は、取り込みません。名前・お店・URL・備考は変えません。",
            overwrite ? "上書きして取り込む" : "足す");
        if (!done) return false;
        var r = result!;
        OutfitImport.Say($"BOOTH の画像・情報を {r.Updated} 件の服に{(overwrite ? "上書き" : "足し")}ました（変更なし {r.AlreadyFilled}・URL なし {r.NoBoothUrl}・BOOTH に見つからない {r.NotInLibrary}・成人向け {r.Adult}）。");
        return true;
    }

    private static string AvatarsPathOf(ContentEditor editor) => Path.Combine(editor.ContentDir, AvatarOutfitService.AvatarsFile);

    private static string OutfitsPathOf(ContentEditor editor) => Path.Combine(editor.ContentDir, AvatarOutfitService.OutfitsFile);

    /// <summary>複数件を 1 回で書く。ファイルを控えておき、途中で落ちるか、プレビュー（apply なし）なら元に戻す。</summary>
    private static void AddAll(ContentEditor editor, bool apply, string file, IReadOnlyList<string> names, Action<int, string> add)
    {
        var snapshot = new FileSnapshot();
        snapshot.Capture(file);
        try
        {
            for (var i = 0; i < names.Count; i++) add(i, names[i]);
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
        if (!apply) snapshot.Restore();
    }

    /// <summary>選んだアバター（まとめて）を削除する。写真で使われているものがあれば、何も消さずに理由を出す。</summary>
    private async Task RemoveAvatarsAsync()
    {
        var targets = TargetAvatars();
        var editor = Session.Editor;
        var ids = targets.Select(t => t.Id).ToList();
        try
        {
            var done = await PreviewConfirmApplyAsync(
                apply => RemoveAll(editor, apply, AvatarsPathOf(editor), ids, id => AvatarOutfitService.AvatarRemove(editor, true, id)),
                "アバターを削除します", $"アバター {targets.Count} 件をマスタから外します。\n" + Names(targets.Select(t => t.Name)), "削除する");
            if (done) Status = $"アバター {targets.Count} 件を削除しました。";
        }
        catch (InvalidOperationException e)
        {
            Status = "削除できません: " + UserText.Plain(e.Message);
        }
    }

    /// <summary>複数件を 1 回で消す。ファイルを控えておき、途中で落ちるか、プレビュー（apply なし）なら元に戻す。</summary>
    private static void RemoveAll(ContentEditor editor, bool apply, string file, IReadOnlyList<string> ids, Action<string> remove)
    {
        var snapshot = new FileSnapshot();
        snapshot.Capture(file);
        try
        {
            foreach (var id in ids) remove(id);
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
        if (!apply) snapshot.Restore();
    }

}
