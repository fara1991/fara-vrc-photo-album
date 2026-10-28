using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// 書き込み前の内容（元に戻すため）。バイト列のまま控えるので、BOM・改行・文字コードも元どおりに戻る。null は「無かった」を表す。
/// </summary>
public sealed class FileSnapshot
{
    private readonly Dictionary<string, byte[]?> _before = new(StringComparer.Ordinal);

    public void Capture(string path)
    {
        if (!_before.ContainsKey(path)) _before[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>控えに戻す。1 つ戻せなくても残りは戻し、戻せなかったものを一覧にして最後に投げる（ContentPath.RollBack と同じ）。</summary>
    public void Restore()
    {
        var failed = new List<string>();
        foreach (var (path, bytes) in _before)
        {
            try
            {
                if (bytes is null)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                else
                {
                    JsonFiles.WriteAtomic(path, bytes);
                }
            }
            catch (Exception r)
            {
                failed.Add($"{path}: {r.Message}");
            }
        }
        if (failed.Count > 0) throw new IOException("元に戻せなかったファイルがあります:\n  " + string.Join("\n  ", failed));
    }

    /// <summary>
    /// paths の内容を控えてから action を実行する。例外になったら控えに戻して、同じ例外を投げ直す
    /// （戻すことも失敗したら、両方の理由を含む IOException にする）。action の中で検査（ValidateOrThrow）まで行う。
    /// </summary>
    public static void Run(IEnumerable<string> paths, Action action) => Run(paths, () => { action(); return 0; });

    /// <summary><see cref="Run(IEnumerable{string}, Action)"/> の戻り値つき。</summary>
    public static T Run<T>(IEnumerable<string> paths, Func<T> action)
    {
        var snapshot = new FileSnapshot();
        foreach (var path in paths) snapshot.Capture(path);
        try
        {
            return action();
        }
        catch (Exception e)
        {
            try
            {
                snapshot.Restore();
            }
            catch (Exception r)
            {
                throw new IOException($"失敗しましたが、元に戻すこともできませんでした（{r.Message}）。記事の内容を手で確かめてください", e);
            }
            throw;
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
    public void ValidateOrThrow() => LoadContent();
}
