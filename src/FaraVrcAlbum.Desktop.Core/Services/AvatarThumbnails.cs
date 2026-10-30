using System.Text.RegularExpressions;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// アバターのサムネイル画像（手元の Unity のプロジェクトにある画像）を探す。画像はコピーも公開もしない（アプリの一覧に出すだけ）。
/// 探す場所: 設定で決めたフォルダ（あれば、その中をすべて）。無ければ自動で、アバターの prefab のフォルダの上にある「Assets」の下の、
/// 名前に thumb / icon を含むフォルダの中。ファイル名（拡張子なし）がアバター名（または別名）と同じ画像を使う（空白・大文字小文字は見ない）。
/// </summary>
public sealed class AvatarThumbnails
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];
    private const int MaxDepth = 8;
    private const int MaxDirectories = 20000;

    private readonly Dictionary<string, string> _byName;

    private AvatarThumbnails(Dictionary<string, string> byName) => _byName = byName;

    public static readonly AvatarThumbnails Empty = new([]);

    public int Count => _byName.Count;

    /// <summary>フォルダから索引を作る。configuredFolder があればそれだけを探す。無ければ avatarFolder から自動で探す。</summary>
    public static AvatarThumbnails Build(string? configuredFolder, string? avatarFolder)
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            if (Directory.Exists(configuredFolder)) AddImages(byName, configuredFolder, recursive: true);
            return new AvatarThumbnails(byName);
        }
        if (!string.IsNullOrWhiteSpace(avatarFolder) && AssetsRootOf(avatarFolder) is { } assets)
        {
            foreach (var dir in ThumbnailDirectories(assets)) AddImages(byName, dir, recursive: false);
        }
        return new AvatarThumbnails(byName);
    }

    /// <summary>アバター名（または別名）に合う画像のパス。無ければ null。</summary>
    public string? Find(string name, IEnumerable<string>? aliases = null)
    {
        foreach (var candidate in new[] { name }.Concat(aliases ?? []))
            if (_byName.TryGetValue(Normalize(candidate), out var path)) return path;
        return null;
    }

    /// <summary>名前の比べ方: 空白を取り、大文字小文字を区別しない（VRChat の「Fara AlmarianDress」と prefab の「FaraAlmarianDress」が合う）。</summary>
    public static string Normalize(string name) => Regex.Replace(name, @"\s+", "").ToLowerInvariant();

    /// <summary>prefab のフォルダから上へたどって、最初の「Assets」フォルダ（Unity のプロジェクトの Assets）。無ければ null。</summary>
    public static string? AssetsRootOf(string folder)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(folder));
        while (dir is not null)
        {
            if (string.Equals(dir.Name, "Assets", StringComparison.OrdinalIgnoreCase) && dir.Exists) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static IEnumerable<string> ThumbnailDirectories(string assets)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((assets, 0));
        var seen = 0;
        while (pending.Count > 0 && seen < MaxDirectories)
        {
            var (path, depth) = pending.Pop();
            List<string> children;
            try { children = Directory.EnumerateDirectories(path).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                seen++;
                var name = Path.GetFileName(child);
                if (name.Contains("thumb", StringComparison.OrdinalIgnoreCase) || name.Contains("icon", StringComparison.OrdinalIgnoreCase)) yield return child;
                if (depth + 1 < MaxDepth) pending.Push((child, depth + 1));
            }
        }
    }

    private static void AddImages(Dictionary<string, string> byName, string folder, bool recursive)
    {
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            byName.TryAdd(Normalize(Path.GetFileNameWithoutExtension(file)), file);           // 同じ名前が複数あれば、先に見つけたもの
        }
    }
}
