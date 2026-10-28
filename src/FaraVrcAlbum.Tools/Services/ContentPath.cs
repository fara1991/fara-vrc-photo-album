using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>content/ の中のファイルを指す相対パス（控え・同期・復元で使う）の検査と、content の巻き戻し。</summary>
public static class ContentPath
{
    /// <summary>Windows で特別な意味を持つ名前（拡張子を付けても同じ）。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Windows で使えない文字。Linux の Path.GetInvalidFileNameChars() は \0 と / だけなので、Windows の分をここで明示する（CI は Linux で動く）。
    /// </summary>
    private static readonly char[] ExplicitInvalid = ['<', '>', '"', '|', '?', '*', ':', '\\'];

    /// <summary>
    /// 相対パスとして使ってよいか。.json で終わり、区切りは / だけ、ルートや上の階層（..）・空の部分・. を含まない。
    /// 各要素は、制御文字や Windows で使えない文字（: や \ など）を含まず、末尾が空白やドットでなく、予約名（CON など）でもないこと
    /// （Windows は末尾の空白やドットを落とすので、別の名前や代替データストリームに化けるため）。
    /// 控えの目録・R2 の一覧・復元の入り口で、同じ判定を使う。
    /// </summary>
    public static bool IsSafe(string path)
    {
        if (path.Length == 0 || !path.EndsWith(".json", StringComparison.Ordinal) || Path.IsPathRooted(path) || path.StartsWith('/')) return false;
        return path.Split('/').All(IsSafePart);
    }

    private static bool IsSafePart(string part)
    {
        if (part is "" or "." or "..") return false;
        if (part.EndsWith(' ') || part.EndsWith('.')) return false;
        if (part.Any(c => c < ' ' || c == '\u007f')) return false;
        if (part.IndexOfAny(ExplicitInvalid) >= 0 || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return !ReservedNames.Contains(part.Split('.')[0]);
    }

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
