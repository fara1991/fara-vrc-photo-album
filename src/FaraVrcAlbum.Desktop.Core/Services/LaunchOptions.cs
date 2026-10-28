using FaraVrcAlbum.Desktop.Demo;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// 起動の引数: --repo（リポジトリ。既定はカレントから FaraVrcAlbum.sln を探す）、--upload-root（既定は設定の値か ピクチャ\VRChatAlbum）、
/// --demo（合成データを作って、R2 に接続せずに起動する。作業用フォルダを指定する）。
/// </summary>
public sealed record LaunchOptions(string? RepoPath, string? UploadRoot, string? DemoFolder)
{
    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        string? repo = null, upload = null, demo = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--repo":
                    repo = Value(args, ref i, "--repo");
                    break;
                case "--upload-root":
                    upload = Value(args, ref i, "--upload-root");
                    break;
                case "--demo":
                    demo = Value(args, ref i, "--demo");
                    break;
                default:
                    throw new UsageException($"不明な引数です: {args[i]}（--repo / --upload-root / --demo）");
            }
        }
        return new LaunchOptions(repo, upload, demo);
    }

    /// <summary>画面で使う文脈を作る。デモは合成データを作り、メモリの R2 に入れる（実接続はしない）。</summary>
    public AlbumSession CreateSession()
    {
        if (DemoFolder is not null)
        {
            var layout = DemoWorkspace.Create(DemoFolder);
            var demoSession = new AlbumSession(RepoContext.Discover(layout.RepoRoot), null, offline: true, sourceRoot: layout.SourceRoot);
            DemoWorkspace.SeedStore(demoSession.DemoStore(demoSession.ProductionBucket), PhotoLedger.Load(demoSession.LedgerPath(demoSession.ProductionBucket)))
                .GetAwaiter().GetResult();
            return demoSession;
        }

        var repo = RepoContext.Discover(RepoPath);
        return new AlbumSession(repo, UploadRoot, offline: false);
    }

    private static string Value(IReadOnlyList<string> args, ref int index, string name)
    {
        if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new UsageException($"{name} には値が必要です");
        }
        index++;
        return args[index];
    }
}
