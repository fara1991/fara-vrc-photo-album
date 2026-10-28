using System.Globalization;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

public sealed record DraftRequest
{
    /// <summary>新しく入れる写真に付けるアバター（content/avatars.json の id）。省略は何も付けない。</summary>
    public string? Avatar { get; init; }

    /// <summary>新しく入れる写真の服（順序つき。省略はアバターの既定の服）。</summary>
    public List<string>? Outfits { get; init; }

    /// <summary>写真ごとのアバター（写真のキー → アバターの id）。ここにある写真は <see cref="Avatar"/> より優先する（デスクトップ版の写真ごとの選択）。</summary>
    public IReadOnlyDictionary<string, string>? PhotoAvatars { get; init; }

    public required string Slug { get; init; }

    /// <summary>新規の記事に必要。既存の記事では無視する。</summary>
    public string? Title { get; init; }

    /// <summary>公開日（ISO 8601 の日時。省略時は今の JST の分まで）。新規の記事だけ使う。</summary>
    public string? PublishedAt { get; init; }

    /// <summary>撮影日時がこれ以降の写真を選ぶ。</summary>
    public DateTimeOffset? Since { get; init; }

    /// <summary>キーを指定して選ぶ（Since と併用は不可）。</summary>
    public IReadOnlyList<string>? Keys { get; init; }

    /// <summary>既存の記事の末尾に、まだ入っていない写真を足す。</summary>
    public bool Append { get; init; }
}

public sealed record DraftResult(string PostPath, int Added, bool Created, IReadOnlyList<string> Messages);

/// <summary>
/// 台帳（uploaded）の写真から、記事の下書き（draft: true）の photos[] を作る（計画書 §5.5.2）。
/// 撮影日時の昇順。ワールドは content/worlds.json のマスタに無ければ追記、名前が違えば aliases に足す（name は変えない・§3.3.1）。
/// 撮影者・写っている人は持たない。書いたあとに ContentLoader で検査し、失敗したら元に戻す。
/// </summary>
public static class DraftService
{
    /// <param name="apply">false は書かずに、追加される写真と変わるワールドの一覧だけを返す（プレビュー。既定は CLI が false）。</param>
    public static DraftResult Run(ContentEditor editor, PhotoLedger ledger, DraftRequest request, DateTimeOffset now, bool apply = true)
    {
        if (!FaraVrcAlbum.SiteRules.IsValidSlug(request.Slug)) throw new ArgumentException($"slug の形式が不正です: {request.Slug}");
        if (request.Since is not null && request.Keys is not null) throw new ArgumentException("--since と --keys は同時に指定できません");
        if (request.Since is null && request.Keys is null) throw new ArgumentException("写真の選び方（--since または --keys）を指定してください");

        var selected = SelectEntries(ledger, request);
        var messages = new List<string>();
        var postPath = editor.PostPath(request.Slug);
        var exists = File.Exists(postPath);
        if (exists && !request.Append) throw new InvalidOperationException($"記事が既にあります（{request.Slug}）。末尾に足すには --append");
        if (!exists && string.IsNullOrWhiteSpace(request.Title)) throw new ArgumentException("新しい記事には --title を指定してください");

        var snapshot = new FileSnapshot();
        snapshot.Capture(postPath);
        snapshot.Capture(editor.WorldsPath);
        try
        {
            var post = exists ? ContentEditor.ReadObject(postPath) : NewPost(request, now);
            var present = (post["photos"] as JsonArray)?.Select(p => p?["key"]?.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
            var photos = post["photos"] as JsonArray ?? new JsonArray();
            post["photos"] = photos;

            var worlds = ReadWorlds(editor.WorldsPath);
            var worldsChanged = false;
            var added = 0;
            foreach (var entry in selected.Where(e => !present.Contains(e.Key)))
            {
                worldsChanged |= RegisterWorld(worlds, entry, messages);
                var node = PhotoNode(entry, worlds, messages);
                var avatar = request.PhotoAvatars?.GetValueOrDefault(entry.Key) ?? request.Avatar;
                if (avatar is not null) node["avatar"] = avatar;
                if (request.Outfits is not null) node["outfits"] = new JsonArray(request.Outfits.Select(o => (JsonNode)o).ToArray());
                photos.Add(node);
                added++;
            }

            if (!apply) return new DraftResult(postPath, added, !exists, messages);

            if (worldsChanged) ContentEditor.WriteNode(editor.WorldsPath, worlds);
            ContentEditor.WriteNode(postPath, post);
            editor.ValidateOrThrow();
            return new DraftResult(postPath, added, !exists, messages);
        }
        catch
        {
            snapshot.Restore();
            throw;
        }
    }

    /// <summary>選ぶ写真（uploaded だけ。撮影日時の昇順）。</summary>
    public static IReadOnlyList<LedgerEntry> SelectEntries(PhotoLedger ledger, DraftRequest request)
    {
        var uploaded = ledger.BySha.Values.Where(e => e.State == LedgerStates.Uploaded).ToList();
        IEnumerable<LedgerEntry> chosen;
        if (request.Keys is not null)
        {
            var byKey = uploaded.ToDictionary(e => e.Key, StringComparer.Ordinal);
            var missing = request.Keys.Where(k => !byKey.ContainsKey(k)).ToList();
            if (missing.Count > 0) throw new InvalidOperationException($"台帳に uploaded で無いキー: {string.Join(", ", missing)}");
            chosen = request.Keys.Select(k => byKey[k]);
        }
        else
        {
            var since = request.Since!.Value;
            chosen = uploaded.Where(e => DateTimeOffset.Parse(e.TakenAt, CultureInfo.InvariantCulture) >= since);
        }
        return chosen.OrderBy(e => e.TakenAt, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
    }

    private static JsonObject NewPost(DraftRequest request, DateTimeOffset now)
    {
        var published = request.PublishedAt ?? FormatMinute(now);
        return new JsonObject
        {
            ["schema"] = 1,
            ["slug"] = request.Slug,
            ["title"] = request.Title!.Trim(),
            ["publishedAt"] = published,
            ["draft"] = true,
            ["noindex"] = false,
            ["tags"] = new JsonArray(),
            ["body"] = "",
            ["photos"] = new JsonArray(),
        };
    }

    private static string FormatMinute(DateTimeOffset now)
    {
        var jst = now.ToOffset(PhotoKeys.Jst);
        var minute = new DateTimeOffset(jst.Ticks - jst.Ticks % TimeSpan.TicksPerMinute, PhotoKeys.Jst);
        return minute.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static JsonObject ReadWorlds(string path) =>
        File.Exists(path) ? ContentEditor.ReadObject(path) : new JsonObject();

    /// <summary>
    /// ワールドのマスタに登録する。無ければ追記。名前が違うとき: その写真の撮影日時が、マスタの nameAt（名前を観測した日時）以降なら
    /// name を新しい名前に変え、旧名を aliases に残し、nameAt を更新する。古い写真の名前では name を変えず、aliases に足すだけ。
    /// nameAt が無い既存データは、どの写真の名前でも更新してよい。変わったら true（プレビューでも同じ表示）。
    /// </summary>
    private static bool RegisterWorld(JsonObject worlds, LedgerEntry entry, List<string> messages)
    {
        if (entry.World is null) return false;
        var id = entry.World.Id;
        // ワールド名は表示名の共通の規則で整える（制御・書式文字を除く・長すぎるものは空）
        var name = DisplayName.Clean(entry.World.Name) ?? "";
        if (worlds[id] is not JsonObject master)
        {
            if (name.Length == 0)
            {
                messages.Add($"{entry.Key}: ワールド名が XMP に無いので、ワールドは空にしました（マスタに追加できません）");
                return false;
            }
            worlds[id] = new JsonObject { ["name"] = name, ["aliases"] = new JsonArray(), ["nameAt"] = observedAt(entry) };
            messages.Add($"ワールドを追加: {id}");
            return true;
        }

        if (name.Length == 0) return false;
        var currentName = master["name"]?.GetValue<string>() ?? "";
        var aliases = master["aliases"] as JsonArray ?? new JsonArray();
        master["aliases"] = aliases;
        var observed = DateTimeOffset.Parse(entry.TakenAt, CultureInfo.InvariantCulture);
        var nameAt = master["nameAt"]?.GetValue<string>();
        // 観測日時（nameAt）より厳密に新しい写真だけが名前（と観測日時）を進める。nameAt が無い既存データは進めてよい
        var newer = nameAt is null || !DateTimeOffset.TryParse(nameAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var known) || observed > known;

        if (name == currentName)
        {
            // 同じ名前をさらに新しい写真で観測したときは、観測日時だけを進める
            if (!newer) return false;
            master["nameAt"] = observedAt(entry);
            messages.Add($"ワールド {id} の名前の観測日時を進めました（撮影 {observed:yyyy-MM-dd}。名前は変えません）");
            return true;
        }
        if (newer)
        {
            // 新しい写真の名前: name を更新し、旧名は aliases に残す。新しい名前が aliases に既にあれば外す（name と aliases を重ねない）
            RemoveAlias(aliases, name);
            if (currentName.Length > 0 && !aliases.Any(a => a?.GetValue<string>() == currentName)) aliases.Add(currentName);
            master["name"] = name;
            master["nameAt"] = observedAt(entry);
            messages.Add($"ワールド {id} の名前を更新: 「{currentName}」→「{name}」（撮影 {observed:yyyy-MM-dd}。旧名は aliases に残します）");
            return true;
        }
        if (aliases.Any(a => a?.GetValue<string>() == name)) return false;
        aliases.Add(name);
        messages.Add($"ワールド {id} の名前の違いを aliases に追加（撮影 {observed:yyyy-MM-dd} は古いため name は変えません）");
        return true;
    }

    private static void RemoveAlias(JsonArray aliases, string name)
    {
        for (var i = aliases.Count - 1; i >= 0; i--)
        {
            if (aliases[i]?.GetValue<string>() == name) aliases.RemoveAt(i);
        }
    }

    private static string observedAt(LedgerEntry entry) =>
        DateTimeOffset.Parse(entry.TakenAt, CultureInfo.InvariantCulture).ToString("O", CultureInfo.InvariantCulture);

    private static JsonObject PhotoNode(LedgerEntry entry, JsonObject worlds, List<string> messages)
    {
        var photo = new JsonObject
        {
            ["key"] = entry.Key,
            ["w"] = entry.W,
            ["h"] = entry.H,
            ["takenAt"] = entry.TakenAt,
            ["color"] = entry.Color,
        };
        if (entry.World is not null && worlds[entry.World.Id] is JsonObject master)
        {
            photo["world"] = new JsonObject { ["id"] = entry.World.Id, ["name"] = master["name"]?.GetValue<string>() ?? "" };
        }
        else if (entry.World is not null)
        {
            messages.Add($"{entry.Key}: ワールドがマスタに無いため、ワールドは空にしました");
        }
        return photo;
    }
}
