using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Build;

/// <summary>
/// 環境に出す記事（draft と、本番での予約公開を除いたもの）から、一覧・アーカイブ・タグ・ワールドを導く。
/// data/*.json・静的 HTML・フィードが、同じ集計を使う（食い違わない）。
/// </summary>
internal sealed class SiteModel
{
    public SiteSettings Settings { get; }
    public ContentSet Content { get; }

    /// <summary>公開する記事（新しい順）。</summary>
    public IReadOnlyList<PostSource> Posts => Content.Posts;

    public IReadOnlyList<PostSummary> Summaries { get; }

    /// <summary>公開月（JST）ごとの記事。新しい月から。</summary>
    public IReadOnlyList<(int Year, int Month, IReadOnlyList<PostSource> Posts)> Months { get; }

    /// <summary>タグの slug → 記事（新しい順）。記事が 1 件以上あるタグだけ。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PostSource>> PostsByTag { get; }

    /// <summary>ワールド ID → 写真（新しい記事の順、記事の中は並びのまま）。写真が 1 枚以上あるワールドだけ。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(PostSource Post, PhotoInfo Photo)>> PhotosByWorld { get; }

    /// <summary>服の id → 写真（新しい記事の順、記事の中は並びのまま）。その服を着た写真が 1 枚以上ある服だけ。</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(PostSource Post, PhotoInfo Photo)>> PhotosByOutfit { get; }

    public SiteModel(SiteSettings settings, ContentSet content)
    {
        Settings = settings;
        Content = content;
        Summaries = content.Posts.Select(ToSummary).ToList();

        Months = content.Posts
            .GroupBy(p => SiteRules.MonthOf(p.PublishedAt))
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Select(g => (g.Key.Year, g.Key.Month, (IReadOnlyList<PostSource>)g.ToList()))
            .ToList();

        PostsByTag = content.Posts
            .SelectMany(p => p.Tags.Select(t => (Tag: t, Post: p)))
            .GroupBy(x => x.Tag, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PostSource>)g.Select(x => x.Post).ToList(), StringComparer.Ordinal);

        PhotosByWorld = content.Posts
            .SelectMany(p => p.Photos.Where(ph => ph.World is not null).Select(ph => (Post: p, Photo: ph)))
            .GroupBy(x => x.Photo.World!.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(PostSource, PhotoInfo)>)g.ToList(), StringComparer.Ordinal);

        PhotosByOutfit = content.Posts
            .SelectMany(p => p.Photos.SelectMany(ph => (OutfitRules.Resolve(ph, content.Avatars, content.Outfits) ?? []).Select(o => (OutfitId: o.Id, Post: p, Photo: ph))))
            .GroupBy(x => x.OutfitId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(PostSource, PhotoInfo)>)g.Select(x => (x.Post, x.Photo)).ToList(), StringComparer.Ordinal);
    }

    /// <summary>服のマスタ（その服を着た写真があるものだけが、一覧・ページになる）。</summary>
    public OutfitMasterEntry OutfitMaster(string id) => Content.Outfits[id];

    /// <summary>画面に出すワールド名（マスタの name）。</summary>
    public string WorldName(string id) => Content.Worlds[id].Name;

    public string TagName(string slug) => Content.Tags[slug].Name;

    /// <summary>タグの slug → 表示名（カードのタグ）。</summary>
    public IReadOnlyDictionary<string, string> TagNameMap() => Content.Tags.ToDictionary(t => t.Key, t => t.Value.Name, StringComparer.Ordinal);

    /// <summary>
    /// 公開する写真の情報。ワールド名はマスタの名前にそろえる（記事の JSON の name は撮影時点の控え）。撮影者・写っている人の項目は持たない。
    /// </summary>
    public PhotoInfo ToPublic(PhotoInfo photo) => new()
    {
        Key = photo.Key,
        W = photo.W,
        H = photo.H,
        TakenAt = photo.TakenAt,
        Color = photo.Color,
        Caption = photo.Caption,
        TextHtml = string.IsNullOrWhiteSpace(photo.Text) ? null : MarkdownRenderer.ToHtml(photo.Text),
        World = photo.World is null ? null : new WorldRef { Id = photo.World.Id, Name = WorldName(photo.World.Id) },
        Wear = OutfitRules.Resolve(photo, Content.Avatars, Content.Outfits),
    };

    /// <summary>一覧のカードに要る分。カバーは寸法と色だけ（一覧の JSON を小さく保つ）。</summary>
    public PostSummary ToSummary(PostSource post)
    {
        var cover = CoverOf(post);
        return new PostSummary
        {
            Slug = post.Slug,
            Title = post.Title,
            PublishedAt = post.PublishedAt,
            Summary = SummaryOf(post),
            Cover = cover is null ? null : new PhotoInfo { Key = cover.Key, W = cover.W, H = cover.H, Color = cover.Color },
            Tags = post.Tags.ToList(),
            PhotoCount = post.Photos.Count,
        };
    }

    /// <summary>関連する記事の最大件数。</summary>
    public const int MaxRelated = 6;

    public PostPage ToPage(PostSource post) => new()
    {
        Slug = post.Slug,
        Title = post.Title,
        PublishedAt = post.PublishedAt,
        UpdatedAt = post.UpdatedAt,
        Summary = SummaryOf(post),
        Cover = CoverOf(post)?.Key,
        Tags = post.Tags.Select(t => new TagInfo { Slug = t, Name = TagName(t) }).ToList(),
        BodyHtml = MarkdownRenderer.ToHtml(post.Body),
        Photos = post.Photos.Select(ToPublic).ToList(),
        AfterwordHtml = string.IsNullOrWhiteSpace(post.Afterword) ? null : MarkdownRenderer.ToHtml(post.Afterword),
        Older = LinkOf(NeighbourOf(post, +1)),
        Newer = LinkOf(NeighbourOf(post, -1)),
        Related = RelatedOf(post) is { Count: > 0 } related ? related : null,
    };

    // 前後の記事。環境に出す記事の並び（新しい順）だけから決める（本番では予約公開の記事は前後に現れない）
    private PostSource? NeighbourOf(PostSource post, int offset)
    {
        var index = Content.Posts.ToList().IndexOf(post) + offset;
        return index >= 0 && index < Content.Posts.Count ? Content.Posts[index] : null;
    }

    private static PostLink? LinkOf(PostSource? post) => post is null ? null : new PostLink { Slug = post.Slug, Title = post.Title };

    /// <summary>関連する記事: 同じタグ・同じワールドの数が多い順（同数は新しい順）。最大 MaxRelated 件。</summary>
    private List<PostSummary> RelatedOf(PostSource post)
    {
        var tags = post.Tags.ToHashSet(StringComparer.Ordinal);
        var worlds = post.Photos.Where(p => p.World is not null).Select(p => p.World!.Id).ToHashSet(StringComparer.Ordinal);
        return Content.Posts
            .Where(other => !ReferenceEquals(other, post))
            .Select(other => (Post: other, Score: other.Tags.Count(tags.Contains)
                + other.Photos.Where(p => p.World is not null).Select(p => p.World!.Id).Distinct(StringComparer.Ordinal).Count(worlds.Contains)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Post.PublishedAt).ThenBy(x => x.Post.Slug, StringComparer.Ordinal)
            .Take(MaxRelated)
            .Select(x => ToSummary(x.Post))
            .ToList();
    }

    public static PhotoInfo? CoverOf(PostSource post) =>
        post.Photos.FirstOrDefault(p => p.Key == post.Cover) ?? post.Photos.FirstOrDefault();

    public static string SummaryOf(PostSource post) =>
        string.IsNullOrWhiteSpace(post.Summary) ? MarkdownRenderer.PlainSummary(post.Body) : post.Summary.Trim();

    /// <summary>公開するタグの一覧（記事が 1 件以上あるもの。記事の多い順、同数は slug 順）。</summary>
    public List<TagSummary> TagSummaries() => PostsByTag
        .Select(t => new TagSummary { Slug = t.Key, Name = TagName(t.Key), Description = Content.Tags[t.Key].Description, PostCount = t.Value.Count })
        .OrderByDescending(t => t.PostCount).ThenBy(t => t.Slug, StringComparer.Ordinal)
        .ToList();

    /// <summary>公開するワールドの一覧（写真が 1 枚以上あるもの。写真の多い順、同数は名前順）。代表は最新の記事の最初の写真。</summary>
    private static PhotoRef ToRef(PostSource post, PhotoInfo photo) => new()
    {
        Key = photo.Key, W = photo.W, H = photo.H, Color = photo.Color, Slug = post.Slug, TakenAt = photo.TakenAt,
    };

    public List<WorldSummary> WorldSummaries() => PhotosByWorld
        .Select(w => new WorldSummary
        {
            Id = w.Key,
            Name = WorldName(w.Key),
            PhotoCount = w.Value.Count,
            Cover = ToRef(w.Value[0].Post, w.Value[0].Photo),
            Image = Content.Worlds[w.Key].Image,
        })
        .OrderByDescending(w => w.PhotoCount).ThenBy(w => w.Name, StringComparer.Ordinal).ThenBy(w => w.Id, StringComparer.Ordinal)
        .ToList();

    /// <summary>VRChat から取ったワールドの情報（無ければ null）。</summary>
    public WorldStats? WorldStatsOf(string worldId) => Content.WorldStats?.Worlds.GetValueOrDefault(worldId);

    public string? WorldStatsFetchedAt => Content.WorldStats?.FetchedAt;

    public List<PhotoRef> WorldPhotos(string worldId) =>
        PhotosByWorld[worldId].Select(x => ToRef(x.Post, x.Photo)).ToList();

    public List<OutfitSummary> OutfitSummaries() => PhotosByOutfit
        .Select(o => new OutfitSummary
        {
            Id = o.Key,
            Name = Content.Outfits[o.Key].Name,
            Shop = Content.Outfits[o.Key].Shop,
            PhotoCount = o.Value.Count,
            Thumbnail = Content.Outfits[o.Key].Thumbnail,
        })
        .OrderByDescending(o => o.PhotoCount).ThenBy(o => o.Name, StringComparer.Ordinal).ThenBy(o => o.Id, StringComparer.Ordinal)
        .ToList();

    public List<PhotoRef> OutfitPhotos(string outfitId) =>
        PhotosByOutfit[outfitId].Select(x => ToRef(x.Post, x.Photo)).ToList();

    /// <summary>月別の件数（data/archive.json）。年 → 月（2 桁）→ 件数。</summary>
    public Dictionary<string, Dictionary<string, int>> ArchiveCounts()
    {
        var result = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (year, month, posts) in Months)
        {
            var y = year.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
            if (!result.TryGetValue(y, out var months)) result[y] = months = new Dictionary<string, int>(StringComparer.Ordinal);
            months[month.ToString("00", System.Globalization.CultureInfo.InvariantCulture)] = posts.Count;
        }
        return result;
    }

    /// <summary>記事の集まりが noindex か（空、または全部が noindex の記事）。一覧ページの noindex の判断。</summary>
    public static bool AllNoindex(IEnumerable<PostSource> posts)
    {
        // 空の一覧も noindex（検索に出す中身が無い）
        return posts.All(p => p.Noindex);
    }

    /// <summary>OGP 用の画像のキー: 記事の集まりのうち最初の記事のカバー（_og.jpg を作るのはカバーだけ）。無ければ null。</summary>
    public static string? OgCoverKey(IEnumerable<PostSource> posts) => posts.Select(p => CoverOf(p)?.Key).FirstOrDefault(k => k is not null);
}
