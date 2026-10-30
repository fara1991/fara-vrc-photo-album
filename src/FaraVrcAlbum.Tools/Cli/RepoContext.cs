using System.Text.Json.Nodes;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Cli;

/// <summary>
/// リポジトリの場所と、手元の設定（local/album.local.json・.env.local・環境変数）。
/// 秘密（R2 の値）は表示しない。足りない項目は名前だけを返す。
/// </summary>
public sealed class RepoContext
{
    /// <summary>本番のバケット（写真の配信元）。これ以外は試験用として扱う（台帳を分ける・uploaded/ へ移さない）。</summary>
    public const string ProductionBucket = "fara-vrc-photos";

    private static string? _productionOverride;

    /// <summary>今、本番として扱うバケット名（local/album.local.json の bucket。無い・不正なら既定）。デスクトップ版の設定で変えられる。</summary>
    public static string CurrentProductionBucket => _productionOverride ?? ProductionBucket;

    /// <summary>設定のバケット名を反映する（空・不正な名前は既定に戻す）。</summary>
    public static void ConfigureProductionBucket(string? name)
    {
        var trimmed = name?.Trim();
        _productionOverride = string.IsNullOrEmpty(trimmed) || !IsValidBucketName(trimmed) ? null : trimmed;
    }

    /// <summary>設定でバケット名が決まっているか（無いときは、デスクトップ版は保存先に接続しない。CLI は従来の名前を使う）。</summary>
    public static bool HasConfiguredProductionBucket => _productionOverride is not null;

    /// <summary>既定のアップロード用フォルダ（その PC のユーザーの「ピクチャ」の下。例: C:\Users\{ユーザー}\Pictures\VRChatAlbum）。</summary>
    public static string DefaultUploadRoot => Path.Combine(PicturesDir, "VRChatAlbum");

    /// <summary>既定の VRChat の写真のフォルダ（VRChat が写真を保存する場所。例: C:\Users\{ユーザー}\Pictures\VRChat）。</summary>
    public static string DefaultSourceRoot => Path.Combine(PicturesDir, "VRChat");

    /// <summary>ユーザーの「ピクチャ」フォルダ。取れない環境（Windows 以外の一部）では、ホームの下の Pictures。</summary>
    private static string PicturesDir
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return pictures.Length > 0 ? pictures : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        }
    }

    /// <summary>R2 の認証情報（C4）。値は表示しない。</summary>
    public static readonly string[] R2Variables = ["R2_ACCOUNT_ID", "R2_ACCESS_KEY_ID", "R2_SECRET_ACCESS_KEY"];

    private readonly Dictionary<string, string> _envFile;

    public RepoContext(string root)
    {
        Root = root;
        _envFile = ReadEnvFile(Path.Combine(root, ".env.local"));
        ConfigureProductionBucket(LocalConfig("bucket"));
        var configuredContent = LocalConfig("contentDir");
        ConfiguredContentDir = string.IsNullOrWhiteSpace(configuredContent) ? null : Path.GetFullPath(configuredContent.Trim(), root);
    }

    public string Root { get; }

    /// <summary>
    /// 設定（local/album.local.json の contentDir）で決めた、記事の中身のフォルダ。無ければ null（リポジトリの content を使う）。
    /// 設定したフォルダが無い・読めないときも、リポジトリの content に切り替えない（違う内容を直してしまうため。読むときにエラーになる）。
    /// </summary>
    public string? ConfiguredContentDir { get; }

    /// <summary>記事の中身（posts・マスタ・台帳）のフォルダ。設定があればそれ、無ければリポジトリの content。</summary>
    public string ContentDir =>
        ConfiguredContentDir is { } configured
            ? Directory.Exists(configured)
                ? configured
                : throw new DirectoryNotFoundException($"記事の中身のフォルダが見つかりません: {configured}（メニューの「設定」で確かめてください。既定の content には切り替えません）")
            : Path.Combine(Root, "content");

    public string PostsDir => Path.Combine(ContentDir, "posts");

    public string WorldsPath => Path.Combine(ContentDir, "worlds.json");

    /// <summary>本番の台帳（Git 管理）。</summary>
    public string ProductionLedgerPath => Path.Combine(ContentDir, "catalog", "photos.json");

    public string LocalDir => Path.Combine(Root, "local");

    /// <summary>R2 の認証情報とバケット名のファイル（Git 管理外）。</summary>
    public string EnvFilePath => Path.Combine(Root, ".env.local");

    /// <summary>手元のフォルダの設定（Git 管理外）。</summary>
    public string LocalConfigPath => Path.Combine(LocalDir, "album.local.json");

    /// <summary>.env.local に書いてある値（環境変数は見ない）。設定画面の編集用。</summary>
    public string? EnvFileValue(string name) => _envFile.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    /// <summary>環境変数に入っているか（入っていれば .env.local より優先される）。</summary>
    public static bool HasProcessEnv(string name) => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));

    /// <summary>local/album.local.json の値（無ければ null）。</summary>
    public string? LocalConfigValue(string key) => LocalConfig(key);

    public static RepoContext Discover(string? explicitRoot) =>
        TryFindRoot(explicitRoot ?? Directory.GetCurrentDirectory()) is { } root
            ? new RepoContext(root)
            : throw new UsageException("FaraVrcAlbum.sln が見つかりません（リポジトリの中で実行するか、--repo で指定してください）");

    /// <summary>その場所から上へたどって FaraVrcAlbum.sln のあるフォルダ。無ければ null。</summary>
    public static string? TryFindRoot(string start)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FaraVrcAlbum.sln"))) dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>写真の基準 URL（appsettings.json の ImageBaseUrl。末尾の / を含む）。</summary>
    public string ImageBaseUrl
    {
        get
        {
            var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "appsettings.json")));
            var url = settings?["ImageBaseUrl"]?.GetValue<string>() ?? throw new InvalidDataException("appsettings.json に ImageBaseUrl がありません");
            return url.EndsWith('/') ? url : url + "/";
        }
    }

    public string UploadRoot(Args args) => ResolveUploadRoot(args.Value("upload-root"));

    /// <summary>アップロード用フォルダ: 指定 → local/album.local.json → 既定の順。</summary>
    public string ResolveUploadRoot(string? explicitRoot) => explicitRoot ?? LocalConfig("uploadRoot") ?? DefaultUploadRoot;

    public string SourceRoot(Args args) => args.Value("source") ?? DefaultSourceRoot;

    /// <summary>環境変数を優先し、無ければ .env.local の値。値そのものは表示しない。</summary>
    public string? Env(string name)
    {
        var fromProcess = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(fromProcess)) return fromProcess;
        return _envFile.TryGetValue(name, out var fromFile) && fromFile.Length > 0 ? fromFile : null;
    }

    /// <summary>試験用のバケット名（.env.local の R2_BUCKET が本番以外ならそれ。無ければ fara-vrc-photos-test）。</summary>
    public const string DefaultTestBucket = "fara-vrc-photos-test";

    public string Bucket(Args args) => ResolveBucket(args.Value("bucket"));

    /// <summary>--bucket（省略可）→ .env.local の R2_BUCKET（環境変数を優先）→ 本番の順に決める。名前は検査する。</summary>
    public string ResolveBucket(string? explicitBucket)
    {
        var bucket = explicitBucket ?? Env("R2_BUCKET") ?? CurrentProductionBucket;
        ValidateBucketName(bucket);
        return bucket;
    }

    /// <summary>デスクトップ版の「試験用」の名前。本番の名前は使わない。</summary>
    public string TestBucket()
    {
        var configured = Env("R2_BUCKET");
        if (configured is null || IsProductionBucket(configured) || !IsValidBucketName(configured)) return DefaultTestBucket;
        return configured;
    }

    /// <summary>本番のバケットだけに許す操作（削除・表紙の OGP）。試験用なら例外。CLI と デスクトップ版で同じ検査。</summary>
    public static void RequireProductionBucket(string bucket, string action)
    {
        if (!IsProductionBucket(bucket))
        {
            throw new UsageException($"{action}は本番バケット（{CurrentProductionBucket}）に対してだけ行います（--bucket で試験用は指定できません）");
        }
    }

    public static bool IsValidBucketName(string bucket) =>
        System.Text.RegularExpressions.Regex.IsMatch(bucket, @"^[a-z0-9][a-z0-9-]{2,62}$");

    public static bool IsProductionBucket(string bucket) => bucket == CurrentProductionBucket;

    /// <summary>バケットごとの台帳。本番は Git の台帳、試験用は local/ の別ファイル（本番の台帳を汚さない）。</summary>
    public string LedgerPathFor(string bucket)
    {
        if (IsProductionBucket(bucket)) return ProductionLedgerPath;
        ValidateBucketName(bucket);
        var path = Path.GetFullPath(Path.Combine(LocalDir, $"catalog-{bucket}.json"));
        // 名前の検証で防いでいるが、念のため local/ の中に収まることも確かめる
        if (!path.StartsWith(Path.GetFullPath(LocalDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageException($"台帳の場所が local/ の外になるため、バケット名を拒否しました");
        }
        return path;
    }

    /// <summary>バケット名（R2 の命名に合わせる: 小文字・数字・ハイフン、3〜63 文字）。パス区切りや .. を入れさせない。</summary>
    public static void ValidateBucketName(string bucket)
    {
        if (!IsValidBucketName(bucket))
        {
            throw new UsageException("バケット名が不正です（小文字・数字・ハイフンのみ、3〜63 文字）");
        }
    }

    /// <summary>R2 の認証情報。足りない項目の名前だけを返す（値は返さない・表示しない）。</summary>
    public R2Credentials? Credentials(out IReadOnlyList<string> missing)
    {
        var missingNames = R2Variables.Where(name => Env(name) is null).ToList();
        missing = missingNames;
        if (missingNames.Count > 0) return null;
        return new R2Credentials(Env("R2_ACCOUNT_ID")!, Env("R2_ACCESS_KEY_ID")!, Env("R2_SECRET_ACCESS_KEY")!);
    }

    /// <summary>R2 に接続する。認証情報が無ければ、足りない名前を示して例外。</summary>
    public S3ObjectStore RequireStore(string bucket)
    {
        var credentials = Credentials(out var missing);
        if (credentials is null)
        {
            throw new UsageException($".env.local（または環境変数）に {string.Join(", ", missing)} が足りません。値は表示しません");
        }
        return new S3ObjectStore(credentials, bucket);
    }

    private string? LocalConfig(string key)
    {
        var path = Path.Combine(LocalDir, "album.local.json");
        if (!File.Exists(path)) return null;
        return JsonNode.Parse(File.ReadAllText(path))?[key]?.GetValue<string>();
    }

    private static Dictionary<string, string> ReadEnvFile(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return values;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var name = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];
            values[name] = value;
        }
        return values;
    }
}
