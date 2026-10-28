using System.Text.Json;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>記事の一覧（下書きを含む）。画面の一覧・写真の紐づけの候補に使う。</summary>
public sealed record PostSummary(string Slug, string Title, bool Draft, string PublishedAt, string? Cover, IReadOnlyList<string> PhotoKeys, string? Problem);

/// <summary>
/// 記事の読み取りと表紙の設定（デスクトップ版と CLI の共通）。書き込みは検査（ContentLoader）を通し、失敗したら元に戻す。
/// </summary>
public static class PostService
{
    /// <summary>表紙になっている写真（cover、無ければ先頭の写真）。下書きも含む全記事から。</summary>
    public static HashSet<string> CoverKeys(ContentEditor editor)
    {
        var covers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in editor.PostFiles())
        {
            var post = ContentEditor.ReadObject(path);
            var cover = post["cover"]?.GetValue<string>()
                ?? (post["photos"] as JsonArray)?.FirstOrDefault()?["key"]?.GetValue<string>();
            if (cover is not null) covers.Add(cover);
        }
        return covers;
    }

    /// <summary>記事（下書きを含む全件）の写真のキー。</summary>
    public static HashSet<string> PhotoKeys(ContentEditor editor)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in editor.PostFiles())
        {
            var photos = ContentEditor.ReadObject(path)["photos"] as JsonArray;
            if (photos is null) continue;
            foreach (var photo in photos)
            {
                if (photo?["key"]?.GetValue<string>() is { } key) keys.Add(key);
            }
        }
        return keys;
    }

    /// <summary>記事の一覧。読めない記事は Problem に理由を入れて残す（一覧が途中で止まらないように）。</summary>
    public static IReadOnlyList<PostSummary> List(ContentEditor editor)
    {
        var list = new List<PostSummary>();
        foreach (var path in editor.PostFiles())
        {
            try
            {
                var post = ContentEditor.ReadObject(path);
                var keys = (post["photos"] as JsonArray)?.Select(p => p?["key"]?.GetValue<string>() ?? "").Where(k => k.Length > 0).ToList() ?? [];
                list.Add(new PostSummary(
                    post["slug"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(path),
                    post["title"]?.GetValue<string>() ?? "",
                    post["draft"]?.GetValue<bool>() ?? false,
                    post["publishedAt"]?.GetValue<string>() ?? "",
                    post["cover"]?.GetValue<string>(),
                    keys,
                    null));
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or InvalidOperationException or FormatException)
            {
                list.Add(new PostSummary(Path.GetFileNameWithoutExtension(path), "", false, "", null, [], "読めませんでした: " + e.GetType().Name));
            }
        }
        return list;
    }

    /// <summary>表紙を設定する（cover）。key が null なら cover を外す（先頭の写真になる）。写真が記事に無ければ例外。</summary>
    public static void SetCover(ContentEditor editor, string bucket, string slug, string? key)
    {
        // 表紙の設定は本番だけ（OGP と一緒に行う操作）
        RepoContext.RequireProductionBucket(bucket, "表紙の設定");
        var path = editor.PostPath(slug);
        if (!File.Exists(path)) throw new FileNotFoundException($"記事がありません: {slug}");
        var snapshot = new FileSnapshot();
        snapshot.Capture(path);
        try
        {
            var post = ContentEditor.ReadObject(path);
            if (key is null)
            {
                post.Remove("cover");
            }
            else
            {
                var has = (post["photos"] as JsonArray)?.Any(p => p?["key"]?.GetValue<string>() == key) == true;
                if (!has) throw new InvalidOperationException($"記事 {slug} に写真 {key} がありません");
                post["cover"] = key;
            }
            ContentEditor.WriteNode(path, post);
            editor.ValidateOrThrow();
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
    }
}
