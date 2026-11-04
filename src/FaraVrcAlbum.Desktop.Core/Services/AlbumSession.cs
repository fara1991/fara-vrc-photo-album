using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// 画面の 1 回の作業の文脈: リポジトリ・アップロード用フォルダ・R2（実接続、またはデモ用のメモリ）。
/// デモ（offline）のときは R2 に一切つながない（MemoryObjectStore だけ）。
/// </summary>
public sealed class AlbumSession
{
    private readonly Dictionary<string, MemoryObjectStore> _demoStores = new(StringComparer.Ordinal);

    private readonly string? _explicitUploadRoot;

    private readonly string? _explicitSourceRoot;

    /// <param name="uploadRoot">起動の引数（--upload-root）で決めたアップロード用フォルダ。null なら設定（無ければ既定）に従う。</param>
    /// <param name="sourceRoot">VRChat の写真のフォルダ。デモ・テストだけが指定する。null なら、いつもの場所（ユーザーの「ピクチャ」の下の VRChat。設定では変えない）。</param>
    public AlbumSession(RepoContext repo, string? uploadRoot, bool offline, string? sourceRoot = null)
    {
        Repo = repo;
        _explicitUploadRoot = uploadRoot;
        _explicitSourceRoot = sourceRoot;
        IsOffline = offline;
        StartupPreferencesAside = RecoverBrokenPreferences();
    }

    public RepoContext Repo { get; private set; }

    /// <summary>
    /// 起動（作成）のときに、壊れていた desktop.json を退避したときの退避先。無ければ null。
    /// 画面（MainViewModel）が、起動後に 1 回だけ知らせる。
    /// </summary>
    public string? StartupPreferencesAside { get; }

    private string PreferencesPath => Path.Combine(Repo.LocalDir, "desktop.json");

    /// <summary>起動のときの 1 回だけの確認。壊れていれば退避する。失敗しても起動は止めない（書くときに、もう一度退避を試す）。</summary>
    private string? RecoverBrokenPreferences()
    {
        try
        {
            return new DesktopPreferences(PreferencesPath).RecoverIfBroken();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"AlbumSession: 設定の起動時の確認で例外: {e.GetType().Name}");
            return null;
        }
    }

    /// <summary>設定（.env.local・local/album.local.json）を読み直す（設定画面で保存したあと）。フォルダの変更もすぐ反映される。</summary>
    public void Reload() => Repo = new RepoContext(Repo.Root);

    /// <summary>PC のアップロード用フォルダ（起動の引数 → 設定 → 既定）。設定を変えると、すぐ変わる。</summary>
    public string UploadRoot => Repo.ResolveUploadRoot(_explicitUploadRoot);

    /// <summary>アップロード済みの写真の、プレビュー用の URL（1280 幅）。デモ（保存先に接続しない）では null。</summary>
    public string? PhotoPreviewUrl(string? key) =>
        IsOffline || string.IsNullOrEmpty(key) ? null : SiteRules.ImageUrl(Repo.ImageBaseUrl, key, 1280);

    /// <summary>PC の VRChat 写真格納フォルダ（ユーザーの「ピクチャ」の下の VRChat。設定では変えない。VRChat の既定の保存先と同じ）。「取り込み」はここの yyyy-mm フォルダの写真を一覧にする。</summary>
    public string SourceRoot => _explicitSourceRoot ?? RepoContext.DefaultSourceRoot;

    /// <summary>true ならデモ（R2 に接続しない・公開 URL も見ない）。</summary>
    public bool IsOffline { get; }

    /// <summary>アバターのサムネイル画像のフォルダ（設定。空なら、アバターの prefab のフォルダから自動で探す）。</summary>
    public string? AvatarThumbnailFolder => Repo.LocalConfigValue("avatarThumbnailDir") is { Length: > 0 } dir ? dir.Trim() : null;

    /// <summary>fara-vrc-photo-server の履歴ファイル（history.jsonl）の場所。既定の場所にある。テストなどが、設定ファイルの photoServerHistory で変えられる（画面の設定の項目にはしない）。</summary>
    public string PhotoServerHistoryPath => Repo.LocalConfigValue("photoServerHistory") is { Length: > 0 } path ? path : PhotoServerHistory.DefaultPath;

    public string ProductionBucket => RepoContext.CurrentProductionBucket;

    /// <summary>記事の中身の控え用のバケット名（設定。空なら未設定）。写真のバケットとは別の、公開しないバケット。</summary>
    public string BackupBucket => Repo.LocalConfigValue("backupBucket")?.Trim() ?? "";

    /// <summary>
    /// 画面が操作するバケット。確認用（dev）も本番も同じ R2 を見るので、本番の 1 つだけ（試験用の選択肢は無い）。
    /// 試験用のバケットは CLI の --bucket でだけ使える。
    /// </summary>
    public string Bucket => ProductionBucket;

    public string InboxRoot => Path.Combine(UploadRoot, "inbox");

    public string UploadedRoot => Path.Combine(UploadRoot, "uploaded");

    public string HashCachePath => Path.Combine(Repo.LocalDir, "cache", "hashes.json");

    public string LockPath => Path.Combine(Repo.LocalDir, "album.lock");

    /// <summary>デスクトップ版の手元の設定（既定のアバター）。</summary>
    public DesktopPreferences Preferences => new(PreferencesPath, aside => PreferencesRecovered?.Invoke(aside));

    /// <summary>設定（desktop.json）が壊れて作り直されたときの通知（退避先のパス）。画面（MainViewModel）が 1 つだけ設定する（+= ではなく =）。</summary>
    public Action<string>? PreferencesRecovered { get; set; }

    /// <summary>バケットの台帳（本番は Git の台帳、試験用は local/ の別ファイル）。</summary>
    public string LedgerPath(string bucket) => Repo.LedgerPathFor(bucket);

    public PhotoLedger LoadLedger(string bucket) => PhotoLedger.Load(LedgerPath(bucket));

    /// <summary>記事の下書きの台帳は、CLI と同じく本番の台帳を見る（試験用の台帳からは作らない）。</summary>
    public PhotoLedger LoadProductionLedger() => PhotoLedger.Load(Repo.ProductionLedgerPath);

    public ContentEditor Editor => new(Repo.ContentDir);

    /// <summary>
    /// R2 への接続（または、デモのメモリ）。認証情報が無ければ、足りない項目の名前だけを例外にする（値は出さない）。
    /// 戻り値は破棄すること（実接続の HTTP クライアントを閉じる）。
    /// </summary>
    public StoreHandle OpenStore(string bucket)
    {
        if (!IsOffline && !RepoContext.HasConfiguredProductionBucket)
            throw new InvalidOperationException("Cloudflare R2 のバケット名が設定されていません（メニューの「設定」で入力してください）");
        RepoContext.ValidateBucketName(bucket);
        if (IsOffline)
        {
            return new StoreHandle(DemoStore(bucket), owner: null);
        }
        var store = Repo.RequireStore(bucket);
        return new StoreHandle(store, store);
    }

    /// <summary>認証情報が揃っているか（画面の表示用。値は読まない）。</summary>
    public bool HasCredentials => !IsOffline && Repo.Credentials(out _) is not null;

    /// <summary>デモ用の R2（バケットごと）。実接続のときは使われない。</summary>
    public MemoryObjectStore DemoStore(string bucket)
    {
        if (!_demoStores.TryGetValue(bucket, out var store))
        {
            store = new MemoryObjectStore();
            _demoStores[bucket] = store;
        }
        return store;
    }
}

/// <summary>開いた R2 の接続。Dispose で閉じる（デモのメモリは閉じるものが無い）。</summary>
public sealed class StoreHandle(IObjectStore store, IDisposable? owner) : IDisposable
{
    public IObjectStore Store { get; } = store;

    public void Dispose() => owner?.Dispose();
}
