using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// アバターと服のマスタ（content/avatars.json・content/outfits.json）と、写真へのアバター・服の付与（記事の photos[]）。
/// どの操作も、書いたあとに ContentLoader（ビルドと同じ検査）を通す。失敗したら元に戻す。apply が false（既定）のときは、検査を通して、必ず元に戻す（プレビュー）。
/// Git のファイルだけを書く（R2 には触らない）。
/// </summary>
public static class AvatarOutfitService
{
    public const string OutfitsFile = "outfits.json";
    public const string AvatarsFile = "avatars.json";

    /// <summary>服を追加する（id が既にあれば拒否。直すのは <see cref="OutfitEdit"/>）。</summary>
    public static void OutfitAdd(ContentEditor editor, bool apply, string id, string name, string? shop, string? url, string? note) =>
        Transact(editor, apply, [OutfitsPath(editor)], () =>
        {
            var outfits = LoadMaster(OutfitsPath(editor));
            if (outfits.ContainsKey(id)) throw new InvalidOperationException($"服 {id} は既にあります（直すには album outfit edit）");
            outfits[id] = Entry(("name", name), ("shop", shop), ("url", url), ("note", note));
            ContentEditor.WriteNode(OutfitsPath(editor), outfits);
        });

    /// <summary>服の項目を直す（指定した項目だけ。空の文字列は項目を消す）。</summary>
    public static void OutfitEdit(ContentEditor editor, bool apply, string id, string? name, string? shop, string? url, string? note) =>
        Transact(editor, apply, [OutfitsPath(editor)], () =>
        {
            var outfits = LoadMaster(OutfitsPath(editor));
            var entry = outfits[id]?.AsObject() ?? throw new InvalidOperationException($"服 {id} がありません");
            Set(entry, "name", name);
            Set(entry, "shop", shop);
            Set(entry, "url", url);
            Set(entry, "note", note);
            ContentEditor.WriteNode(OutfitsPath(editor), outfits);
        });

    /// <summary>BOOTH の商品 1 件の情報（BOOTH Library Manager の保存データから読んだもの）。</summary>
    public sealed record BoothInfo(string? Thumbnail, string? Description, IReadOnlyList<string> Tags, string? PublishedAt, string? UpdatedAt, bool Adult);

    /// <summary>BOOTH の情報の取り込みの結果。</summary>
    public sealed record BoothFillResult(int Updated, int AlreadyFilled, int NoBoothUrl, int NotInLibrary, int Adult);

    /// <summary>BOOTH の商品ページの URL（booth.pm/…/items/{ID}）から、商品 ID。その形でなければ null。</summary>
    public static long? BoothItemIdOf(string? url) =>
        url is not null && System.Text.RegularExpressions.Regex.Match(url, @"/items/(?<id>\d+)") is { Success: true } m && long.TryParse(m.Groups["id"].Value, out var id) ? id : null;

    /// <summary>
    /// 服の URL（BOOTH の商品ページ）から商品を探し、画像（サムネイル）・説明・タグ・公開日・更新日・取り込んだ日時を、服の項目に書く。
    /// <b>差分（既定。overwrite が false）</b>は、服の項目のうち<b>まだ無いものだけ</b>を足す（すでにある画像・説明・タグ・日付は変えない）。
    /// <b>上書き（overwrite が true。再インポート）</b>は、すでにあるものも BOOTH の今の内容にそろえる。
    /// onlyIds があれば、その id の服だけが対象（無ければ全部）。<b>成人向け（adult）の商品は、画像・説明を取り込まない</b>（公開するサイトに出さないため）。
    /// 名前・お店・URL・備考は変えない。書いたあとに検査する。apply が false なら、検査して元に戻す（プレビュー）。
    /// 結果: Updated = 項目が変わった服、AlreadyFilled = 足すもの・直すものが無かった服（差分で、すでに揃っている）。
    /// </summary>
    public static BoothFillResult OutfitFillFromBooth(ContentEditor editor, bool apply, Func<long, BoothInfo?> lookup, DateTimeOffset now, bool overwrite = false, IReadOnlySet<string>? onlyIds = null)
    {
        int updated = 0, filled = 0, noUrl = 0, missing = 0, adult = 0;
        Transact(editor, apply, [OutfitsPath(editor)], () =>
        {
            var outfits = LoadMaster(OutfitsPath(editor));
            foreach (var (id, node) in outfits.ToList())
            {
                if (node is not JsonObject entry) continue;
                if (onlyIds is not null && !onlyIds.Contains(id)) continue;
                if (BoothItemIdOf(entry["url"]?.GetValue<string>()) is not { } itemId) { noUrl++; continue; }
                if (lookup(itemId) is not { } info) { missing++; continue; }
                if (info.Adult) { adult++; continue; }
                var before = entry.ToJsonString();
                if (overwrite || IsMissing(entry, "thumbnail")) Set(entry, "thumbnail", info.Thumbnail ?? "");
                if (overwrite || IsMissing(entry, "description")) Set(entry, "description", string.IsNullOrWhiteSpace(info.Description) ? "" : Truncate(info.Description.Trim(), MaxBoothDescription));
                if (overwrite || IsMissing(entry, "tags")) { if (info.Tags.Count > 0) entry["tags"] = StringArray(info.Tags.Take(30)); else entry.Remove("tags"); }
                if (overwrite || IsMissing(entry, "publishedAt")) Set(entry, "publishedAt", info.PublishedAt ?? "");
                if (overwrite || IsMissing(entry, "updatedAt")) Set(entry, "updatedAt", info.UpdatedAt ?? "");
                if (entry.ToJsonString() == before) { filled++; continue; }                          // 足すもの・直すものが無かった
                Set(entry, "fetchedAt", now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture));
                updated++;
            }
            if (updated > 0) ContentEditor.WriteNode(OutfitsPath(editor), outfits);
        });
        return new BoothFillResult(updated, filled, noUrl, missing, adult);
    }

    /// <summary>服の項目が、まだ無い（項目が無い・空の文字・空の配列）か。</summary>
    private static bool IsMissing(JsonObject entry, string key) => entry[key] switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonValue value => value.TryGetValue<string>(out var text) && text.Length == 0,
        _ => false,
    };

    /// <summary>取り込む商品の説明の長さ（長いものは、サイトでは最初の 3 行だけ見せて「続きを見る」で全文）。</summary>
    public const int MaxBoothDescription = 3000;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    /// <summary>服を外す。アバターの既定の服・写真の outfits から参照されていれば拒否する。</summary>
    public static void OutfitRemove(ContentEditor editor, bool apply, string id)
    {
        var users = OutfitUsers(editor, id);
        if (users.Count > 0) throw new InvalidOperationException($"服 {id} は使われています（先に外してください）: {string.Join(", ", users)}");
        Transact(editor, apply, [OutfitsPath(editor)], () =>
        {
            var outfits = LoadMaster(OutfitsPath(editor));
            if (!outfits.Remove(id)) throw new InvalidOperationException($"服 {id} がありません");
            ContentEditor.WriteNode(OutfitsPath(editor), outfits);
        });
    }

    /// <summary>アバターを追加する（id が既にあれば拒否）。aliases は表記ゆれ（任意）。</summary>
    public static void AvatarAdd(ContentEditor editor, bool apply, string id, string name, IEnumerable<string> aliases, string? note) =>
        Transact(editor, apply, [AvatarsPath(editor)], () =>
        {
            var avatars = LoadMaster(AvatarsPath(editor));
            if (avatars.ContainsKey(id)) throw new InvalidOperationException($"アバター {id} は既にあります（直すには album avatar edit）");
            var entry = new JsonObject
            {
                ["name"] = name,
                ["aliases"] = StringArray(aliases),
                ["outfitIds"] = new JsonArray(),
            };
            if (!string.IsNullOrEmpty(note)) entry["note"] = note;
            avatars[id] = entry;
            ContentEditor.WriteNode(AvatarsPath(editor), avatars);
        });

    /// <summary>アバターの名前・表記ゆれ・備考を直す（指定した項目だけ）。服の紐づけは <see cref="AvatarLink"/>。</summary>
    public static void AvatarEdit(ContentEditor editor, bool apply, string id, string? name, IEnumerable<string>? aliases, string? note) =>
        Transact(editor, apply, [AvatarsPath(editor)], () =>
        {
            var avatars = LoadMaster(AvatarsPath(editor));
            var entry = avatars[id]?.AsObject() ?? throw new InvalidOperationException($"アバター {id} がありません");
            Set(entry, "name", name);
            if (aliases is not null) entry["aliases"] = StringArray(aliases);
            Set(entry, "note", note);
            ContentEditor.WriteNode(AvatarsPath(editor), avatars);
        });

    /// <summary>アバターを外す。写真がこのアバターを使っていれば拒否する。</summary>
    public static void AvatarRemove(ContentEditor editor, bool apply, string id)
    {
        var used = PhotosWhere(editor, p => p["avatar"]?.GetValue<string>() == id);
        if (used.Count > 0) throw new InvalidOperationException($"アバター {id} は写真に使われています（先に外してください）: {string.Join(", ", used)}");
        Transact(editor, apply, [AvatarsPath(editor)], () =>
        {
            var avatars = LoadMaster(AvatarsPath(editor));
            if (!avatars.Remove(id)) throw new InvalidOperationException($"アバター {id} がありません");
            ContentEditor.WriteNode(AvatarsPath(editor), avatars);
        });
    }

    /// <summary>アバターに着せている服（既定の服・順序つき）を置き換える。</summary>
    public static void AvatarLink(ContentEditor editor, bool apply, string id, IReadOnlyList<string> outfitIds) =>
        Transact(editor, apply, [AvatarsPath(editor)], () =>
        {
            var avatars = LoadMaster(AvatarsPath(editor));
            var entry = avatars[id]?.AsObject() ?? throw new InvalidOperationException($"アバター {id} がありません");
            entry["outfitIds"] = StringArray(outfitIds);
            ContentEditor.WriteNode(AvatarsPath(editor), avatars);
        });

    /// <summary>記事の写真（key）にアバターを付ける。outfits を渡すと、その写真の服を上書きする（省略はアバターの既定）。</summary>
    public static void PhotoAvatar(ContentEditor editor, bool apply, string key, string avatar, IReadOnlyList<string>? outfits)
    {
        var path = PostPathOf(editor, key) ?? throw new InvalidOperationException($"写真 {key} が記事にありません（先に記事へ入れてから付けます）");
        Transact(editor, apply, [path], () =>
        {
            var post = ContentEditor.ReadObject(path);
            var photo = (post["photos"] as JsonArray)?.Select(p => p?.AsObject()).FirstOrDefault(p => p?["key"]?.GetValue<string>() == key)
                ?? throw new InvalidOperationException($"写真 {key} が記事にありません");
            photo["avatar"] = avatar;
            if (outfits is not null) photo["outfits"] = StringArray(outfits);
            ContentEditor.WriteNode(path, post);
        });
    }

    /// <summary>服を使っている場所（アバターの既定・写真の outfits）。</summary>
    public static List<string> OutfitUsers(ContentEditor editor, string outfitId)
    {
        var users = new List<string>();
        foreach (var (id, entry) in LoadMaster(AvatarsPath(editor)))
        {
            if ((entry?["outfitIds"] as JsonArray)?.Any(x => x?.GetValue<string>() == outfitId) == true) users.Add("アバター " + id);
        }
        users.AddRange(PhotosWhere(editor, p => (p["outfits"] as JsonArray)?.Any(x => x?.GetValue<string>() == outfitId) == true));
        return users;
    }

    private static List<string> PhotosWhere(ContentEditor editor, Func<JsonObject, bool> match)
    {
        var found = new List<string>();
        foreach (var path in editor.PostFiles())
        {
            var post = ContentEditor.ReadObject(path);
            foreach (var photo in (post["photos"] as JsonArray ?? []).Select(p => p?.AsObject()).OfType<JsonObject>())
            {
                if (match(photo)) found.Add($"{Path.GetFileNameWithoutExtension(path)} の {photo["key"]?.GetValue<string>()}");
            }
        }
        return found;
    }

    private static string? PostPathOf(ContentEditor editor, string key) =>
        editor.PostFiles().FirstOrDefault(path => (ContentEditor.ReadObject(path)["photos"] as JsonArray ?? [])
            .Any(p => p?["key"]?.GetValue<string>() == key));

    private static string OutfitsPath(ContentEditor editor) => Path.Combine(editor.ContentDir, OutfitsFile);

    private static string AvatarsPath(ContentEditor editor) => Path.Combine(editor.ContentDir, AvatarsFile);

    private static JsonObject LoadMaster(string path) => File.Exists(path) ? ContentEditor.ReadObject(path) : new JsonObject();

    private static JsonArray StringArray(IEnumerable<string> values) => new(values.Select(v => (JsonNode)v).ToArray());

    private static JsonObject Entry(params (string Key, string? Value)[] fields)
    {
        var entry = new JsonObject();
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrEmpty(value)) entry[key] = value;
        }
        return entry;
    }

    /// <summary>値を入れる（null は触らない・空の文字列は項目を消す）。</summary>
    private static void Set(JsonObject entry, string key, string? value)
    {
        if (value is null) return;
        if (value.Length == 0) entry.Remove(key);
        else entry[key] = value;
    }

    /// <summary>
    /// ファイルを書いて検査する。apply が false のときは、検査のあと必ず元に戻す（プレビュー）。
    /// 検査に失敗したら元に戻して、例外を上に投げる。
    /// </summary>
    private static void Transact(ContentEditor editor, bool apply, IEnumerable<string> files, Action write)
    {
        var snapshot = new FileSnapshot();
        foreach (var file in files) snapshot.Capture(file);
        try
        {
            write();
            editor.ValidateOrThrow();
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
        if (!apply) snapshot.Restore();
    }
}
