using System.Globalization;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>編集画面に出す、写真 1 枚の情報。自動で決まる項目（撮影日時・ワールド・服）は読むだけ。</summary>
public sealed record PhotoEditInfo(string Key, string? TakenAt, string? WorldName, IReadOnlyList<string> Wear, string Caption, string Text, string Avatar = "");

/// <summary>記事の編集できる項目（下書きを含む）。</summary>
public sealed record PostEditModel(
    string Slug,
    string Title,
    string PublishedAt,
    bool Draft,
    IReadOnlyList<string> Tags,
    string Body,
    string Afterword,
    IReadOnlyList<PhotoEditInfo> Photos);

/// <summary>
/// 保存する値。Tags はタグの表示名か slug（登録の無い名前は、tags.json に新しく足す）。
/// Photos は記事に出す順の写真（key・説明・前の文章）。記事の写真を全部、1 回ずつ含めること（並び替えはできる。足す・外すはできない）。
/// </summary>
public sealed record PostEditChanges(
    string Title,
    string PublishedAt,
    bool Draft,
    IReadOnlyList<string> Tags,
    string Body,
    string Afterword,
    IReadOnlyList<(string Key, string Caption, string Text)> Photos);

/// <summary>
/// 記事の文章の編集（題・公開日・下書きかどうか・タグ・はじめの文章・写真ごとの説明と前の文章・おわりの文章）。
/// 写真の並び・ワールド・服・表紙などは変えない（ほかの項目と順序は保つ。JsonNode で編集する）。
/// 書いたあと、ビルドと同じ検査を通す。通らなければ元に戻す。apply が false なら、検査だけして必ず元に戻す。
/// </summary>
public static class PostEditService
{
    /// <summary>
    /// 記事の公開・非公開（draft の印）だけを変える（本文などは触らない）。draft にする（非公開）と、どの環境にも出ない。外す（公開）と、確認用・本番に出る（本番は、公開日が過ぎていれば）。
    /// 書いたあと content の検査に通らなければ元に戻して例外にする。
    /// </summary>
    public static void SetDraft(ContentEditor editor, string slug, bool draft)
    {
        var path = editor.PostPath(slug);
        if (!File.Exists(path)) throw new FileNotFoundException($"記事がありません: {slug}");
        FileSnapshot.Run([path], () =>
        {
            var post = ContentEditor.ReadObject(path);
            if (draft) post["draft"] = true;
            else post.Remove("draft");
            ContentEditor.WriteNode(path, post);
            editor.ValidateOrThrow();
        });
    }

    /// <summary>
    /// 記事を削除する（content/posts/{slug}.json を消す）。消す前の内容を backupDir/{slug}.json に残す。写真（R2）・表紙の画像は消さない
    /// （写真は「管理」タブの「写真の点検」で、どの記事にも使われていないものとして見つけて消せる）。消したあと content の検査に通らなければ元に戻して例外にする。
    /// </summary>
    public static void Delete(ContentEditor editor, string slug, string backupDir)
    {
        var path = editor.PostPath(slug);
        if (!File.Exists(path)) throw new FileNotFoundException($"記事がありません: {slug}");
        Directory.CreateDirectory(backupDir);
        File.Copy(path, Path.Combine(backupDir, slug + ".json"), overwrite: true);
        FileSnapshot.Run([path], () =>
        {
            File.Delete(path);
            editor.ValidateOrThrow();
        });
    }

    public static PostEditModel Load(ContentEditor editor, string slug)
    {
        var path = editor.PostPath(slug);
        if (!File.Exists(path)) throw new FileNotFoundException($"記事がありません: {slug}");
        var content = editor.LoadContent();
        var post = ContentEditor.ReadObject(path);
        var photos = new List<PhotoEditInfo>();
        foreach (var node in (post["photos"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var worldId = node["world"]?["id"]?.GetValue<string>();
            var worldName = worldId is not null && content.Worlds.TryGetValue(worldId, out var world) ? world.Name : node["world"]?["name"]?.GetValue<string>();
            var photo = new PhotoInfo
            {
                Avatar = node["avatar"]?.GetValue<string>(),
                Outfits = (node["outfits"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToList(),
            };
            var wear = OutfitRules.Resolve(photo, content.Avatars, content.Outfits)?.Select(w => w.Name).ToList() ?? [];
            photos.Add(new PhotoEditInfo(
                node["key"]?.GetValue<string>() ?? "",
                node["takenAt"]?.GetValue<string>(),
                worldName,
                wear,
                node["caption"]?.GetValue<string>() ?? "",
                node["text"]?.GetValue<string>() ?? "",
                node["avatar"]?.GetValue<string>() ?? ""));
        }
        return new PostEditModel(
            post["slug"]?.GetValue<string>() ?? slug,
            post["title"]?.GetValue<string>() ?? "",
            post["publishedAt"]?.GetValue<string>() ?? "",
            post["draft"]?.GetValue<bool>() ?? false,
            (post["tags"] as JsonArray)?.Select(t => t!.GetValue<string>()).ToList() ?? [],
            post["body"]?.GetValue<string>() ?? "",
            post["afterword"]?.GetValue<string>() ?? "",
            photos);
    }

    /// <summary>
    /// タグの名前（または slug）から slug を決める。登録があればそれ。無ければ新しい slug（英小文字・数字・ハイフンだけの名前はそのまま、それ以外は "tag-" + ハッシュ 8 桁）。
    /// </summary>
    public static string TagSlug(string nameOrSlug, IReadOnlyDictionary<string, string> existingNames)
    {
        var name = nameOrSlug.Trim();
        if (existingNames.ContainsKey(name)) return name;
        var byName = existingNames.FirstOrDefault(t => t.Value == name).Key;
        if (byName is not null) return byName;
        var lower = name.ToLowerInvariant().Replace(' ', '-');
        if (SiteRules.IsValidSlug(lower)) return lower;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
        return "tag-" + hash[..8];
    }

    /// <summary>タグのマスタ（slug → 表示名）。</summary>
    public static IReadOnlyList<(string Slug, string Name)> Tags(ContentEditor editor) =>
        editor.LoadContent().Tags.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => (t.Key, t.Value.Name)).ToList();

    /// <summary>記事を書き換える。下書きを外す（公開する）ときは、公開日を今にそろえるかどうかを呼び出し側が決める（PublishedAt で渡す）。</summary>
    public static void Save(ContentEditor editor, string slug, PostEditChanges changes, bool apply, DateTimeOffset now)
    {
        var path = editor.PostPath(slug);
        if (!File.Exists(path)) throw new FileNotFoundException($"記事がありません: {slug}");
        if (!DateTimeOffset.TryParse(changes.PublishedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("公開日時の形式が正しくありません（例: 2026-10-24T20:00:00+09:00）");
        var tagsPath = Path.Combine(editor.ContentDir, "tags.json");
        var snapshot = new FileSnapshot();
        snapshot.Capture(path);
        snapshot.Capture(tagsPath);
        try
        {
            var post = ContentEditor.ReadObject(path);
            // タグ: 名前か slug。登録の無い名前は tags.json に足す（表示名はその名前）
            var tagMaster = File.Exists(tagsPath) ? ContentEditor.ReadObject(tagsPath) : new JsonObject();
            var names = tagMaster.ToDictionary(t => t.Key, t => t.Value?["name"]?.GetValue<string>() ?? t.Key, StringComparer.Ordinal);
            var slugs = new List<string>();
            foreach (var tag in changes.Tags.Select(t => t.Trim()).Where(t => t.Length > 0))
            {
                var tagSlug = TagSlug(tag, names);
                if (!names.ContainsKey(tagSlug))
                {
                    tagMaster[tagSlug] = new JsonObject { ["name"] = tag };
                    names[tagSlug] = tag;
                }
                if (!slugs.Contains(tagSlug)) slugs.Add(tagSlug);
            }
            if (tagMaster.Count != (File.Exists(tagsPath) ? ContentEditor.ReadObject(tagsPath).Count : 0)) ContentEditor.WriteNode(tagsPath, tagMaster.DeepClone());
            var wasDraft = post["draft"]?.GetValue<bool>() ?? false;
            post["title"] = changes.Title.Trim();
            post["publishedAt"] = changes.PublishedAt.Trim();
            post["draft"] = changes.Draft;
            post["tags"] = new JsonArray(slugs.Select(t => (JsonNode)t).ToArray());
            post["body"] = Normalize(changes.Body);
            SetOrRemove(post, "afterword", changes.Afterword);
            // 公開済みの記事の文章を直したら、更新日時を入れる（下書きのあいだ・公開した瞬間は入れない）
            if (!wasDraft && !changes.Draft) post["updatedAt"] = now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            // 写真: 渡された順に並べ直す（記事の写真を全部、1 回ずつ）。説明と前の文章を書く。ほかの項目（ワールド・アバターなど）は保つ
            var current = (post["photos"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var byKey = current.ToDictionary(n => n["key"]?.GetValue<string>() ?? "", StringComparer.Ordinal);
            var order = changes.Photos.Select(p => p.Key).ToList();
            if (order.Distinct(StringComparer.Ordinal).Count() != order.Count) throw new InvalidDataException("同じ写真が 2 回あります");
            var unknown = order.Where(k => !byKey.ContainsKey(k)).ToList();
            if (unknown.Count > 0) throw new InvalidDataException($"この記事に無い写真があります: {string.Join(", ", unknown)}");
            var missing = byKey.Keys.Where(k => !order.Contains(k)).ToList();
            if (missing.Count > 0) throw new InvalidDataException($"記事の写真が足りません（写真を外すときは「管理」の削除を使います）: {string.Join(", ", missing)}");
            var reordered = new JsonArray();
            foreach (var (key, caption, text) in changes.Photos)
            {
                var node = byKey[key];
                node.Parent?.AsArray().Remove(node);
                SetOrRemove(node, "caption", caption);
                SetOrRemove(node, "text", text);
                reordered.Add(node);
            }
            post["photos"] = reordered;
            ContentEditor.WriteNode(path, post);
            editor.ValidateOrThrow();
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
        if (!apply) snapshot.Restore();
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static void SetOrRemove(JsonObject node, string name, string value)
    {
        var text = Normalize(value);
        if (text.Length == 0) node.Remove(name);
        else node[name] = text;
    }
}
