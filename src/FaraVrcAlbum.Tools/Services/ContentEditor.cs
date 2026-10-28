using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>書き込み前の内容（元に戻すため）。null は「無かった」を表す。</summary>
public sealed class FileSnapshot
{
    private readonly Dictionary<string, string?> _before = new(StringComparer.Ordinal);

    public void Capture(string path)
    {
        if (!_before.ContainsKey(path)) _before[path] = File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Restore()
    {
        foreach (var (path, text) in _before)
        {
            if (text is null)
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            }
        }
    }
}

/// <summary>
/// content/ の JSON の読み書き。既存の項目の順序・内容は保つ（JsonNode で編集する）。
/// 書いたあとは <see cref="ValidateOrThrow"/> で ContentLoader（ビルドと同じ検査）を通す。
/// </summary>
public sealed class ContentEditor
{
    private readonly string _contentDir;

    public ContentEditor(string contentDir)
    {
        _contentDir = contentDir;
    }

    public string ContentDir => _contentDir;

    public string PostsDir => Path.Combine(_contentDir, "posts");

    public string PostPath(string slug) => Path.Combine(PostsDir, slug + ".json");

    public string WorldsPath => Path.Combine(_contentDir, "worlds.json");

    /// <summary>記事の JSON（draft も含む全件）。</summary>
    public IEnumerable<string> PostFiles() =>
        Directory.Exists(PostsDir) ? Directory.EnumerateFiles(PostsDir, "*.json").OrderBy(p => p, StringComparer.Ordinal) : [];

    public static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException($"{Path.GetFileName(path)} が空です");

    public static void WriteNode(string path, JsonNode node) => JsonFiles.WriteAtomic(path, node.ToJsonString(JsonFiles.Options));

    /// <summary>ContentLoader で読んで検査する（ビルドと同じ）。問題があれば一覧にして InvalidDataException。</summary>
    public ContentSet LoadContent()
    {
        try
        {
            return ContentLoader.Load(ContentDir);
        }
        catch (ContentException e)
        {
            throw new InvalidDataException("content/ の検査に失敗しました:\n  " + string.Join("\n  ", e.Problems));
        }
    }

    /// <summary>ContentLoader で全体を検査する（ビルドと同じ）。問題があれば一覧にして例外。</summary>
    public void ValidateOrThrow()
    {
        try
        {
            ContentLoader.Load(ContentDir);
        }
        catch (ContentException e)
        {
            throw new InvalidDataException("content/ の検査に失敗しました:\n  " + string.Join("\n  ", e.Problems));
        }
    }
}
