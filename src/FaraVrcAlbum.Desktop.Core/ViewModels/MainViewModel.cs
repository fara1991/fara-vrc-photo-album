using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>
/// 画面の骨組み: 6 つの作業の画面（タブ）と、設定（メニューから別ウィンドウ）。バケットは本番の 1 つだけ。
/// 起動直後は inbox を読むだけ（書き込みは何もしない）。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private PageViewModel? _current;

    public MainViewModel(AlbumSession session, IDialogService dialogs)
    {
        App = new AppState(session);
        Dialogs = dialogs;
        Ingest = new IngestViewModel(App, dialogs);
        Manage = new ManageViewModel(App, dialogs);
        AvatarOutfit = new AvatarOutfitViewModel(App, dialogs);
        Editor = new PostEditorViewModel(App, dialogs);
        Publish = new PublishViewModel(App, dialogs);
        Sync = new SyncViewModel(App, dialogs);
        Settings = new SettingsViewModel(App, dialogs);

        Pages =
        [
            new PageItem("取り込み", Ingest),
            new PageItem("記事", Editor),
            new PageItem("アバター・服", AvatarOutfit),
            new PageItem("管理", Manage),
            new PageItem("同期", Sync),
            new PageItem("公開", Publish),
        ];

        Current = Ingest;
    }

    public AppState App { get; }

    public IDialogService Dialogs { get; }

    public IngestViewModel Ingest { get; }

    public ManageViewModel Manage { get; }

    public AvatarOutfitViewModel AvatarOutfit { get; }

    public PostEditorViewModel Editor { get; }

    public PublishViewModel Publish { get; }

    public SyncViewModel Sync { get; }

    public SettingsViewModel Settings { get; }

    public IReadOnlyList<PageItem> Pages { get; }

    /// <summary>今の画面。</summary>
    public PageViewModel? Current
    {
        get => _current;
        set => Set(ref _current, value);
    }

    /// <summary>画面の上部に出す、アプリの説明。</summary>
    public string AppDescription =>
        "VRChat で撮った写真を、写真ブログに載せるためのアプリです。上のタブの左から順に「取り込み → 記事」と進めます（写真のアップロードは取り込みで、記事の作成・編集・表紙は記事で行います）。";

    /// <summary>画面を切り替える。表示に必要なものは、切り替えの時に読み直す（ファイルの移動などを反映するため）。</summary>
    public void Navigate(PageViewModel page)
    {
        Current = page;
        switch (page)
        {
            case IngestViewModel ingest:
                _ = ingest.RefreshCommand.RunAsync();
                break;
            case PostEditorViewModel editor:
                _ = editor.RefreshCommand.RunAsync();
                break;
            case AvatarOutfitViewModel avatarOutfit:
                _ = avatarOutfit.RefreshCommand.RunAsync();
                break;
            case ManageViewModel manage:
                _ = manage.ScopeCommand.RunAsync();                                     // 確認する写真を、開いたときに数える
                manage.LoadAuditRows();                                                 // 点検する写真の一覧も
                break;
            case PublishViewModel publish:
                publish.Refresh();
                break;
            case SettingsViewModel settings:
                settings.Refresh();
                break;
        }
    }

    /// <summary>設定のウィンドウを開くときに呼ぶ（ファイルから読み直す）。</summary>
    public void OpenSettings() => Settings.Refresh();

    /// <summary>上のタブに出す画面（作業の 6 つ。設定はメニューから）。</summary>
    public sealed record PageItem(string Title, PageViewModel Page);
}
