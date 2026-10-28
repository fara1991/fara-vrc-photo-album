namespace FaraVrcAlbum.Content.Tests;

internal static class RepoRoot
{
    /// <summary>リポジトリのルート（FaraVrcAlbum.sln があるフォルダ）。</summary>
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FaraVrcAlbum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FaraVrcAlbum.sln が見つかりません");
    }

    /// <summary>
    /// 検査する記事の中身のフォルダ。手元には本物の content/（Git に入れない）があればそれ、無ければ（CI）samples/content の架空のサンプル。
    /// </summary>
    public static string ContentDir()
    {
        var repo = Find();
        var real = Path.Combine(repo, "content");
        return Directory.Exists(Path.Combine(real, "posts")) ? real : Path.Combine(repo, "samples", "content");
    }
}
