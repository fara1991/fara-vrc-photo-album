using Bunit;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Pages;
using Xunit;

namespace FaraVrcAlbum.Web.Tests;

/// <summary>Pages（Web と Build の HtmlRenderer で共用する素の HTML の部品）。</summary>
public class PagesTests : BunitContext
{
    private const string Base = "https://vrc-photos.fara-labs.com/";
    private const string Key = "2026-10/20261023-213312-345_a1b2c3d4";

    private static PhotoInfo Photo(int w = 2048, int h = 1152, string? color = "1b2a3c", string? caption = null) =>
        new() { Key = Key, W = w, H = h, Color = color, Caption = caption };

    private static PostSummary Summary(string slug, PhotoInfo? cover = null, int count = 1) => new()
    {
        Slug = slug, Title = "題 " + slug, PublishedAt = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(9)),
        Cover = cover, PhotoCount = count,
    };

    [Fact]
    public void PhotoImgHasSizeAspectRatioColorAndLazyLoadingByDefault()
    {
        var img = Render<PhotoImg>(p => p.Add(c => c.Photo, Photo()).Add(c => c.BaseUrl, Base).Add(c => c.Alt, "代替")).Find("img");

        Assert.Equal("2048", img.GetAttribute("width"));
        Assert.Equal("1152", img.GetAttribute("height"));
        Assert.Equal("lazy", img.GetAttribute("loading"));
        Assert.Equal("async", img.GetAttribute("decoding"));
        Assert.Null(img.GetAttribute("fetchpriority"));
        Assert.Equal("代替", img.GetAttribute("alt"));
        Assert.Contains("aspect-ratio: 2048 / 1152", img.GetAttribute("style"));
        Assert.Contains("background-color: #1b2a3c", img.GetAttribute("style"));
        Assert.Equal(Base + Key + "_640.webp", img.GetAttribute("src"));
        Assert.Equal($"{Base}{Key}_640.webp 640w, {Base}{Key}_1280.webp 1280w", img.GetAttribute("srcset"));                    // 一覧のカードは 640 / 1280
        Assert.Equal("(min-width: 1200px) 360px, (min-width: 600px) 45vw, 92vw", img.GetAttribute("sizes"));
    }

    [Fact]
    public void PhotoImgIsEagerAndHighPriorityForTheFirstImages()
    {
        var img = Render<PhotoImg>(p => p.Add(c => c.Photo, Photo()).Add(c => c.BaseUrl, Base).Add(c => c.Eager, true)).Find("img");

        Assert.Equal("eager", img.GetAttribute("loading"));
        Assert.Equal("high", img.GetAttribute("fetchpriority"));
    }

    [Fact]
    public void PhotoImgWithoutAColorOmitsTheBackground()
    {
        var img = Render<PhotoImg>(p => p.Add(c => c.Photo, Photo(color: null)).Add(c => c.BaseUrl, Base)).Find("img");

        Assert.DoesNotContain("background-color", img.GetAttribute("style"));
    }

    [Fact]
    public void PostListShowsCardsWithRelativeLinksDatesTagsAndOnlyTheFirstImageEager()
    {
        var withTags = Summary("a", Photo());
        withTags.Tags = ["night", "unknown"];
        var cut = Render<PostListView>(p => p.Add(c => c.BaseUrl, Base)
            .Add(c => c.TagNames, new Dictionary<string, string> { ["night"] = "夜景" })
            .Add(c => c.Posts, new[] { withTags, Summary("b", Photo()), Summary("c", Photo()), Summary("d", null, 0) }));

        var cards = cut.FindAll(".album-card");
        Assert.Equal(4, cards.Count);
        Assert.Equal("posts/a", cards[0].QuerySelector("h2 a.album-card-link")!.GetAttribute("href"));
        Assert.Equal("題 a", cards[0].QuerySelector("h2")!.TextContent);
        Assert.Equal(["tags/night", "tags/unknown"], cards[0].QuerySelectorAll(".album-card-tags a").Select(a => a.GetAttribute("href")));
        Assert.Equal(["夜景", "unknown"], cards[0].QuerySelectorAll(".album-card-tags a").Select(a => a.TextContent));   // 名前が無いタグは slug
        Assert.Equal("", cards[0].QuerySelector("img")!.GetAttribute("alt"));                      // 題が隣にあるので、カバーは飾り（alt は空）
        Assert.Contains("1 枚", cards[0].QuerySelector(".album-card-meta")!.TextContent);
        Assert.Null(cards[0].QuerySelector("time"));                                              // 題の下に日付は出さない（題の既定が日付のため）
        var loading = cut.FindAll("img").Select(i => i.GetAttribute("loading")).ToList();
        Assert.Equal(["eager", "lazy", "lazy"], loading);   // 最初の 1 枚だけ優先。写真のない記事（d）は img を出さない
        Assert.Empty(cards[3].QuerySelectorAll("img"));
    }

    [Fact]
    public void PostListWithoutPostsSaysSo()
    {
        var cut = Render<PostListView>(p => p.Add(c => c.BaseUrl, Base).Add(c => c.Posts, Array.Empty<PostSummary>()));

        Assert.Empty(cut.FindAll(".album-card"));
        Assert.Contains("まだ記事がありません", cut.Markup);
    }

    [Fact]
    public void PagerIsHiddenForOnePageAndLinksToNeighboursOtherwise()
    {
        Assert.Empty(Render<Pager>(p => p.Add(c => c.Page, 1).Add(c => c.PageCount, 1).Add(c => c.HrefFor, SiteRules.HomeHref)).FindAll("nav"));

        var middle = Render<Pager>(p => p.Add(c => c.Page, 2).Add(c => c.PageCount, 3).Add(c => c.HrefFor, SiteRules.HomeHref));
        Assert.Equal("./", middle.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Equal("page/3", middle.Find("a[rel=next]").GetAttribute("href"));
        Assert.Contains("2 / 3", middle.Find(".album-pager-status").TextContent);

        var first = Render<Pager>(p => p.Add(c => c.Page, 1).Add(c => c.PageCount, 3).Add(c => c.HrefFor, SiteRules.HomeHref));
        Assert.Empty(first.FindAll("a[rel=prev]"));
        var last = Render<Pager>(p => p.Add(c => c.Page, 3).Add(c => c.PageCount, 3).Add(c => c.HrefFor, SiteRules.HomeHref));
        Assert.Empty(last.FindAll("a[rel=next]"));
        Assert.Equal("page/2", last.Find("a[rel=prev]").GetAttribute("href"));
    }

    [Fact]
    public void HomeHasAHiddenH1OnlyOnTheFirstPage()
    {
        var first = Render<HomeView>(p => p.Add(c => c.SiteName, "Fara Photo Album").Add(c => c.BaseUrl, Base).Add(c => c.Posts, new[] { Summary("a", Photo()) }));
        Assert.Equal("Fara Photo Album", first.Find("h1").TextContent);
        Assert.Contains("visually-hidden", first.Find("h1").ClassName);                // 視覚的に隠した h1
        var lead = first.Find(".album-lead");
        Assert.Contains("Fara です。インバイトでよく引きこもっています。", lead.TextContent);
        Assert.Contains("たまに VRChat に潜って撮った写真や、フレンドに撮ってもらった写真を、このサイトに載せていきます。", lead.TextContent);
        Assert.Single(lead.QuerySelectorAll("br"));                                     // 2 行（改行は <br>）

        var second = Render<HomeView>(p => p.Add(c => c.SiteName, "Fara Photo Album").Add(c => c.BaseUrl, Base).Add(c => c.Page, 2).Add(c => c.PageCount, 3).Add(c => c.Posts, new[] { Summary("a", Photo()) }));
        Assert.Single(second.FindAll("h1"));
        Assert.Empty(second.FindAll(".album-lead"));
        Assert.Contains("2 ページ目", second.Find("h1").TextContent);
    }

    [Fact]
    public void PostArticleShowsTheBodyHtmlAndAGalleryWithSizesAndCaptions()
    {
        var post = new PostPage
        {
            Slug = "x", Title = "記事の題", PublishedAt = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(9)),
            Tags = [new TagInfo { Slug = "sample", Name = "サンプル" }, new TagInfo { Slug = "night", Name = "夜景" }], BodyHtml = "<p>本文 <strong>強調</strong></p>",
            Photos = [Photo(caption: "キャプション"), new PhotoInfo { Key = "2026-10/20261023-214001-120_0f1e2d3c", W = 1152, H = 2048 }],
        };

        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, post).Add(c => c.BaseUrl, Base));

        Assert.Equal("記事の題", cut.Find("h1").TextContent);
        Assert.Equal("<p>本文 <strong>強調</strong></p>", cut.Find(".album-post-body").InnerHtml);
        var imgs = cut.FindAll(".album-gallery img");
        Assert.Equal(2, imgs.Count);
        Assert.Equal("キャプション", imgs[0].GetAttribute("alt"));
        Assert.Equal("記事の題 の写真（2 / 2）", imgs[1].GetAttribute("alt"));   // キャプションもワールドも無ければ題 + 連番
        Assert.Equal("eager", imgs[0].GetAttribute("loading"));
        Assert.Equal("lazy", imgs[1].GetAttribute("loading"));
        Assert.Equal(Base + Key + "_1280.webp", imgs[0].GetAttribute("src"));
        Assert.Contains("_2048.webp 2048w", imgs[0].GetAttribute("srcset"));
        Assert.Equal(Base + Key + "_2048.webp", imgs[0].ParentElement!.GetAttribute("href"));   // JS が無くても大きい画像を開ける
        Assert.Contains("--ar: 1.7778", cut.FindAll(".album-gallery-item")[0].GetAttribute("style"));     // 縦横比（CSS が行の幅の配分に使う）
        Assert.Contains("album-gallery--2", cut.Find(".album-gallery").ClassName);
        Assert.Equal(["photo-1", "photo-2"], cut.FindAll(".album-gallery-item").Select(i => i.Id));   // #photo-N で直接開ける
        Assert.StartsWith("(min-width: 768px) ", imgs[0].GetAttribute("sizes"));          // 枚数・縦横比に応じた sizes（SiteRules.GallerySizes）
        Assert.EndsWith(", 92vw", imgs[0].GetAttribute("sizes"));
        Assert.Single(cut.FindAll("figcaption"));
    }

    [Fact]
    public void BodyHtmlIsRenderedAsMarkupButTitlesAreEscaped()
    {
        var post = new PostPage { Slug = "x", Title = "<b>題</b>", PublishedAt = DateTimeOffset.Now, BodyHtml = "<p>ok</p>" };

        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, post).Add(c => c.BaseUrl, Base));

        Assert.Equal("<b>題</b>", cut.Find("h1").TextContent);
        Assert.Empty(cut.FindAll("h1 b"));
    }

    private const string World = "wrld_00000000-0000-4000-8000-000000000001";
    private const string WorldUuid = "00000000-0000-4000-8000-000000000001";

    private static PostPage PostWith(PhotoInfo photo) => new()
    {
        Slug = "x", Title = "題", PublishedAt = new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero), BodyHtml = "<p>b</p>",
        Tags = [new TagInfo { Slug = "night", Name = "夜景" }], Photos = [photo],
    };

    [Fact]
    public void TextBeforeAPhotoSplitsTheGalleryAndTheAfterwordComesLast()
    {
        PhotoInfo P(int n, string? text = null) => new() { Key = $"2026-10/20261023-21331{n}-345_a1b2c3d{n}", W = 2048, H = 1152, TextHtml = text };
        var post = PostWith(P(1));
        post.Photos = [P(1), P(2), P(3, "<p>ここから夜</p>"), P(4)];
        post.AfterwordHtml = "<p>おわり</p>";

        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, post).Add(c => c.BaseUrl, Base));

        var galleries = cut.FindAll("ul.album-gallery");
        Assert.Equal(2, galleries.Count);                                               // 文章のある写真の前で区切る
        Assert.Contains("album-gallery--2", galleries[0].ClassName);
        Assert.Contains("album-gallery--2", galleries[1].ClassName);
        Assert.Equal(["photo-1", "photo-2", "photo-3", "photo-4"], cut.FindAll("li.album-gallery-item").Select(li => li.Id));   // 番号は記事全体の通し
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("photo-2", StringComparison.Ordinal) < markup.IndexOf("ここから夜", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("ここから夜", StringComparison.Ordinal) < markup.IndexOf("photo-3", StringComparison.Ordinal));
        Assert.True(markup.IndexOf("photo-4", StringComparison.Ordinal) < markup.IndexOf("おわり", StringComparison.Ordinal));
    }

    [Fact]
    public void WithoutTextThereIsOneGalleryAsBefore()
    {
        var post = PostWith(Photo());
        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, post).Add(c => c.BaseUrl, Base));
        Assert.Single(cut.FindAll("ul.album-gallery"));
        Assert.Empty(cut.FindAll(".album-post-text"));
        Assert.Empty(cut.FindAll(".album-post-afterword"));
    }

    [Fact]
    public void ThePostDateLinksToTheJstMonthArchiveAndTagsLinkToTheirPages()
    {
        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, PostWith(Photo())).Add(c => c.BaseUrl, Base));

        Assert.Equal("archive/2026/11", cut.Find(".album-heading a").GetAttribute("href"));      // UTC 10/31 15:30 = JST 11/1
        Assert.Equal("tags/night", cut.Find("a.album-tag").GetAttribute("href"));
        Assert.Equal("夜景", cut.Find("a.album-tag").TextContent);
    }

    [Fact]
    public void TheCaptionShowsTheWorldNameAsALinkToTheSitesWorldPage()
    {
        var photo = Photo(caption: "夕方");
        photo.World = new WorldRef { Id = World, Name = "マスタの名前" };

        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, PostWith(photo)).Add(c => c.BaseUrl, Base));

        // ワールド名そのものが、サイト内のワールドのページへのリンク（VRChat へは、そのページの名前から）。リンクは 1 つだけ
        var link = Assert.Single(cut.FindAll("figcaption .album-caption-world a"));
        Assert.Equal("worlds/" + World["wrld_".Length..], link.GetAttribute("href"));
        Assert.Equal("マスタの名前", link.TextContent);
        Assert.Null(link.GetAttribute("target"));
        Assert.Contains("夕方", cut.Find("figcaption").TextContent);
    }

    [Fact]
    public void APhotoWithoutAWorldHasNoWorldNameOrLink()
    {
        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, PostWith(Photo())).Add(c => c.BaseUrl, Base));

        Assert.Empty(cut.FindAll(".album-caption-world"));
        Assert.Empty(cut.FindAll("figcaption"));            // キャプション・撮影日時・ワールドのどれも無ければ figcaption も出さない
    }

    [Fact]
    public void ThePagerUsesTheGivenHrefFunction()
    {
        var cut = Render<Pager>(p => p.Add(c => c.Page, 2).Add(c => c.PageCount, 3).Add(c => c.HrefFor, n => SiteRules.TagHref("night", n)));

        Assert.Equal("tags/night", cut.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Equal("tags/night/page/3", cut.Find("a[rel=next]").GetAttribute("href"));
    }

    [Fact]
    public void TheWorldPageHasTheNameTheVrchatLinkTheCardsAndPaging()
    {
        var photos = new[]
        {
            new PhotoRef { Key = Key, W = 2048, H = 1152, Color = "1b2a3c", Slug = "a" },
            new PhotoRef { Key = "2026-10/20261023-214001-120_0f1e2d3c", W = 1152, H = 2048, Slug = "b" },
        };

        var cut = Render<WorldPageView>(p => p.Add(c => c.Id, World).Add(c => c.Name, "ワールド名").Add(c => c.Photos, photos).Add(c => c.Total, 5)
            .Add(c => c.BaseUrl, Base).Add(c => c.Page, 2).Add(c => c.PageCount, 3).Add(c => c.PerPage, 2));

        Assert.Equal("ワールド名", cut.Find("h1").TextContent);
        var vrchat = cut.Find("a.album-external");
        Assert.Equal($"https://vrchat.com/home/world/{World}/info", vrchat.GetAttribute("href"));
        Assert.Equal("_blank", vrchat.GetAttribute("target"));
        Assert.Equal("noopener noreferrer nofollow", vrchat.GetAttribute("rel"));
        Assert.Equal(["posts/a", "posts/b"], cut.FindAll(".album-card a").Select(a => a.GetAttribute("href")));
        Assert.Equal("ワールド名 で撮った写真（3）", cut.FindAll("img")[0].GetAttribute("alt"));     // 2 ページ目 = 通し番号 3 から
        Assert.Equal($"worlds/{WorldUuid}", cut.Find("a[rel=prev]").GetAttribute("href"));
        Assert.Equal($"worlds/{WorldUuid}/page/3", cut.Find("a[rel=next]").GetAttribute("href"));
        Assert.Contains("5 枚", cut.Markup);
        Assert.Empty(cut.FindAll(".album-world-hero"));                       // 画像の無いワールドは、画像を出さない
    }

    private static readonly WorldImage Img = new() { Key = "worlds/00000000-0000-4000-8000-000000000001/file_00000000-0000-4000-8000-0000000000aa_3", W = 1024, H = 768 };

    [Fact]
    public void TheWorldPageShowsTheWorldImageFirstThenTheNameAndInfoThenThePhotos()
    {
        var photos = new[] { new PhotoRef { Key = Key, W = 2048, H = 1152, Slug = "a" } };
        var stats = new WorldStats
        {
            AuthorName = "テスト作者", Description = "説明文", Capacity = 80, RecommendedCapacity = 40, Visits = 123456, Favorites = 4321,
            Tags = ["Chill", "Field"], PublicationDate = "2025-01-04T15:00:00Z", UpdatedAt = "2025-03-10T03:00:00Z",
        };
        var cut = Render<WorldPageView>(p => p.Add(c => c.Id, World).Add(c => c.Name, "ワールド名").Add(c => c.Image, Img)
            .Add(c => c.Stats, stats).Add(c => c.StatsFetchedAt, "2026-10-26T12:00:00+09:00")
            .Add(c => c.Photos, photos).Add(c => c.Total, 1).Add(c => c.BaseUrl, Base));

        // 画像と「VRChat でワールドを見る」は、どちらも VRChat のワールドページ（新しいタブ）
        var hero = cut.Find("a.album-world-hero");
        Assert.Equal($"https://vrchat.com/home/world/{World}/info", hero.GetAttribute("href"));
        Assert.Equal(("_blank", "noopener noreferrer nofollow"), (hero.GetAttribute("target"), hero.GetAttribute("rel")));
        Assert.Equal($"https://vrchat.com/home/world/{World}/info", cut.Find("a.album-external").GetAttribute("href"));
        var img = hero.QuerySelector("img")!;
        Assert.Equal($"{Base}{Img.Key}_1024.webp", img.GetAttribute("src"));             // R2 から（VRChat から直接読まない）
        Assert.Contains($"{Base}{Img.Key}_512.webp 512w", img.GetAttribute("srcset"));
        Assert.Equal(("1024", "768"), (img.GetAttribute("width"), img.GetAttribute("height")));
        Assert.DoesNotContain("vrchat.cloud", cut.Markup);

        var info = cut.Find("dl.album-world-info").TextContent;
        foreach (var expected in new[] { "テスト作者", "123,456", "4,321", "80 人（推奨 40 人）", "2025年1月5日", "2025年3月10日", "Chill、Field" })
            Assert.Contains(expected, info);
        Assert.DoesNotContain("人気度", info);
        Assert.DoesNotContain("注目度", info);
        Assert.Equal("説明文", cut.Find(".album-world-description").TextContent);
        Assert.Contains("2026年10月26日時点の", cut.Markup);

        var markup = cut.Markup;
        var order = new[] { "album-world-hero", "<h1", "album-world-info", "album-grid--photos" }.Select(x => markup.IndexOf(x, StringComparison.Ordinal)).ToArray();
        Assert.True(order.All(i => i >= 0) && order.SequenceEqual(order.Order()), string.Join(",", order));
    }

    [Fact]
    public void TheWorldListUsesTheWorldImageFromR2AndShowsNoImageForAWorldWithoutOne()
    {
        var cover = new PhotoRef { Key = Key, W = 2048, H = 1152, Slug = "a" };
        var cut = Render<WorldListView>(p => p.Add(c => c.BaseUrl, Base).Add(c => c.Worlds, new[]
        {
            new WorldSummary { Id = World, Name = "画像あり", PhotoCount = 3, Cover = cover, Image = Img },
            new WorldSummary { Id = "wrld_00000000-0000-4000-8000-000000000002", Name = "画像なし", PhotoCount = 1, Cover = cover },
        }));

        var images = cut.FindAll(".album-card-media img");
        Assert.Single(images);                                                         // 画像の無いワールドは、画像を出さない（自分の撮った写真は出さない）
        Assert.Equal($"{Base}{Img.Key}_512.webp", images[0].GetAttribute("src"));
        Assert.Contains("album-card-media--world", images[0].ParentElement!.ClassName);
        Assert.Equal(2, cut.FindAll(".album-card").Count);
    }

    private static PostPage PostOfPhotos(int count, Action<PhotoInfo, int>? configure = null)
    {
        var photos = Enumerable.Range(0, count).Select(i =>
        {
            var photo = Photo();
            photo.Key = $"2026-10/20261023-2133{i % 60:00}-{i:000}_{i + 1:x8}";
            configure?.Invoke(photo, i);
            return photo;
        }).ToList();
        return new PostPage { Slug = "x", Title = "題", PublishedAt = new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(9)), BodyHtml = "<p>b</p>", Photos = photos };
    }

    [Theory]
    [InlineData(1, "album-gallery--1")]
    [InlineData(2, "album-gallery--2")]
    [InlineData(3, "album-gallery--n")]
    [InlineData(12, "album-gallery--n")]
    public void TheGalleryLayoutDependsOnThePhotoCount(int count, string expected)
    {
        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, PostOfPhotos(count)).Add(c => c.BaseUrl, Base));

        Assert.Contains(expected, cut.Find(".album-gallery").ClassName);
        Assert.Equal(count, cut.FindAll(".album-photo-link").Count);
        Assert.All(cut.FindAll(".album-photo-link"), a => Assert.Equal("dialog", a.GetAttribute("aria-haspopup")));
    }

    [Fact]
    public void EveryGalleryImageHasSizeAspectRatioColorAndOnlyTheFirstIsEager()
    {
        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, PostOfPhotos(4)).Add(c => c.BaseUrl, Base));

        var imgs = cut.FindAll(".album-gallery img");
        Assert.All(imgs, img =>
        {
            Assert.Equal("2048", img.GetAttribute("width"));
            Assert.Equal("1152", img.GetAttribute("height"));
            Assert.Contains("aspect-ratio: 2048 / 1152", img.GetAttribute("style"));
            Assert.Contains("background-color: #1b2a3c", img.GetAttribute("style"));
            Assert.Equal("async", img.GetAttribute("decoding"));
        });
        Assert.Equal(["eager", "lazy", "lazy", "lazy"], imgs.Select(i => i.GetAttribute("loading")));
        Assert.Equal("high", imgs[0].GetAttribute("fetchpriority"));
        Assert.All(imgs.Skip(1), img => Assert.Null(img.GetAttribute("fetchpriority")));
    }

    [Fact]
    public void TheArticleHasBreadcrumbsPreviousNextAndRelatedPosts()
    {
        var post = PostOfPhotos(1);
        post.Older = new PostLink { Slug = "old", Title = "古い記事" };
        post.Newer = new PostLink { Slug = "new", Title = "新しい記事" };
        post.Related = [Summary("rel", Photo())];

        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, post).Add(c => c.BaseUrl, Base));

        Assert.Equal(["./", "archive/2026/10"], cut.FindAll(".album-breadcrumb a").Select(a => a.GetAttribute("href")));
        Assert.Equal("page", cut.Find(".album-breadcrumb [aria-current]").GetAttribute("aria-current"));
        Assert.Equal("posts/old", cut.Find("a[rel=prev].album-prevnext-link").GetAttribute("href"));
        Assert.Equal("posts/new", cut.Find("a[rel=next].album-prevnext-link").GetAttribute("href"));
        Assert.Contains("古い記事", cut.Find("a[rel=prev]").TextContent);
        Assert.Equal("posts/rel", cut.Find(".album-related h3.album-card-title a").GetAttribute("href"));
        Assert.Equal("lazy", cut.Find(".album-related img").GetAttribute("loading"));              // 画面の下にある一覧は優先しない
    }

    [Fact]
    public void WithoutNeighboursAndRelatedPostsThoseSectionsAreNotShown()
    {
        var cut = Render<PostArticleView>(p => p.Add(c => c.Post, PostOfPhotos(1)).Add(c => c.BaseUrl, Base));

        Assert.Empty(cut.FindAll(".album-prevnext"));
        Assert.Empty(cut.FindAll(".album-related"));
    }

    [Fact]
    public void TheOutfitPageShowsTheBoothInfoAndTheNameLinksToTheShopWhileTheListUsesTheBoothImage()
    {
        var page = Render<OutfitPageView>(p => p.Add(c => c.Id, "coat").Add(c => c.Name, "コート").Add(c => c.Shop, "店").Add(c => c.Url, "https://booth.pm/ja/items/1")
            .Add(c => c.Thumbnail, "https://booth.pximg.net/a.jpg").Add(c => c.Description, "説明").Add(c => c.Tags, new[] { "A", "B" })
            .Add(c => c.PublishedAt, "2025-11-20T10:00:00+09:00").Add(c => c.FetchedAt, "2026-10-26T12:00:00+09:00")
            .Add(c => c.Photos, new[] { new PhotoRef { Key = Key, W = 2048, H = 1152, Slug = "a" } }).Add(c => c.Total, 1).Add(c => c.BaseUrl, Base));

        Assert.Equal("https://booth.pm/ja/items/1", page.Find("h1 a").GetAttribute("href"));
        Assert.Equal("https://booth.pximg.net/a.jpg", page.Find("a.album-outfit-hero img").GetAttribute("src"));
        Assert.Equal("no-referrer", page.Find("a.album-outfit-hero img").GetAttribute("referrerpolicy"));
        Assert.Contains("A、B", page.Find("dl.album-world-info").TextContent);
        Assert.Contains("2026年10月26日時点の BOOTH の情報です。", page.Markup);
        Assert.DoesNotContain(">商品ページを見る</a>", page.Markup);
        Assert.NotNull(page.Find("p.album-lead--after-info"));

        // BOOTH の情報が無い服: 画像・情報の塊を出さない（名前はリンクのまま）
        var bare = Render<OutfitPageView>(p => p.Add(c => c.Id, "x").Add(c => c.Name, "靴").Add(c => c.Total, 0).Add(c => c.BaseUrl, Base));
        Assert.Empty(bare.FindAll("dl.album-world-info"));
        Assert.Empty(bare.FindAll(".album-outfit-hero"));

        var list = Render<OutfitListView>(p => p.Add(c => c.Outfits, new[]
        {
            new OutfitSummary { Id = "coat", Name = "コート", PhotoCount = 1, Thumbnail = "https://booth.pximg.net/a.jpg" },
            new OutfitSummary { Id = "boots", Name = "靴", PhotoCount = 1 },
        }));
        Assert.Single(list.FindAll(".album-card-media img"));
        Assert.Equal("https://booth.pximg.net/a.jpg", list.Find(".album-card-media--outfit img").GetAttribute("src"));
    }

    [Fact]
    public void ALongDescriptionIsFoldedWithAToggleAndAShortOneIsNot()
    {
        var longText = string.Join("\n", Enumerable.Range(1, 8).Select(i => $"{i} 行目"));
        var folded = Render<FoldedText>(p => p.Add(c => c.Text, longText).Add(c => c.Id, "outfit"));

        Assert.Equal("album-more-outfit", folded.Find("input.album-more-toggle").Id);                    // チェックボックス（JS なし）
        Assert.Equal("album-more-outfit", folded.Find("label.album-more-label").GetAttribute("for"));
        Assert.Contains("album-more-text--clamped", folded.Find("p.album-more-text").ClassName);        // 3 行に切り詰める
        Assert.Equal(longText, folded.Find("p.album-more-text").TextContent);                            // 中身は全文（折りたたみは CSS）
        Assert.Contains("続きを見る", folded.Find(".album-more-open").TextContent);
        Assert.Contains("閉じる", folded.Find(".album-more-close").TextContent);

        var brief = Render<FoldedText>(p => p.Add(c => c.Text, "短い説明です。"));
        Assert.Empty(brief.FindAll("input.album-more-toggle"));
        Assert.Empty(brief.FindAll("label.album-more-label"));
        Assert.DoesNotContain("album-more-text--clamped", brief.Markup);

        Assert.Empty(Render<FoldedText>(p => p.Add(c => c.Text, "  ")).Markup.Trim());                    // 空なら何も出さない
        var oneLongLine = Render<FoldedText>(p => p.Add(c => c.Text, new string('あ', FoldedText.Characters + 1)));
        Assert.Single(oneLongLine.FindAll("input.album-more-toggle"));                                    // 1 行でも長ければ折りたたむ
    }
}
