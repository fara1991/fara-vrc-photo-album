using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>content/ の中のファイルを指す相対パス（控え・同期・復元で使う）の検査と、content の巻き戻し。</summary>
public static class ContentPath
{
    /// <summary>
    /// 相対パスとして使ってよいか。.json で終わり、区切りは / だけ、ルートや上の階層（..）・空の部分・. を含まない。
    /// 控えの目録・R2 の一覧・復元の入り口で、同じ判定を使う。
    /// </summary>
    public static bool IsSafe(string path) =>
        path.Length > 0 && path.EndsWith(".json", StringComparison.Ordinal) && !Path.IsPathRooted(path) && !path.StartsWith('/')
        && !path.Contains('\\') && !path.Split('/').Any(part => part is "" or "." or "..");

    /// <summary>
    /// content のファイルを変える前の内容に戻す（before が null のものは消す）。後ろから 1 つずつ試し、戻せなかったものは一覧にして返す（残りは戻す）。
    /// </summary>
    public static List<string> RollBack(string contentDir, IEnumerable<(string Path, byte[]? Before)> changed)
    {
        var failed = new List<string>();
        foreach (var (path, before) in changed.Reverse())
        {
            try
            {
                var local = Path.Combine(contentDir, path.Replace('/', Path.DirectorySeparatorChar));
                if (before is null) File.Delete(local);
                else JsonFiles.WriteAtomic(local, before);
            }
            catch (Exception r)
            {
                failed.Add($"{path}: {r.Message}");
            }
        }
        return failed;
    }
}
