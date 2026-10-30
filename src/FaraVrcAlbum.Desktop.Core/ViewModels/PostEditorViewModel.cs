using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>プレビューの文章の区切り（段落）。</summary>
public sealed record PreviewTextBlock(string Text, string Align = "");

/// <summary>プレビューの写真。画像・説明と、自動で表示される内容（撮影日時・ワールド・服。服は紐づけがあるときだけ）。</summary>
public sealed record PreviewPhotoBlock(string? PreviewUrl, string Caption, string AutoText)
{
    public bool HasCaption => Caption.Length > 0;
}

/// <summary>日付の選択肢。その日の記事が作成済みかどうか（と公開か非公開か）・その日にアップロードした写真の枚数を出す。</summary>
public sealed record DateOptionViewModel(string Label, string Date, PostOptionViewModel? Post, int PhotoCount)
{
    public bool Exists => Post is not null;

    public string StateText => Post is null ? "未作成" : Post.Draft ? "作成済み（非公開）" : "作成済み（公開）";

    public string Text => $"{Label}　{StateText}　写真 {(PhotoCount > 0 ? PhotoCount : Post?.PhotoKeys.Count ?? 0)} 枚";

    public override string ToString() => Text;
}

/// <summary>記事の写真 1 枚のアバター（写真ごとに選ぶ。服はアバターの既定の服になる）。</summary>
public sealed class PhotoAvatarRowViewModel(string key, string takenText, string worldText, string originalAvatarId, AvatarOptionViewModel avatar, string? previewUrl) : ObservableObject
{
    private AvatarOptionViewModel _avatar = avatar;

    public string Key { get; } = key;

    public string TakenText { get; } = takenText;

    public string WorldText { get; } = worldText;

    public string? PreviewUrl { get; } = previewUrl;

    /// <summary>保存してあるアバターの id（変えたかどうかの判定に使う）。</summary>
    public string OriginalAvatarId { get; } = originalAvatarId;

    public AvatarOptionViewModel Avatar
    {
        get => _avatar;
        set => Set(ref _avatar, value ?? AvatarOptionViewModel.None);
    }
}

/// <summary>
/// 記事の作成と編集（日付で選ぶ）。上で日付を選ぶと、その日の記事（slug は日付: 2026-10-24）を編集できる。まだ無い日は、その日にアップロードした写真が
/// 入った新しい記事（下書き）の編集になり、保存すると作られる。作成済みかどうかは日付の選択肢に出る。
/// WordPress の本文のように、1 つの入力欄に文章と写真の行 ![説明](URL) を並べて書く。右に、サイトでの見た目に近いプレビューを出す。
/// 写真の画像・撮影日時・ワールド名・服の名前はサイトに自動で表示される（プレビューにも出す）。服は紐づけがあるときだけ出す。
/// 題・公開日時（日本時間）・非公開かどうか・タグ（自由に入力。読点・カンマで区切る。新しいタグは登録も行う）・写真ごとのアバターも編集できる。
/// 表紙と SNS 共有用の画像は、本文の最初の写真に自動で合わせる。保存は、先に検査 → 確認 → 書く。
/// </summary>
public sealed partial class PostEditorViewModel : PageViewModel
{
    private PostOptionViewModel? _selectedPost;
    private DateOptionViewModel? _selectedDate;
    private bool _isNewArticle;
    private string _newSlug = "";
    private IReadOnlyList<LedgerEntry> _newEntries = [];
    private int _avatarsFromPhotos;
    private IReadOnlyDictionary<string, FaraVrcAlbum.Models.AvatarMasterEntry> _avatarMasters = new Dictionary<string, FaraVrcAlbum.Models.AvatarMasterEntry>();
    private IReadOnlyDictionary<string, FaraVrcAlbum.Models.OutfitMasterEntry> _outfitMasters = new Dictionary<string, FaraVrcAlbum.Models.OutfitMasterEntry>();
    private string _title = "";
    private string _publishedAt = "";
    private bool _isDraft;
    private string _tagsText = "";
    private string _document = "";
    private string _documentError = "";
    private bool _isDirty;
    private bool _loading;
    private bool _syncingSelection;
    private IReadOnlyList<string> _postKeys = [];
    private string _storedPublishedAt = "";
    private Dictionary<string, PhotoEditInfo> _photoInfo = new(StringComparer.Ordinal);
    private AvatarOptionViewModel _defaultAvatar = AvatarOptionViewModel.None;
    private PhotoAvatarRowViewModel? _selectedAvatarRow;

    public PostEditorViewModel(AppState app, IDialogService dialogs) : base(app, dialogs)
    {
        Posts = [];
        Dates = [];
        PreviewBlocks = [];
        AvatarOptions = [AvatarOptionViewModel.None];
        PhotoAvatars = [];
        RefreshCommand = Command(_ => { Refresh(); return Task.CompletedTask; });
        SaveCommand = Command(_ => SaveAsync(), () => HasPost && (IsDirty || IsNewArticle) && DocumentError.Length == 0);
        RevertCommand = new RelayCommand(Reload, () => HasPost && IsDirty);
        PublishCommand = Command(_ => SetPublishedAsync(publish: true), () => HasPost && !IsNewArticle && !IsDirty && SelectedPost is { Draft: true });
        UnpublishCommand = Command(_ => SetPublishedAsync(publish: false), () => HasPost && !IsNewArticle && !IsDirty && SelectedPost is { Draft: false });
        DeletePostCommand = Command(_ => DeletePostAsync(), () => HasPost && !IsNewArticle);
        PublishNowCommand = new RelayCommand(() => PublishedAt = JstDate.Display(DateTimeOffset.UtcNow), () => HasPost);
        ApplyDefaultToAllCommand = new RelayCommand(() => SetAllAvatars(DefaultAvatar), () => HasPost && PhotoAvatars.Count > 0);
        ApplySelectedToAllCommand = new RelayCommand(() => SetAllAvatars(SelectedAvatarRow!.Avatar), () => SelectedAvatarRow is not null);
    }

    /// <summary>作成済みの記事（下書きを含む）。</summary>
    public ObservableCollection<PostOptionViewModel> Posts { get; }

    /// <summary>日付の選択肢（新しい日付が上。作成済みか未作成かが分かる）。</summary>
    public ObservableCollection<DateOptionViewModel> Dates { get; }

    /// <summary>右のプレビュー（文章の段落と写真）。</summary>
    public ObservableCollection<object> PreviewBlocks { get; }

    /// <summary>アバターの選択肢（先頭は「（なし）」）。</summary>
    public ObservableCollection<AvatarOptionViewModel> AvatarOptions { get; }

    /// <summary>この記事の写真ごとのアバター。</summary>
    public ObservableCollection<PhotoAvatarRowViewModel> PhotoAvatars { get; }

    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public RelayCommand RevertCommand { get; }

    /// <summary>非公開の記事を公開にする（本文などは変えない）。本番のサイトには、アプリの「公開」で本番へ公開したあとに出る。保存していない変更があるときは押せない。</summary>
    public AsyncCommand PublishCommand { get; }

    /// <summary>公開している記事を非公開にする（サイトに出なくなる）。保存していない変更があるときは押せない。</summary>
    public AsyncCommand UnpublishCommand { get; }

    /// <summary>記事を削除する（記事のファイルを消す。消す前の内容は local/deleted-posts に残す。写真は消さない）。</summary>
    public AsyncCommand DeletePostCommand { get; }

    /// <summary>公開日時を今にする。</summary>
    public RelayCommand PublishNowCommand { get; }

    /// <summary>「いつものアバター」を全部の写真に入れる。</summary>
    public RelayCommand ApplyDefaultToAllCommand { get; }

    /// <summary>選んでいる写真のアバターを、全部の写真に入れる。</summary>
    public RelayCommand ApplySelectedToAllCommand { get; }

    /// <summary>選んでいる日付。変えると、その日の記事（無ければ新しい記事）を開く。</summary>
    public DateOptionViewModel? SelectedDate
    {
        get => _selectedDate;
        set
        {
            if (!Set(ref _selectedDate, value) || _syncingSelection) return;
            _syncingSelection = true;
            try
            {
                if (value?.Post is { } post)
                {
                    _selectedPost = post;
                    OnPropertyChanged(nameof(SelectedPost));
                    Load(post);
                }
                else
                {
                    _selectedPost = null;
                    OnPropertyChanged(nameof(SelectedPost));
                    LoadNew(value);
                }
            }
            finally
            {
                _syncingSelection = false;
            }
        }
    }

    /// <summary>今開いている作成済みの記事（未作成の日付なら null）。記事を直接選ぶと、その日付が選ばれる。</summary>
    public PostOptionViewModel? SelectedPost
    {
        get => _selectedPost;
        set
        {
            if (_syncingSelection)
            {
                Set(ref _selectedPost, value);
                return;
            }
            SelectedDate = value is null ? null : Dates.FirstOrDefault(d => d.Post?.Slug == value.Slug);
        }
    }

    /// <summary>まだ作っていない日付の記事を開いているとき true（保存すると作られる）。</summary>
    public bool IsNewArticle
    {
        get => _isNewArticle;
        private set
        {
            if (Set(ref _isNewArticle, value)) OnPropertyChanged(nameof(HasPost));
        }
    }

    public bool HasPost => SelectedPost is not null || IsNewArticle;

    public string Title { get => _title; set { if (SetField(ref _title, value)) OnPropertyChanged(nameof(PreviewTitle)); } }

    /// <summary>公開日時（日本時間。例: 2026-10-24 20:00。保存するときに +09:00 を付ける）。</summary>
    public string PublishedAt { get => _publishedAt; set { if (SetField(ref _publishedAt, value)) OnPropertyChanged(nameof(PreviewMeta)); } }

    /// <summary>非公開（draft。サイトに出さない）。外すと公開する記事になる。</summary>
    public bool IsDraft { get => _isDraft; set { if (SetField(ref _isDraft, value)) OnPropertyChanged(nameof(PreviewMeta)); } }

    /// <summary>タグ（自由に入力。読点・カンマで区切る）。</summary>
    public string TagsText { get => _tagsText; set { if (SetField(ref _tagsText, value)) OnPropertyChanged(nameof(PreviewMeta)); } }

    /// <summary>本文（文章と写真の行）。変えるとプレビューを作り直す。</summary>
    public string Document
    {
        get => _document;
        set
        {
            if (SetField(ref _document, value)) UpdatePreview();
        }
    }

    /// <summary>本文の問題（写真の行が足りない・ほかの記事の写真など）。空なら保存できる。</summary>
    public string DocumentError
    {
        get => _documentError;
        private set
        {
            if (Set(ref _documentError, value)) SaveCommand.RaiseCanExecuteChanged();
        }
    }

    public string PreviewTitle => Title.Trim().Length > 0 ? Title.Trim() : "（題なし）";

    /// <summary>プレビューの題の下（公開日・タグ・非公開かどうか）。</summary>
    public string PreviewMeta
    {
        get
        {
            var parts = new List<string>();
            if (JstDate.TryParse(PublishedAt, out var at))
                parts.Add(at.ToString("yyyy年M月d日", CultureInfo.InvariantCulture));
            var tags = SplitTags(TagsText);
            if (tags.Count > 0) parts.Add("タグ: " + string.Join("、", tags));
            if (IsDraft) parts.Add("非公開（サイトには出ません）");
            return string.Join("　・　", parts);
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!Set(ref _isDirty, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
            RevertCommand.RaiseCanExecuteChanged();
            PublishCommand.RaiseCanExecuteChanged();
            UnpublishCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>「いつものアバター」。新しい記事の写真に最初から入る。手元（local/desktop.json）に覚える。</summary>
    public AvatarOptionViewModel DefaultAvatar
    {
        get => _defaultAvatar;
        set
        {
            if (!Set(ref _defaultAvatar, value ?? AvatarOptionViewModel.None)) return;
            Session.Preferences.DefaultAvatar = _defaultAvatar.Id;
        }
    }

    /// <summary>アバターの一覧で選んでいる写真（右のプレビューに出す）。</summary>
    public PhotoAvatarRowViewModel? SelectedAvatarRow
    {
        get => _selectedAvatarRow;
        set
        {
            if (!Set(ref _selectedAvatarRow, value)) return;
            OnPropertyChanged(nameof(AvatarPreviewUrl));
            ApplySelectedToAllCommand.RaiseCanExecuteChanged();
        }
    }

    public string? AvatarPreviewUrl => SelectedAvatarRow?.PreviewUrl;

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (!Set(ref field, value, name)) return false;
        if (!_loading) IsDirty = true;
        return true;
    }

    public static IReadOnlyList<string> SplitTags(string text) =>
        text.Split(['、', ',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2})")]
    private static partial Regex SlugDate();

    /// <summary>記事の日付（slug が日付で始まればそれ。そうでなければ公開日時の日本時間の日付）。</summary>
    private static string DateOfPost(string slug, string publishedAt)
    {
        if (SlugDate().Match(slug) is { Success: true } m) return m.Groups["date"].Value;
        return JstDate.TryParse(publishedAt, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : slug;
    }

    private static string? DateOfTakenAt(string takenAt) =>
        JstDate.TryParse(takenAt, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    /// <summary>記事の一覧と日付の一覧を読み直す（下書きを含む）。開いていた記事（日付）は、なるべくそのまま開く。</summary>
    public void Refresh(string? selectSlug = null)
    {
        var keepSlug = selectSlug ?? SelectedPost?.Slug;
        var keepDate = IsNewArticle ? _newSlug : null;
        var summaries = PostService.List(Session.Editor).Where(p => p.Problem is null).ToList();
        Posts.Clear();
        foreach (var post in summaries) Posts.Add(new PostOptionViewModel(post));
        LoadAvatarOptions();

        // 日付の一覧: 記事のある日付と、アップロード済みの写真がある日付（新しい日付が上）
        var ledger = Session.LoadProductionLedger();
        var photosByDate = ledger.BySha.Values.Where(e => e.State == LedgerStates.Uploaded)
            .Select(e => DateOfTakenAt(e.TakenAt))
            .Where(d => d is not null)
            .GroupBy(d => d!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var options = new List<DateOptionViewModel>();
        var usedDates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var post in Posts.OrderBy(p => p.Slug, StringComparer.Ordinal))
        {
            var date = DateOfPost(post.Slug, summaries.First(s => s.Slug == post.Slug).PublishedAt);
            var label = usedDates.Add(date) ? date : $"{date}（{post.Slug}）";            // 同じ日付の記事が複数あるときは、記事名を添える
            options.Add(new DateOptionViewModel(label, date, post, photosByDate.GetValueOrDefault(date)));
        }
        foreach (var (date, count) in photosByDate)
            if (usedDates.Add(date)) options.Add(new DateOptionViewModel(date, date, null, count));

        _syncingSelection = true;
        try
        {
            Dates.Clear();
            foreach (var option in options.OrderByDescending(o => o.Date, StringComparer.Ordinal).ThenBy(o => o.Label, StringComparer.Ordinal)) Dates.Add(option);
            _selectedDate = null;
            _selectedPost = null;
            _isNewArticle = false;
        }
        finally
        {
            _syncingSelection = false;
        }
        OnPropertyChanged(nameof(SelectedDate));
        SelectedDate = Dates.FirstOrDefault(d => keepSlug is not null && d.Post?.Slug == keepSlug)
                       ?? Dates.FirstOrDefault(d => keepDate is not null && d.Date == keepDate)
                       ?? Dates.FirstOrDefault(d => d.Exists)
                       ?? Dates.FirstOrDefault();
        if (!IsNewArticle)
        {
            var created = Dates.Count(d => d.Exists);
            Status = $"日付 {Dates.Count} 件（作成済み {created}・未作成 {Dates.Count - created}）を読みました。";
        }
    }

    private void LoadAvatarOptions()
    {
        var content = Session.Editor.LoadContent();
        var masters = content.Avatars;
        _avatarMasters = masters;
        _outfitMasters = content.Outfits;
        AvatarOptions.Clear();
        AvatarOptions.Add(AvatarOptionViewModel.None);
        foreach (var a in masters.OrderBy(a => a.Value.Name, StringComparer.Ordinal)) AvatarOptions.Add(new AvatarOptionViewModel(a.Key, a.Value.Name));
        var savedDefault = Session.Preferences.DefaultAvatar;
        _defaultAvatar = AvatarOptions.FirstOrDefault(a => a.Id == savedDefault) ?? AvatarOptionViewModel.None;
        OnPropertyChanged(nameof(DefaultAvatar));
    }

    private void Reload()
    {
        if (SelectedPost is not null) Load(SelectedPost);
        else LoadNew(SelectedDate);
    }

    private void BeginLoad()
    {
        _loading = true;
        OnPropertyChanged(nameof(HasPost));
    }

    private void EndLoad()
    {
        _loading = false;
        IsDirty = false;
        UpdatePreview();
        PublishNowCommand.RaiseCanExecuteChanged();
        ApplyDefaultToAllCommand.RaiseCanExecuteChanged();
        SaveCommand.RaiseCanExecuteChanged();
        RevertCommand.RaiseCanExecuteChanged();
        PublishCommand.RaiseCanExecuteChanged();
        UnpublishCommand.RaiseCanExecuteChanged();
        DeletePostCommand.RaiseCanExecuteChanged();
    }

    private void SetAvatarRows(IEnumerable<PhotoAvatarRowViewModel> rows)
    {
        foreach (var row in PhotoAvatars) row.PropertyChanged -= OnAvatarRowChanged;
        PhotoAvatars.Clear();
        foreach (var row in rows)
        {
            row.PropertyChanged += OnAvatarRowChanged;
            PhotoAvatars.Add(row);
        }
        SelectedAvatarRow = null;
    }

    private void OnAvatarRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhotoAvatarRowViewModel.Avatar) && !_loading) IsDirty = true;
    }

    private void SetAllAvatars(AvatarOptionViewModel avatar)
    {
        foreach (var row in PhotoAvatars) row.Avatar = avatar;
        Status = $"全部の写真（{PhotoAvatars.Count} 枚）のアバターを {avatar.Text} にしました（保存すると反映されます）。";
    }

    /// <summary>作成済みの記事を開く。</summary>
    private void Load(PostOptionViewModel? option)
    {
        BeginLoad();
        try
        {
            IsNewArticle = false;
            if (option is null)
            {
                _postKeys = [];
                _photoInfo = new(StringComparer.Ordinal);
                _storedPublishedAt = "";
                Title = PublishedAt = TagsText = Document = "";
                IsDraft = false;
                SetAvatarRows([]);
                return;
            }
            var model = PostEditService.Load(Session.Editor, option.Slug);
            var tagNames = PostEditService.Tags(Session.Editor).ToDictionary(t => t.Slug, t => t.Name, StringComparer.Ordinal);
            _postKeys = model.Photos.Select(p => p.Key).ToList();
            _photoInfo = model.Photos.ToDictionary(p => p.Key, StringComparer.Ordinal);
            _storedPublishedAt = model.PublishedAt;
            Title = model.Title;
            PublishedAt = JstDate.DisplayOf(model.PublishedAt);
            IsDraft = model.Draft;
            TagsText = string.Join("、", model.Tags.Select(t => tagNames.GetValueOrDefault(t, t)));
            SetAvatarRows(model.Photos.Select(p => new PhotoAvatarRowViewModel(
                p.Key, JstDate.DisplayOf(p.TakenAt ?? ""), p.WorldName is { Length: > 0 } w ? w : "（ワールドなし）", p.Avatar,
                AvatarOptions.FirstOrDefault(a => p.Avatar.Length > 0 && a.Id == p.Avatar) ?? AvatarOptionViewModel.None, Session.PhotoPreviewUrl(p.Key))));
            Document = ArticleDocument.Compose(model with { Photos = model.Photos.Select(p => p with { Wear = WearOf(p) }).ToList() }, Session.Repo.ImageBaseUrl);   // 写真の下に、日付・服・ワールドの行が入る
        }
        finally
        {
            EndLoad();
        }
    }

    /// <summary>まだ作っていない日付の記事を開く: その日にアップロードした写真が入った、新しい下書き。保存すると作られる。</summary>
    private void LoadNew(DateOptionViewModel? date)
    {
        BeginLoad();
        try
        {
            if (date is null)
            {
                IsNewArticle = false;
                _postKeys = [];
                _photoInfo = new(StringComparer.Ordinal);
                _storedPublishedAt = "";
                _newSlug = "";
                Title = PublishedAt = TagsText = Document = "";
                IsDraft = false;
                SetAvatarRows([]);
                return;
            }
            var ledger = Session.LoadProductionLedger();
            _newEntries = ledger.BySha.Values.Where(e => e.State == LedgerStates.Uploaded && DateOfTakenAt(e.TakenAt) == date.Date)
                .OrderBy(e => e.TakenAt, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
            _newSlug = date.Date;
            _postKeys = _newEntries.Select(e => e.Key).ToList();
            var photos = _newEntries.Select(e => new PhotoEditInfo(e.Key, e.TakenAt, e.World?.Name, [], "", "")).ToList();
            _photoInfo = photos.ToDictionary(p => p.Key, StringComparer.Ordinal);
            _storedPublishedAt = "";
            IsNewArticle = true;
            Title = DefaultTitle(date.Date);                                            // 題の既定は、日付（2026/10/24）
            PublishedAt = _newEntries.Count > 0 ? JstDate.DisplayOf(_newEntries[0].TakenAt) : date.Date + " 00:00";
            IsDraft = true;
            TagsText = "";
            // アバターは、台帳の控え（取り込み時の XMP）→ 元の写真の XMP → 履歴ファイル → 既定のアバターの順に決める
            var fromPhotos = 0;
            var avatars = Session.Editor.LoadContent().Avatars;
            var history = PhotoServerHistory.Load(Session.PhotoServerHistoryPath);
            AvatarOptionViewModel AvatarOf(LedgerEntry e)
            {
                var name = e.AvatarName;
                if (string.IsNullOrWhiteSpace(name)) name = SourceAvatar.Read(Session.SourceRoot, e.SourceName)?.Name;     // 台帳に控えが無い写真は、元の写真の XMP を読み直す（旧・新どちらの形式も）
                if (string.IsNullOrWhiteSpace(name) && DateTimeOffset.TryParse(e.TakenAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var taken))
                    name = history.AvatarAt(taken)?.Name;
                var id = AvatarMatcher.Match(name, avatars);
                if (id is not null && AvatarOptions.FirstOrDefault(a => a.Id == id) is { } option)
                {
                    fromPhotos++;
                    return option;
                }
                return DefaultAvatar;
            }
            SetAvatarRows(_newEntries.Select(e => new PhotoAvatarRowViewModel(
                e.Key, JstDate.DisplayOf(e.TakenAt), e.World?.Name is { Length: > 0 } w ? w : "（ワールドなし）", "", AvatarOf(e), Session.PhotoPreviewUrl(e.Key))));
            _avatarsFromPhotos = fromPhotos;
            Document = ArticleDocument.Compose(new PostEditModel(_newSlug, "", PublishedAt, true, [], "", "", photos.Select(p => p with { Wear = WearOf(p) }).ToList()), Session.Repo.ImageBaseUrl);   // 写真の下に、日付・服・ワールドの行が入る
        }
        finally
        {
            EndLoad();
        }
        if (IsNewArticle) Status = $"{date!.Date} の記事はまだ作っていません。題には日付が入っています（直せます）。「保存する」を押すと、この日の写真 {_newEntries.Count} 枚で記事（下書き）を作ります。{(_avatarsFromPhotos > 0 ? $" アバターは、写真の記録から {_avatarsFromPhotos} 枚に自動で入れました。" : "")}";
    }

    /// <summary>新しい記事の題の既定値（日付。例: 2026/10/24）。</summary>
    public static string DefaultTitle(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)
            : date;

    /// <summary>右のプレビューと、本文の問題を作り直す。</summary>
    private void UpdatePreview()
    {
        if (_loading) return;
        var baseUrl = Session.Repo.ImageBaseUrl;
        PreviewBlocks.Clear();
        var align = "";
        foreach (var (isPhoto, textOrKey, caption, auto) in ArticleDocument.Blocks(Document, baseUrl))
        {
            if (!isPhoto)
            {
                align = AddTextBlock(textOrKey, align);
                continue;
            }
            var info = _photoInfo.GetValueOrDefault(textOrKey);
            PreviewBlocks.Add(new PreviewPhotoBlock(Session.PhotoPreviewUrl(info?.Key), caption, info is null ? "（この記事の写真ではありません）" : auto));
        }
        var parts = ArticleDocument.Parse(Document, _postKeys, baseUrl);
        DocumentError = string.Join("\n", parts.Errors);
    }

    /// <summary>文章の段落をプレビューに足す。:::center などの囲みの行は、揃えとして使い、文字としては出さない。今の揃えを返す。</summary>
    private string AddTextBlock(string paragraph, string align)
    {
        var kept = new List<string>();
        var closeAfter = false;
        foreach (var raw in paragraph.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line is ":::left" or ":::center" or ":::right") { align = line[3..]; continue; }
            if (line == ":::") { closeAfter = true; continue; }
            kept.Add(line);
        }
        if (kept.Any(l => l.Trim().Length > 0)) PreviewBlocks.Add(new PreviewTextBlock(string.Join("\n", kept), align));
        return closeAfter ? "" : align;
    }

    /// <summary>写真の服の名前。保存してある服があればそれ、無ければ、その写真のアバターに着せている服（アバターのマスタ）。どちらも無ければ空。</summary>
    private IReadOnlyList<string> WearOf(PhotoEditInfo info)
    {
        if (info.Wear.Count > 0) return info.Wear;
        var avatarId = PhotoAvatars.FirstOrDefault(r => r.Key == info.Key)?.Avatar.Id;
        if (string.IsNullOrEmpty(avatarId) || !_avatarMasters.TryGetValue(avatarId, out var avatar)) return [];
        return avatar.OutfitIds.Select(id => _outfitMasters.TryGetValue(id, out var outfit) ? outfit.Name : null).OfType<string>().ToList();
    }

    private static string TitleOf(PostOptionViewModel post) => post.Title.Length > 0 ? post.Title : post.Slug;

    /// <summary>記事を公開する・非公開にする（本文などは変えない。draft の印だけを変える）。</summary>
    private async Task SetPublishedAsync(bool publish)
    {
        var post = SelectedPost!;
        var title = TitleOf(post);
        var message = publish
            ? $"記事「{title}」を公開にします。本文などは変えません。\nサイトには、アプリの「公開」で公開（確認用・本番）したあとに出ます（公開日時が未来のときは、本番には、その日時になってから）。"
            : $"記事「{title}」を非公開にします。本文などは変えません。\nサイトに出なくなります（次に公開（確認用・本番）したときに、サイトから消えます）。";
        if (!await Dialogs.ConfirmAsync(publish ? "記事を公開します" : "記事を非公開にします", message, publish ? "公開する" : "非公開にする")) return;
        PostEditService.SetDraft(Session.Editor, post.Slug, draft: !publish);
        Refresh(post.Slug);
        Status = publish
            ? $"記事「{title}」を公開にしました。サイトには、アプリの「公開」で公開したあとに出ます。"
            : $"記事「{title}」を非公開にしました。公開（確認用・本番）すると、サイトから消えます。";
    }

    /// <summary>記事を削除する。消す前の内容は local/deleted-posts に残す。写真（R2）は消さない。</summary>
    private async Task DeletePostAsync()
    {
        var post = SelectedPost!;
        var title = TitleOf(post);
        var backup = Path.Combine(Session.Repo.LocalDir, "deleted-posts", DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        if (!await Dialogs.ConfirmAsync("記事を削除します",
                $"記事「{title}」（{post.Slug}・写真 {post.PhotoKeys.Count} 枚）を削除します。\n記事のファイルを消します（消す前の内容は {backup} に残します）。写真（R2）は消しません。どの記事にも使われなくなった写真は、「管理」の「写真の点検」で見つけて消せます。\n公開済みの記事は、次に公開（確認用・本番）したときに、サイトから消えます。",
                "削除する")) return;
        PostEditService.Delete(Session.Editor, post.Slug, backup);
        Refresh();
        Status = $"記事「{title}」を削除しました（消す前の内容: {backup}）。サイトへの反映は、アプリの「公開」で公開したあとです。";
    }

    private async Task SaveAsync()
    {
        var isNew = IsNewArticle;
        var slug = isNew ? _newSlug : SelectedPost!.Slug;
        var wasDraft = isNew || SelectedPost!.Draft;
        var currentCover = isNew ? "" : SelectedPost!.Cover;
        var parts = ArticleDocument.Parse(Document, _postKeys, Session.Repo.ImageBaseUrl);
        if (parts.Errors.Count > 0)
        {
            Status = "本文に問題があるため保存できません: " + parts.Errors[0];
            return;
        }
        if (isNew && Title.Trim().Length == 0) throw new InvalidOperationException("題を入れてください（新しい記事には題が要ります）");
        // 公開日時は日本時間（+09:00）で保存する。画面で変えていなければ、保存してある値（秒まで）をそのまま使う
        var unchanged = _storedPublishedAt.Length > 0 && PublishedAt.Trim() == JstDate.DisplayOf(_storedPublishedAt);
        string publishedAt;
        if (unchanged) publishedAt = _storedPublishedAt;
        else if (JstDate.TryParse(PublishedAt, out var at)) publishedAt = JstDate.ToIso(at);
        else throw new InvalidOperationException("公開日時を読めません（例: 2026-10-24 20:00。日本時間で入力します）");
        var changes = new PostEditChanges(Title, publishedAt, IsDraft, SplitTags(TagsText), parts.Body, parts.Afterword, parts.Photos);
        var editor = Session.Editor;
        var ledger = Session.LoadProductionLedger();
        var now = DateTimeOffset.UtcNow;
        var draftRequest = isNew
            ? new DraftRequest
            {
                Slug = slug,
                Title = Title.Trim(),
                PublishedAt = publishedAt,
                Keys = _postKeys,
                PhotoAvatars = PhotoAvatars.Where(r => r.Avatar.Id.Length > 0).ToDictionary(r => r.Key, r => r.Avatar.Id, StringComparer.Ordinal),
            }
            : null;
        // 先に検査（書いて戻す）。落ちたら確認を出さずに理由を出す
        if (draftRequest is not null) DraftService.Run(editor, ledger, draftRequest, now, apply: false);
        else PostEditService.Save(editor, slug, changes, apply: false, now);
        // 表紙（と SNS 共有用の画像）は、本文の最初の写真に自動で合わせる
        var firstKey = parts.Photos.Count > 0 ? parts.Photos[0].Key : null;
        var coverChanged = firstKey is not null && firstKey != currentCover;
        var note = isNew
            ? $"日付 {slug} の記事を新しく作ります（{(IsDraft ? "非公開。サイトには表示されません" : "公開する記事")}）。"
            : wasDraft && !IsDraft
                ? "非公開を外して、公開する記事にします。公開（確認用・本番）したあと、サイトに表示されます。"
                : !wasDraft && IsDraft
                    ? "公開している記事を、非公開にします。公開（確認用・本番）したあと、サイトから消えます。"
                    : "サイトへの反映は、アプリの「公開」で公開したあとです。";
        if (coverChanged) note += "\n本文の最初の写真を、記事の表紙（と SNS で共有したときの画像）にします。";
        if (!await Dialogs.ConfirmAsync(isNew ? "記事を作ります" : "記事を保存します", $"記事「{Title.Trim()}」を保存します。\n{note}", isNew ? "作る" : "保存する")) return;

        string? createdPath = null;
        try
        {
            if (draftRequest is not null) createdPath = DraftService.Run(editor, ledger, draftRequest, now, apply: true).PostPath;
            PostEditService.Save(editor, slug, changes, apply: true, now);
        }
        catch
        {
            // 作った記事は、続きの保存に失敗したら消す（中途半端な記事を残さない）
            if (createdPath is not null && File.Exists(createdPath)) File.Delete(createdPath);
            throw;
        }
        var avatarNote = ApplyAvatarChanges(isNew);
        var coverNote = firstKey is null ? "" : await EnsureCoverAsync(slug, firstKey, currentCover);
        Refresh(slug);
        Status = $"記事「{Title.Trim()}」を{(isNew ? "作りました" : "保存しました")}。" + (!isNew && wasDraft && !IsDraft ? "公開する記事にしました。" : "") + avatarNote + coverNote;
    }

    /// <summary>既存の記事の写真のアバターの変更を書く（新しい記事は、作るときに入れてある）。アバターを外す操作は未対応。</summary>
    private string ApplyAvatarChanges(bool isNew)
    {
        if (isNew) return "";
        var changed = PhotoAvatars.Where(r => r.Avatar.Id.Length > 0 && r.Avatar.Id != r.OriginalAvatarId).ToList();
        foreach (var row in changed) AvatarOutfitService.PhotoAvatar(Session.Editor, true, row.Key, row.Avatar.Id, null);
        var cleared = PhotoAvatars.Count(r => r.Avatar.Id.Length == 0 && r.OriginalAvatarId.Length > 0);
        return (changed.Count > 0 ? $"写真 {changed.Count} 枚のアバターを変えました。" : "") + (cleared > 0 ? $"（アバターを「なし」に戻す操作は未対応のため、{cleared} 枚はそのままです。）" : "");
    }

    /// <summary>
    /// 本文の最初の写真を、記事の表紙にし、SNS 共有用の画像（_og.jpg）を作る。すでに同じ表紙で画像もあれば何もしない。
    /// 元の写真が見つからないときは、表紙を変えずに理由を返す（記事の保存は済んでいる）。
    /// </summary>
    private async Task<string> EnsureCoverAsync(string slug, string key, string currentCover)
    {
        var bucket = Session.ProductionBucket;
        var ledgerPath = Session.LedgerPath(bucket);
        var ledger = PhotoLedger.Load(ledgerPath);
        using var handle = Session.OpenStore(bucket);
        try
        {
            var plan = await CoverService.PlanAsync(key, ledger, Session.UploadRoot, handle.Store, default, Session.SourceRoot);
            if (currentCover == key && plan.AlreadyInR2) return "";
            await CoverService.ApplyAsync(plan, bucket, ledger, ledgerPath, handle.Store);
            PostService.SetCover(Session.Editor, bucket, slug, key);
            return "表紙を最初の写真にしました。";
        }
        catch (FileNotFoundException e)
        {
            return "ただし、表紙の画像は作れませんでした（元の写真が見つかりません）: " + UserText.Plain(Secrets.Redact(e.Message, Session.Repo));
        }
    }
}
