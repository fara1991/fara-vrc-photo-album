using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Pages;

namespace FaraVrcAlbum.Build;

/// <summary>
/// kit の build-pages に渡す pages.json（ページごとの path・title・description・本文の HTML）を作る。
/// 本文の先頭の要素に class="entry-static" を付ける（site.json の bodyRootClass。kit が検査する。読み込み表示の JS がこの class を見る）。
/// 題名・説明の文言は、アプリ側（Web の PageTitle）と同じ規則にする。
/// 静的 HTML には人の名前を入れない（撮影者・写っている人は持たない）。
/// 一覧系のページ（一覧・アーカイブ・タグ・ワールド）は、載せる記事がすべて noindex（または 0 件）なら noindex にする。
/// </summary>
internal static class PageCatalog
{
    private const int MaxDescription = 150;

    public static string BuildPagesJson(SiteModel model, PageRenderer renderer)
    {
        var settings = model.Settings;
        var banner = renderer.Render<SiteBanner>(new Dictionary<string, object?>());               // 全ページの本文の最初（静的 HTML にも画像を入れる）
        var perPage = settings.PostsPerPage;
        var pages = new List<JsonObject>();
        // /…/page/1 は 1 ページ目と同じ内容なので 301 で 1 ページ目へ（/page/1 → / と同じ扱い。各ページは自分自身が canonical）
        var redirects = new List<JsonObject> { new() { ["from"] = "/page/1", ["to"] = "/", ["status"] = 301 } };
        void PageOneRedirect(string firstPagePath) => redirects.Add(new JsonObject { ["from"] = firstPagePath + "/page/1", ["to"] = firstPagePath, ["status"] = 301 });
        var baseUrl = settings.ImageBaseUrl;                       // 画面の画像（確認用も本番と同じ R2）
        var publicBase = settings.PublicImageBaseUrl;               // og:image・JSON-LD（本番の R2 の URL）

        var tagNames = model.TagNameMap();

        string Render<T>(Dictionary<string, object?> parameters) where T : Microsoft.AspNetCore.Components.IComponent => renderer.Render<T>(parameters);
        string Suffix(int page) => page <= 1 ? "" : $"（{page} ページ目）";

        // 一覧の 1 ページ分を pages に足す（題名に「ページ目」、noindex と OGP 画像は載せる記事から決める）
        void Listing(string path, string title, string description, string body, IReadOnlyList<PostSource> posts)
        {
            var node = Page(banner, path, title, description, body);
            if (SiteModel.AllNoindex(posts)) node["noindex"] = true;
            if (SiteModel.OgCoverKey(posts) is { } key) node["ogImage"] = SiteRules.OgImageUrl(publicBase, key);
            pages.Add(node);
        }

        // 新着の一覧（/ と /page/2 以降）
        var summaries = model.Summaries.ToList();
        var pageCount = SiteRules.PageCount(summaries.Count, perPage);
        for (var page = 1; page <= pageCount; page++)
        {
            var body = Render<HomeView>(new Dictionary<string, object?>
            {
                ["SiteName"] = settings.SiteName,
                ["Posts"] = SiteRules.Slice(summaries, page, perPage),
                ["BaseUrl"] = baseUrl,
                ["TagNames"] = tagNames,
                ["Page"] = page,
                ["PageCount"] = pageCount,
            });
            Listing(SiteRules.HomePath(page),
                page == 1 ? settings.SiteName : $"新着の記事{Suffix(page)} | {settings.SiteName}",
                "VRChat で撮った写真を、記事にして並べています。", body, SiteRules.Slice(model.Posts, page, perPage));
        }

        // 記事
        foreach (var post in model.Posts)
        {
            var page = model.ToPage(post);
            var body = Render<PostArticleView>(new Dictionary<string, object?> { ["Post"] = page, ["BaseUrl"] = baseUrl, ["TagNames"] = tagNames });
            var node = Page(banner, SiteRules.PostPath(post.Slug), $"{post.Title} | {settings.SiteName}", Shorten(page.Summary, "VRChat で撮った写真の記事です。"), body);
            node["ogType"] = "article";
            node["lastmod"] = SiteRules.FormatIsoDate(post.UpdatedAt ?? post.PublishedAt);
            if (post.Noindex) node["noindex"] = true;
            if (page.Cover is not null) node["ogImage"] = SiteRules.OgImageUrl(publicBase, page.Cover);
            node["jsonLd"] = BlogPosting(settings, post, page);
            pages.Add(node);
        }

        // アーカイブ（公開月 = JST。/archive/{yyyy}/{MM} と /page/{n}）
        foreach (var (year, month, posts) in model.Months)
        {
            PageOneRedirect(SiteRules.ArchivePath(year, month));
            var monthSummaries = posts.Select(model.ToSummary).ToList();
            var count = SiteRules.PageCount(monthSummaries.Count, perPage);
            for (var page = 1; page <= count; page++)
            {
                var y = year; var m = month;
                var heading = $"{year}年{month}月の記事";
                var body = Render<ListPageView>(new Dictionary<string, object?>
                {
                    ["Heading"] = heading,
                    ["Lead"] = $"{year}年{month}月に公開した記事の一覧です（{monthSummaries.Count} 件）。",
                    ["Posts"] = SiteRules.Slice(monthSummaries, page, perPage),
                    ["BaseUrl"] = baseUrl,
                    ["TagNames"] = tagNames,
                    ["Page"] = page,
                    ["PageCount"] = count,
                    ["HrefFor"] = (Func<int, string>)(n => SiteRules.ArchiveHref(y, m, n)),
                });
                Listing(SiteRules.ArchivePath(year, month, page), $"{heading}{Suffix(page)} | {settings.SiteName}",
                    $"{year}年{month}月に公開した写真の記事の一覧です。", body, SiteRules.Slice(posts, page, perPage));
            }
        }

        // タグの一覧と、タグごとの記事
        var tagSummaries = model.TagSummaries();
        var taggedPosts = model.Posts.Where(p => p.Tags.Count > 0).ToList();
        Listing("/tags", $"タグ | {settings.SiteName}", "写真の記事のタグの一覧です。",
            Render<TagListView>(new Dictionary<string, object?> { ["Tags"] = tagSummaries }), taggedPosts);
        foreach (var tag in tagSummaries)
        {
            PageOneRedirect(SiteRules.TagPath(tag.Slug));
            var posts = model.PostsByTag[tag.Slug];
            var tagSummariesOfPosts = posts.Select(model.ToSummary).ToList();
            var count = SiteRules.PageCount(tagSummariesOfPosts.Count, perPage);
            var slug = tag.Slug;
            for (var page = 1; page <= count; page++)
            {
                var heading = $"タグ: {tag.Name}";
                var body = Render<ListPageView>(new Dictionary<string, object?>
                {
                    ["Heading"] = heading,
                    ["Lead"] = string.IsNullOrWhiteSpace(tag.Description) ? $"「{tag.Name}」のタグが付いた記事の一覧です（{tag.PostCount} 件）。" : tag.Description,
                    ["Posts"] = SiteRules.Slice(tagSummariesOfPosts, page, perPage),
                    ["BaseUrl"] = baseUrl,
                    ["TagNames"] = tagNames,
                    ["Page"] = page,
                    ["PageCount"] = count,
                    ["HrefFor"] = (Func<int, string>)(n => SiteRules.TagHref(slug, n)),
                });
                Listing(SiteRules.TagPath(tag.Slug, page), $"{heading}{Suffix(page)} | {settings.SiteName}",
                    $"「{tag.Name}」のタグが付いた写真の記事の一覧です。", body, SiteRules.Slice(posts, page, perPage));
            }
        }

        // ワールドの一覧と、ワールドごとの写真（/worlds/{uuid}。VRChat のワールドページへのリンク付き）
        var worldSummaries = model.WorldSummaries();
        var postsWithWorlds = model.PhotosByWorld.Values.SelectMany(v => v.Select(x => x.Post)).Distinct().OrderByDescending(p => p.PublishedAt).ToList();
        Listing("/worlds", $"ワールド | {settings.SiteName}", "写真を撮ったワールドの一覧です。",
            Render<WorldListView>(new Dictionary<string, object?> { ["Worlds"] = worldSummaries, ["BaseUrl"] = baseUrl }), postsWithWorlds);
        foreach (var world in worldSummaries)
        {
            PageOneRedirect(SiteRules.WorldPath(world.Id));
            var photos = model.WorldPhotos(world.Id);
            var worldPosts = model.PhotosByWorld[world.Id].Select(x => x.Post).Distinct().ToList();
            var count = SiteRules.PageCount(photos.Count, perPage);
            for (var page = 1; page <= count; page++)
            {
                var body = Render<WorldPageView>(new Dictionary<string, object?>
                {
                    ["Id"] = world.Id,
                    ["Name"] = world.Name,
                    ["Image"] = world.Image,
                    ["Stats"] = model.WorldStatsOf(world.Id),
                    ["StatsFetchedAt"] = model.WorldStatsFetchedAt,
                    ["Photos"] = SiteRules.Slice(photos, page, perPage),
                    ["Total"] = photos.Count,
                    ["BaseUrl"] = baseUrl,
                    ["Page"] = page,
                    ["PageCount"] = count,
                    ["PerPage"] = perPage,
                });
                Listing(SiteRules.WorldPath(world.Id, page), $"{world.Name} の写真{Suffix(page)} | {settings.SiteName}",
                    $"{world.Name} で撮った写真の一覧です。", body, worldPosts);
            }
        }

        // 服の一覧と、服ごとの写真（/outfits/{id}。服の名前から BOOTH などの商品ページへ）
        var outfitSummaries = model.OutfitSummaries();
        var postsWithOutfits = model.PhotosByOutfit.Values.SelectMany(v => v.Select(x => x.Post)).Distinct().OrderByDescending(p => p.PublishedAt).ToList();
        Listing("/outfits", $"服 | {settings.SiteName}", "写真で着ている服の一覧です。",
            Render<OutfitListView>(new Dictionary<string, object?> { ["Outfits"] = outfitSummaries, ["BaseUrl"] = baseUrl }), postsWithOutfits);
        foreach (var outfit in outfitSummaries)
        {
            PageOneRedirect(SiteRules.OutfitPath(outfit.Id));
            var photos = model.OutfitPhotos(outfit.Id);
            var outfitPosts = model.PhotosByOutfit[outfit.Id].Select(x => x.Post).Distinct().ToList();
            var master = model.OutfitMaster(outfit.Id);
            var count = SiteRules.PageCount(photos.Count, perPage);
            for (var page = 1; page <= count; page++)
            {
                var body = Render<OutfitPageView>(new Dictionary<string, object?>
                {
                    ["Id"] = outfit.Id,
                    ["Name"] = outfit.Name,
                    ["Shop"] = master.Shop,
                    ["Url"] = master.Url,
                    ["Thumbnail"] = master.Thumbnail,
                    ["Description"] = OutfitDescription.Summarize(master.Description),
                    ["Tags"] = master.Tags,
                    ["PublishedAt"] = master.PublishedAt,
                    ["UpdatedAt"] = master.UpdatedAt,
                    ["FetchedAt"] = master.FetchedAt,
                    ["Photos"] = SiteRules.Slice(photos, page, perPage),
                    ["Total"] = photos.Count,
                    ["BaseUrl"] = baseUrl,
                    ["Page"] = page,
                    ["PageCount"] = count,
                    ["PerPage"] = perPage,
                });
                Listing(SiteRules.OutfitPath(outfit.Id, page), $"{outfit.Name} の写真{Suffix(page)} | {settings.SiteName}",
                    $"{outfit.Name} を着て撮った写真の一覧です。", body, outfitPosts);
            }
        }

        // 固定ページ
        pages.Add(Page(banner, "/privacy", $"プライバシーポリシー | {settings.SiteName}", "このサイトが取得する情報と、写真の掲載・削除依頼についての方針です。",
            Render<PrivacyView>(new Dictionary<string, object?> { ["ImageHost"] = new Uri(settings.PublicImageBaseUrl).Authority })));

        var report = Page(banner, "/report", $"作者へ報告 | {settings.SiteName}", "作者への報告・削除依頼のフォームです。",
            Render<LoadingView>(new Dictionary<string, object?> { ["Title"] = "作者へ報告" }));
        report["noindex"] = true;
        pages.Add(report);

        var root = new JsonObject
        {
            ["pages"] = new JsonArray(pages.ToArray<JsonNode?>()),
            ["redirects"] = new JsonArray(redirects.ToArray<JsonNode?>()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>1 ページ分。banner は、各ページの本文の最初に入れる画像（SiteBanner の HTML。静的 HTML にも画像を入れる）。</summary>
    private static JsonObject Page(string banner, string path, string title, string description, string bodyHtml) => new()
    {
        ["path"] = path,
        ["title"] = title,
        ["description"] = description,
        ["bodyHtml"] = $"<main class=\"entry-static\">{banner}{bodyHtml}</main>",
    };

    private static string Shorten(string text, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        return value.Length <= MaxDescription ? value : value[..MaxDescription].TrimEnd() + "…";
    }

    private static JsonObject BlogPosting(SiteSettings settings, PostSource post, PostPage page)
    {
        var node = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = post.Title,
            ["datePublished"] = post.PublishedAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            ["author"] = new JsonObject { ["@type"] = "Person", ["name"] = settings.Author },
            ["publisher"] = new JsonObject { ["@type"] = "Organization", ["name"] = settings.SiteName },
        };
        if (post.UpdatedAt is { } updated)
            node["dateModified"] = updated.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        // 画像: カバー（_og.jpg）と、記事の全写真の ImageObject（寸法・2048 版の URL・640 版のサムネイル）。人名・人物 ID・撮影者は入れない
        var images = new JsonArray();
        if (page.Cover is not null) images.Add(SiteRules.OgImageUrl(settings.PublicImageBaseUrl, page.Cover));
        foreach (var photo in page.Photos)
        {
            var image = new JsonObject
            {
                ["@type"] = "ImageObject",
                ["contentUrl"] = SiteRules.ImageUrl(settings.PublicImageBaseUrl, photo.Key, 2048),
                ["thumbnailUrl"] = SiteRules.ImageUrl(settings.PublicImageBaseUrl, photo.Key, 640),
                ["encodingFormat"] = "image/webp",
                ["width"] = photo.W,
                ["height"] = photo.H,
            };
            if (SiteRules.FormatTakenAt(photo.TakenAt) is not null) image["dateCreated"] = photo.TakenAt;
            images.Add(image);
        }
        if (images.Count > 0) node["image"] = images;
        if (page.Tags.Count > 0) node["keywords"] = string.Join(", ", page.Tags.Select(t => t.Name));
        return node;
    }
}
