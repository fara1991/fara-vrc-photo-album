using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Content;

/// <summary>content/ の検証で見つかった問題（まとめて 1 回で報告する）。</summary>
public sealed class ContentException(IReadOnlyList<string> problems)
    : Exception("content/ の検証に失敗しました:\n" + string.Join("\n", problems.Select(p => "  - " + p)))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>読み込んで検証した content/ の中身。Posts は draft も含む全記事（公開日が未来のものも含む。環境による出し分けは <see cref="ForEnvironment"/>）。新しい順。</summary>
public sealed record ContentSet(
    IReadOnlyList<PostSource> Posts,
    IReadOnlyDictionary<string, WorldMasterEntry> Worlds,
    IReadOnlyDictionary<string, TagMasterEntry> Tags,
    WorldStatsFile? WorldStats = null)
{
    /// <summary>アバターのマスタ（content/avatars.json）。無ければ空。</summary>
    public IReadOnlyDictionary<string, AvatarMasterEntry> Avatars { get; init; } = new Dictionary<string, AvatarMasterEntry>(StringComparer.Ordinal);

    /// <summary>服のマスタ（content/outfits.json）。無ければ空。</summary>
    public IReadOnlyDictionary<string, OutfitMasterEntry> Outfits { get; init; } = new Dictionary<string, OutfitMasterEntry>(StringComparer.Ordinal);

    /// <summary>
    /// 環境に出す記事だけにした中身。<b>どちらの環境にも、非公開（draft: true）の記事は出さない</b>。<b>本番（production）</b>は、さらに、公開日が now より後（予約公開）の記事を除く
    /// （確認用（develop）は予約公開の記事も出す）。公開・非公開は、アプリの「公開する」「非公開にする」で切り替える。
    /// now は呼び出し側が決める（時計に依存させない）。マスタは全部残す（使われていないワールド・タグは、出力側で一覧に出さない）。
    /// </summary>
    public ContentSet ForEnvironment(string env, DateTimeOffset now) => env == "production"
        ? this with { Posts = Posts.Where(p => !p.Draft && p.PublishedAt <= now).ToList() }
        : this with { Posts = Posts.Where(p => !p.Draft).ToList() };
}

/// <summary>
/// content/ を読み、形式を検証する。未知の項目・重複した項目・null・形式の誤りは、まとめてエラーにする。
///   content/posts/*.json … 記事（写真・ワールド・キャプション。撮影者・写っている人は持たない。未知の項目はエラー）
///   content/worlds.json … ワールドのマスタ（{ "wrld_…": { "name", "aliases" } }）。記事の world.id はここにあること
///   content/tags.json … タグのマスタ（{ "slug": { "name", "description" } }）。記事の tags はここにあること
///   content/world-stats.json … VRChat から取ったワールドの情報（省略可。キーは worlds.json にあること）
/// </summary>
public static partial class ContentLoader
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // 綴りの間違いを黙って捨てない（項目を足すときは Shared のモデルに足す）。入れ子の world / マスタの中も同じ
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            // 同じ項目（辞書のキーを含む）を 2 回書いたとき（コピペのミス）は、後勝ちにせずエラーにする
            AllowDuplicateProperties = false,
        };
        // 日時はオフセット付き（Z か ±hh:mm）だけ。オフセットが無いと、実行する PC のタイムゾーンで解釈が変わる
        options.Converters.Add(new StrictOffsetConverter());
        return options;
    }

    /// <summary>オフセット付きの ISO 8601 だけを読む日時のコンバーター。</summary>
    private sealed class StrictOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            if (text is null || !TryParseDate(text, out var value))
                throw new JsonException("日時はオフセット付きの ISO 8601 の文字列にしてください（例: 2026-10-24T20:00:00+09:00）");
            return value;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex DatePattern();

    /// <summary>オフセット付きの ISO 8601 の日時を読む（オフセットが無い・形が違う文字列は false）。</summary>
    public static bool TryParseDate(string? text, out DateTimeOffset value)
    {
        value = default;
        return text is not null && DatePattern().IsMatch(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    /// <summary>content/ を読んで検証する。問題があれば <see cref="ContentException"/>（全部まとめて）。</summary>
    public static ContentSet Load(string contentDir)
    {
        var problems = new List<string>();
        var worlds = LoadWorlds(Path.Combine(contentDir, "worlds.json"), problems);
        var tags = LoadTags(Path.Combine(contentDir, "tags.json"), problems);
        var outfits = LoadOutfits(Path.Combine(contentDir, "outfits.json"), problems);
        var avatars = LoadAvatars(Path.Combine(contentDir, "avatars.json"), outfits, problems);
        var posts = LoadPosts(Path.Combine(contentDir, "posts"), worlds, tags, avatars, outfits, problems);
        var stats = LoadWorldStats(Path.Combine(contentDir, "world-stats.json"), worlds, problems);
        if (problems.Count > 0) throw new ContentException(problems);
        return new ContentSet(
            posts.OrderByDescending(p => p.PublishedAt).ThenBy(p => p.Slug, StringComparer.Ordinal).ToList(),
            worlds,
            tags,
            stats)
        {
            Avatars = avatars,
            Outfits = outfits,
        };
    }

    private static WorldStatsFile? LoadWorldStats(string file, Dictionary<string, WorldMasterEntry> worlds, List<string> problems)
    {
        if (!File.Exists(file)) return null;
        var parsed = Read<WorldStatsFile>(file, "world-stats.json", problems);
        if (parsed is null) return null;
        if (!TryParseDate(parsed.FetchedAt, out _)) problems.Add("world-stats.json: fetchedAt が日時ではありません（ISO 8601・オフセット付き）");
        foreach (var (id, s) in parsed.Worlds)
        {
            if (!worlds.ContainsKey(id)) problems.Add($"world-stats.json: {id} が worlds.json にありません");
            if (s.Capacity < 0 || s.RecommendedCapacity < 0 || s.Visits < 0 || s.Favorites < 0)
                problems.Add($"world-stats.json: {id} に負の数があります");
            if (s.AuthorName.Length > 64) problems.Add($"world-stats.json: {id} の authorName が長すぎます");
            if (s.Description.Length > 2000) problems.Add($"world-stats.json: {id} の description が長すぎます");
            if (s.Tags.Count > 20 || s.Tags.Any(t => t.Length is 0 or > 64)) problems.Add($"world-stats.json: {id} の tags が不正です");
            foreach (var (name, value) in new[] { ("publicationDate", s.PublicationDate), ("updatedAt", s.UpdatedAt) })
                if (value.Length > 0 && !TryParseDate(value, out _)) problems.Add($"world-stats.json: {id} の {name} が日時ではありません");
            // 人名を出さない決まりの関門（VRChat のユーザー ID は出さない）。作者の表示名は、ワールドの作者へのクレジットとして出してよい
            if (s.Description.Contains("usr_", StringComparison.Ordinal) || s.AuthorName.Contains("usr_", StringComparison.Ordinal))
                problems.Add($"world-stats.json: {id} に VRChat のユーザー ID（usr_…）があります");
        }
        return parsed;
    }

    /// <summary>写真の前の文章・おわりの文章の長さの上限（文字）。</summary>
    public const int MaxTextLength = 10000;

    private static T? Read<T>(string file, string name, List<string> problems) where T : class
    {
        try
        {
            var text = File.ReadAllText(file);
            using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            // 項目の null は、省略との区別がつかず、のちの検査から漏れる。形が決まっているところでは null を許さない
            var nulls = NullPaths(doc.RootElement, "$").ToList();
            foreach (var path in nulls) problems.Add($"{name}: {path} が null です（項目ごと省略してください）");
            if (nulls.Count > 0) return null;
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException)
        {
            // スタックトレースで落とさず、ファイル名つきの 1 行にする（値の型の違い・重複した項目・未知の項目など）
            problems.Add($"{name}: JSON を読めません（{e.Message}）");
            return null;
        }
    }

    /// <summary>null の項目・要素の場所。記事の updatedAt / summary / cover の null だけは「無し」として許す（計画書の例）。</summary>
    private static IEnumerable<string> NullPaths(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in element.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Null)
                {
                    if (!(path == "$" && p.Name is "updatedAt" or "summary" or "cover")) yield return $"{path}.{p.Name}";
                    continue;
                }
                foreach (var inner in NullPaths(p.Value, $"{path}.{p.Name}")) yield return inner;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) yield return $"{path}[{i}]";
                else foreach (var inner in NullPaths(item, $"{path}[{i}]")) yield return inner;
                i++;
            }
        }
    }

    private static Dictionary<string, WorldMasterEntry> LoadWorlds(string file, List<string> problems)
    {
        var result = new Dictionary<string, WorldMasterEntry>(StringComparer.Ordinal);
        if (!File.Exists(file)) return result;
        var parsed = Read<Dictionary<string, WorldMasterEntry>>(file, "worlds.json", problems);
        if (parsed is null) return result;
        foreach (var (id, entry) in parsed)
        {
            if (!WorldId.IsValid(id)) problems.Add($"worlds.json: ワールド ID の形式が不正です（wrld_ + 小文字の UUID）: \"{id}\"");
            if (string.IsNullOrWhiteSpace(entry.Name)) problems.Add($"worlds.json: {id} の name が空です");
            else if (DisplayName.Problem(entry.Name) is { } nameProblem) problems.Add($"worlds.json: {id} の name: {nameProblem}");
            if (entry.Aliases.Any(a => string.IsNullOrWhiteSpace(a))) problems.Add($"worlds.json: {id} の aliases に空の名前があります");
            foreach (var alias in entry.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                if (DisplayName.Problem(alias) is { } aliasProblem) problems.Add($"worlds.json: {id} の aliases（{alias.Length} 文字）: {aliasProblem}");
            }
            if (entry.NameAt is not null && !DateTimeOffset.TryParse(entry.NameAt, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                problems.Add($"worlds.json: {id} の nameAt が日時ではありません（ISO 8601・オフセット付き）");
            if (entry.Image is { } image && (!VrchatLinks.IsWorldImageKey(id, image.Key) || image.W <= 0 || image.H <= 0))
                problems.Add($"worlds.json: {id} の image が不正です（key は worlds/{{uuid}}/file_…_{{版}}、w・h は正の数）");
            result[id] = entry;
        }
        return result;
    }

    private static Dictionary<string, TagMasterEntry> LoadTags(string file, List<string> problems)
    {
        var result = new Dictionary<string, TagMasterEntry>(StringComparer.Ordinal);
        if (!File.Exists(file)) return result;
        var parsed = Read<Dictionary<string, TagMasterEntry>>(file, "tags.json", problems);
        if (parsed is null) return result;
        foreach (var (slug, entry) in parsed)
        {
            if (!SiteRules.IsValidSlug(slug)) problems.Add($"tags.json: タグの slug が不正です（小文字・数字・ハイフンだけ）: \"{slug}\"");
            if (string.IsNullOrWhiteSpace(entry.Name)) problems.Add($"tags.json: {slug} の name が空です");
            result[slug] = entry;
        }
        return result;
    }

    /// <summary>服の id（小文字・数字・ハイフン。先頭は英数字。40 文字まで）。</summary>
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex MasterIdPattern();

    /// <summary>https:// だけの URL（空白を含まない。500 文字まで）。</summary>
    private static bool IsHttpsUrl(string url) =>
        url.Length <= 500 && !url.Any(char.IsWhiteSpace) && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static Dictionary<string, OutfitMasterEntry> LoadOutfits(string file, List<string> problems)
    {
        var result = new Dictionary<string, OutfitMasterEntry>(StringComparer.Ordinal);
        if (!File.Exists(file)) return result;
        var parsed = Read<Dictionary<string, OutfitMasterEntry>>(file, "outfits.json", problems);
        if (parsed is null) return result;
        foreach (var (id, entry) in parsed)
        {
            if (!MasterIdPattern().IsMatch(id)) problems.Add($"outfits.json: 服の id の形式が不正です（小文字・数字・ハイフン）: \"{id}\"");
            if (string.IsNullOrWhiteSpace(entry.Name)) problems.Add($"outfits.json: {id} の name が空です");
            else if (DisplayName.Problem(entry.Name) is { } nameProblem) problems.Add($"outfits.json: {id} の name: {nameProblem}");
            if (entry.Shop is not null && DisplayName.Problem(entry.Shop) is { } shopProblem) problems.Add($"outfits.json: {id} の shop: {shopProblem}");
            if (entry.Url is not null && !IsHttpsUrl(entry.Url)) problems.Add($"outfits.json: {id} の url は https:// で始まる URL（空白なし）だけです: \"{entry.Url}\"");
            if (entry.Thumbnail is not null && !IsHttpsUrl(entry.Thumbnail)) problems.Add($"outfits.json: {id} の thumbnail は https:// で始まる URL（空白なし）だけです: \"{entry.Thumbnail}\"");
            if (entry.Description is { Length: > 3000 }) problems.Add($"outfits.json: {id} の description が長すぎます（3000 文字まで）");
            foreach (var tag in entry.Tags ?? []) if (string.IsNullOrWhiteSpace(tag) || tag.Length > 60 || DisplayName.Problem(tag) is not null) problems.Add($"outfits.json: {id} の tags に使えない値があります: \"{tag}\"");
            foreach (var (field, value) in new[] { ("publishedAt", entry.PublishedAt), ("updatedAt", entry.UpdatedAt), ("fetchedAt", entry.FetchedAt) })
                if (value is not null && !DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)) problems.Add($"outfits.json: {id} の {field} は日時（ISO 8601）ではありません: \"{value}\"");
            result[id] = entry;
        }
        return result;
    }

    private static Dictionary<string, AvatarMasterEntry> LoadAvatars(string file, Dictionary<string, OutfitMasterEntry> outfits, List<string> problems)
    {
        var result = new Dictionary<string, AvatarMasterEntry>(StringComparer.Ordinal);
        if (!File.Exists(file)) return result;
        var parsed = Read<Dictionary<string, AvatarMasterEntry>>(file, "avatars.json", problems);
        if (parsed is null) return result;
        foreach (var (id, entry) in parsed)
        {
            if (!MasterIdPattern().IsMatch(id)) problems.Add($"avatars.json: アバターの id の形式が不正です（小文字・数字・ハイフン）: \"{id}\"");
            if (string.IsNullOrWhiteSpace(entry.Name)) problems.Add($"avatars.json: {id} の name が空です");
            else if (DisplayName.Problem(entry.Name) is { } nameProblem) problems.Add($"avatars.json: {id} の name: {nameProblem}");
            foreach (var alias in entry.Aliases)
            {
                if (DisplayName.Problem(alias) is { } aliasProblem) problems.Add($"avatars.json: {id} の aliases: {aliasProblem}");
            }
            foreach (var dup in entry.OutfitIds.GroupBy(x => x).Where(g => g.Count() > 1)) problems.Add($"avatars.json: {id} の outfitIds に同じ服が 2 回あります: {dup.Key}");
            foreach (var outfitId in entry.OutfitIds.Where(x => !outfits.ContainsKey(x)).Distinct()) problems.Add($"avatars.json: {id} の outfitIds が outfits.json にありません: \"{outfitId}\"");
            result[id] = entry;
        }
        return result;
    }

    private static List<PostSource> LoadPosts(string postsDir, Dictionary<string, WorldMasterEntry> worlds, Dictionary<string, TagMasterEntry> tags, Dictionary<string, AvatarMasterEntry> avatars, Dictionary<string, OutfitMasterEntry> outfits, List<string> problems)
    {
        var posts = new List<PostSource>();
        if (!Directory.Exists(postsDir)) return posts;
        foreach (var file in Directory.EnumerateFiles(postsDir, "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var before = problems.Count;
            var post = Read<PostSource>(file, name, problems);
            if (post is null)
            {
                if (problems.Count == before) problems.Add($"{name}: 中身が空です");
                continue;
            }
            Validate(post, name, worlds, tags, avatars, outfits, problems);
            posts.Add(post);
        }

        foreach (var dup in posts.GroupBy(p => p.Slug).Where(g => g.Key.Length > 0 && g.Count() > 1))
            problems.Add($"slug が重複しています: {dup.Key}");
        return posts;
    }

    private static void Validate(PostSource post, string fileName, Dictionary<string, WorldMasterEntry> worlds, Dictionary<string, TagMasterEntry> tags, Dictionary<string, AvatarMasterEntry> avatars, Dictionary<string, OutfitMasterEntry> outfits, List<string> problems)
    {
        void Problem(string text) => problems.Add($"{fileName}: {text}");

        if (post.Schema == 0) Problem("schema がありません（1 を書いてください）");
        else if (post.Schema != 1) Problem($"schema は 1 だけです（{post.Schema}）");
        if (!SiteRules.IsValidSlug(post.Slug)) Problem($"slug が不正です（小文字・数字・ハイフンだけ）: \"{post.Slug}\"");
        else if (!string.Equals(Path.GetFileNameWithoutExtension(fileName), post.Slug, StringComparison.Ordinal))
            Problem($"ファイル名と slug が一致しません（slug: {post.Slug}）");
        if (string.IsNullOrWhiteSpace(post.Title)) Problem("title が空です");
        if (post.PublishedAt == default) Problem("publishedAt がありません（オフセット付きの日時。例: 2026-10-24T20:00:00+09:00）");
        foreach (var tag in post.Tags)
        {
            if (!SiteRules.IsValidSlug(tag)) Problem($"タグが不正です（小文字・数字・ハイフンだけ）: \"{tag}\"");
            else if (!tags.ContainsKey(tag)) Problem($"タグ \"{tag}\" が content/tags.json にありません（登録してから使ってください）");
        }
        foreach (var dup in post.Tags.GroupBy(t => t).Where(g => g.Count() > 1)) Problem($"タグが重複しています: {dup.Key}");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < post.Photos.Count; i++)
        {
            var photo = post.Photos[i];
            var label = $"photos[{i}]";
            if (!SiteRules.IsValidPhotoKey(photo.Key)) Problem($"{label} の key の形式が不正です: \"{photo.Key}\"");
            else
            {
                if (!keys.Add(photo.Key)) Problem($"写真の key が重複しています: {photo.Key}");
                if (!SiteRules.PhotoKeyMonthMatches(photo.Key)) Problem($"{label} の key の月（yyyy-mm/）とファイル名の日付が食い違っています: {photo.Key}");
                else if (SiteRules.PhotoKeyTime(photo.Key) < SiteRules.EarliestPhotoDate)
                    Problem($"{label}: 2025 年 11 月より前の写真は載せません: {photo.Key}");
            }
            if (photo.W < 1 || photo.H < 1) Problem($"{label} の w / h が不正です");
            if (photo.Color is not null && !Regex.IsMatch(photo.Color, "^[0-9a-f]{6}$"))
                Problem($"{label} の color は 6 桁の小文字 16 進です: \"{photo.Color}\"");
            if (photo.TakenAt is not null) ValidateTakenAt(photo, label, Problem);
            // 公開データだけの項目は、元データに書かない（書いても使われず、誤解のもとになる）
            if (photo.TextHtml is not null) Problem($"{label} に textHtml は書けません（文章は text に Markdown で書きます）");
            if (photo.Wear is not null) Problem($"{label} に wear は書けません（服は avatar・outfits で指定します）");
            if (photo.Text is { Length: > MaxTextLength }) Problem($"{label} の text が長すぎます（{MaxTextLength} 文字まで）");
            if (photo.Avatar is { } avatarId && !avatars.ContainsKey(avatarId)) Problem($"{label} の avatar が content/avatars.json にありません: \"{avatarId}\"");
            if (photo.Outfits is { } outfitIds)
            {
                foreach (var dup in outfitIds.GroupBy(x => x).Where(g => g.Count() > 1)) Problem($"{label} の outfits に同じ服が 2 回あります: {dup.Key}");
                foreach (var outfitId in outfitIds.Where(x => !outfits.ContainsKey(x)).Distinct()) Problem($"{label} の outfits が content/outfits.json にありません: \"{outfitId}\"");
            }
            if (photo.World is { } world)
            {
                if (!WorldId.IsValid(world.Id)) Problem($"{label} の world.id の形式が不正です（wrld_ + 小文字の UUID）: \"{world.Id}\"");
                else if (!worlds.ContainsKey(world.Id)) Problem($"{label} の world.id が content/worlds.json にありません（登録してから使ってください）: {world.Id}");
                if (world.Name is not null && string.IsNullOrWhiteSpace(world.Name)) Problem($"{label} の world.name が空です（省略するか名前を書いてください）");
            }
        }
        if (post.Cover is not null && !keys.Contains(post.Cover)) Problem($"cover が photos にありません: {post.Cover}");
        if (post.Afterword is { Length: > MaxTextLength }) Problem($"afterword が長すぎます（{MaxTextLength} 文字まで）");
    }

    private static void ValidateTakenAt(PhotoInfo photo, string label, Action<string> problem)
    {
        if (!TryParseDate(photo.TakenAt, out var taken))
        {
            problem($"{label} の takenAt はオフセット付きの ISO 8601 です: \"{photo.TakenAt}\"");
            return;
        }
        if (!SiteRules.IsValidPhotoKey(photo.Key) || !SiteRules.PhotoKeyMonthMatches(photo.Key)) return;
        // key の日時は撮影日時（JST、秒まで）。takenAt の JST の秒までと一致すること
        var jst = taken.ToOffset(TimeSpan.FromHours(9));
        var keyTime = SiteRules.PhotoKeyTime(photo.Key);
        if (keyTime.DateTime != new DateTime(jst.Year, jst.Month, jst.Day, jst.Hour, jst.Minute, jst.Second))
            problem($"{label}: key の日時と takenAt が食い違っています（key は撮影日時の JST）: {photo.Key}");
    }
}
