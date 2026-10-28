using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// 書き込み前の内容（元に戻すため）。バイト列のまま控えるので、BOM・改行・文字コードも元どおりに戻る。null は「無かった」を表す。
/// </summary>
public sealed class FileSnapshot
{
    // 控えた順に並べる（戻すときは後ろから）
    private readonly List<(string Path, byte[]? Bytes)> _before = [];
    private readonly HashSet<string> _captured = new(StringComparer.Ordinal);

    /// <summary>書く前の内容を控える（同じパスは最初の 1 回だけ）。</summary>
    public void Capture(string path)
    {
        if (_captured.Add(path)) _before.Add((path, File.Exists(path) ? File.ReadAllBytes(path) : null));
    }

    /// <summary>
    /// 控えに戻す（後ろから）。1 つ戻せなくても残りは戻し、戻せなかったものの一覧を返す（投げない）。
    /// 一覧が空なら全部戻った。呼び出し側が、画面ごとの文で知らせたいときに使う。
    /// </summary>
    public List<string> TryRestore()
    {
        var failed = new List<string>();
        for (var i = _before.Count - 1; i >= 0; i--)
        {
            var (path, bytes) = _before[i];
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
        return failed;
    }

    /// <summary>控えに戻す。戻せなかったものがあれば、一覧にして IOException（<see cref="TryRestore"/> を使う）。</summary>
    public void Restore()
    {
        var failed = TryRestore();
        if (failed.Count > 0) throw new IOException("元に戻せなかったファイルがあります:\n  " + string.Join("\n  ", failed));
    }

    /// <summary>
    /// paths の内容を控えてから action を実行する。例外になったら控えに戻して、同じ例外を投げ直す（型も変えない）。
    /// 戻すことも失敗したら、元の例外（と戻せなかった一覧）を含む IOException にし、元の例外は InnerException に残す。
    /// action の中で検査（ValidateOrThrow）まで行う。keep が false のときは、成功しても必ず控えに戻す（プレビュー）。
    /// </summary>
    public static void Run(IEnumerable<string> paths, Action action, bool keep = true) =>
        Run(paths, () => { action(); return 0; }, keep);

    /// <summary><see cref="Run(IEnumerable{string}, Action, bool)"/> の戻り値つき。</summary>
    public static T Run<T>(IEnumerable<string> paths, Func<T> action, bool keep = true)
    {
        var snapshot = new FileSnapshot();
        foreach (var path in paths) snapshot.Capture(path);
        T result;
        try
        {
            result = action();
        }
        catch (Exception e)
        {
            var failed = snapshot.TryRestore();
            if (failed.Count > 0)
                throw new IOException($"失敗しましたが（{e.Message}）、元に戻すこともできませんでした: {string.Join("; ", failed)}。記事の内容を手で確かめてください", e);
            throw;
        }
        if (!keep) snapshot.Restore();
        return result;
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
