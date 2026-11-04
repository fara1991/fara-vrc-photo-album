using System.Net;
using Bunit;
using FaraBlazorComponent.Core;
using FaraBlazorComponent.Razor;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Web.Layout;
using FaraVrcAlbum.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FaraVrcAlbum.Web.Tests;

public sealed class WebTests : BunitContext, IAsyncLifetime
{
    public WebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFaraBlazorComponent(options => SiteInfo.Configure(options, new SiteSettings { SiteName = "Fara Photo Album", Author = "Fara", ImageBaseUrl = "https://vrc-photos.fara-labs.com/", PostsPerPage = 12 }));
        Services.AddSingleton(MaintenanceInfo.Off);
    }

    private Handler? _handler;
    private HttpClient? _http;

    async Task IAsyncLifetime.DisposeAsync()
    {
        _http?.Dispose();
        _handler?.Dispose();
        await base.DisposeAsync();
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    private const string ArchiveJson = "{\"2025\":{\"12\":1},\"2026\":{\"09\":2,\"10\":12}}";

    private async Task<IRenderedComponent<MainLayout>> RenderLayoutAsync(string? archiveJson = ArchiveJson)
    {
        _handler = new Handler(path => path switch
        {
            "/data/site.json" => (HttpStatusCode.OK, "{\"siteName\":\"Fara Photo Album\",\"author\":\"Fara\",\"imageBaseUrl\":\"https://i.test/\",\"postsPerPage\":12}"),
            "/data/posts-index.json" => (HttpStatusCode.OK, "[]"),
            "/data/archive.json" when archiveJson is not null => (HttpStatusCode.OK, archiveJson),
            _ => (HttpStatusCode.NotFound, ""),
        });
        _http = new HttpClient(_handler) { BaseAddress = new Uri("https://vrc-album.test/") };
        var store = new AlbumStore(_http);
        Assert.True(await store.LoadAsync());
        Services.AddSingleton(store);
        JSInterop.Setup<bool>("faraBlazorComponent.watchMobile", _ => true).SetResult(false);
        return Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"body-text\">本文</p>"))));
    }

    [Fact]
    public async Task TheHeaderBrandIsTheSiteNameTextAndTheBannerIsInTheSidebarProfileCard()
    {
        var cut = await RenderLayoutAsync();

        var brand = cut.Find(".fbc-header a.fbc-brand");
        Assert.Equal("./", brand.GetAttribute("href"));
        Assert.Equal("ホームへ", brand.GetAttribute("title"));
        Assert.Equal("Fara Photo Album", brand.TextContent.Trim());         // ヘッダーの名前は文字（サイト名。マークは飾りの SVG）
        Assert.Empty(brand.QuerySelectorAll("img"));
        Assert.Empty(cut.FindAll(".album-banner"));                          // 本文の先頭にはバナーを出さない（サイドバーに移した）
        Assert.Single(cut.FindAll("a.fbc-brand"));                           // kit の文字リンクが重ならない
        Assert.Empty(cut.FindAll(".fbc-header h1"));                         // ヘッダーの表示名は h1 にしない
        Assert.Contains("Fara Photo Album", cut.Find("footer").TextContent); // フッターのサイト名も同じ

        // プロフィールカード: バナー画像（4:1 の寸法つき）・丸いアバター（role=img）・名前・説明・件数
        var banner = cut.Find(".album-profile-banner img");
        Assert.Equal("", banner.GetAttribute("alt"));                        // 飾り（名前は文字で出ている）
        Assert.Equal(SiteRules.HeaderImage, banner.GetAttribute("src"));     // 画像の URL は変えない（同じファイル）
        Assert.Equal("960", banner.GetAttribute("width"));                   // CLS: 寸法を指定（表示の高さは CSS）
        Assert.Equal("240", banner.GetAttribute("height"));
        var avatar = cut.Find(".album-profile .album-avatar");
        Assert.Equal("img", avatar.GetAttribute("role"));
        Assert.Equal("Fara のアイコン", avatar.GetAttribute("aria-label"));
        Assert.Equal(SiteRules.HeaderImage, avatar.QuerySelector("img")!.GetAttribute("src"));   // バナーの Fara の顔を丸く切り出す（新しい画像は作らない）
        Assert.Equal("Fara", cut.Find(".album-profile-name").TextContent.Trim());
        Assert.Equal(["0 記事", "0 写真"], cut.FindAll(".album-profile-stats li").Select(li => li.TextContent.Trim()).ToList());   // 実体の無い「オンライン」などは出さない
        Assert.DoesNotContain("オンライン", cut.Find(".album-profile").TextContent);
    }

    [Fact]
    public async Task TheSupportMenuUsesTheSameNamesAsTheFooterAndThePages()
    {
        var cut = await RenderLayoutAsync();

        // 同じ機能は、メニュー・フッター・ページで同じ名前（他のサイトと同じ「問い合わせ」。「作者へ報告」は使わない）
        var support = cut.Find("nav[aria-label=サポート]");
        var labels = support.QuerySelectorAll("a").Select(a => a.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "問い合わせ", "プライバシーポリシー" }, labels);
        var footerLinks = cut.FindAll("footer a").Select(a => a.TextContent.Trim()).ToArray();
        Assert.Contains("問い合わせ", footerLinks);
        Assert.Contains("プライバシーポリシー", footerLinks);
        Assert.DoesNotContain("作者へ報告", cut.Markup);
    }

    [Fact]
    public void TheHeaderImageFileIsHashNamedByItsContentAndIs960By240()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "FaraVrcAlbum.sln"))) repo = repo.Parent;
        var file = Path.Combine(repo!.FullName, "src", "FaraVrcAlbum.Web", "wwwroot", SiteRules.HeaderImage);
        var bytes = File.ReadAllBytes(file);

        // ファイル名の 8 桁 = 内容の SHA-256 の先頭（差し替えたらファイル名も変える = キャッシュが古い画像を返さない）
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))[..8];
        Assert.Equal(SiteRules.HeaderImage, $"images/header-{hash}.webp");
        // WebP（VP8X）の寸法: キャンバスの幅・高さ（-1 した値、3 バイト）
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        var (width, height) = System.Text.Encoding.ASCII.GetString(bytes, 12, 4) switch
        {
            "VP8X" => (1 + (bytes[24] | bytes[25] << 8 | bytes[26] << 16), 1 + (bytes[27] | bytes[28] << 8 | bytes[29] << 16)),
            "VP8 " => (bytes[26] | (bytes[27] & 0x3f) << 8, bytes[28] | (bytes[29] & 0x3f) << 8),
            "VP8L" => (1 + (bytes[21] | (bytes[22] & 0x3f) << 8), 1 + ((bytes[22] >> 6) | bytes[23] << 2 | (bytes[24] & 0x0f) << 10)),
            var other => throw new InvalidDataException($"未対応の WebP: {other}"),
        };
        Assert.Equal((SiteRules.HeaderImageWidth, SiteRules.HeaderImageHeight), (width, height));
        Assert.True(bytes.Length < 200_000, $"ヘッダーの画像が大きすぎます（{bytes.Length} バイト）");
    }

    [Fact]
    public async Task TheSidebarListsTheMonthlyArchiveNewestFirstWithCounts()
    {
        var cut = await RenderLayoutAsync();

        var archive = cut.Find(".album-archive");
        Assert.Equal("NAV", archive.TagName);
        Assert.Equal("アーカイブ（公開月別）", archive.GetAttribute("aria-label"));                              // ランドマークに名前（メインのナビと区別）
        Assert.Empty(archive.QuerySelectorAll("nav"));                                                       // nav を入れ子にしない（axe landmark-unique）
        Assert.Equal(["2026年", "2025年"], archive.QuerySelectorAll("summary .album-nav-label").Select(e => e.TextContent.Trim()).ToList());
        Assert.Equal(["14", "1"], archive.QuerySelectorAll("summary .album-nav-badge").Select(e => e.TextContent.Trim()).ToList());
        Assert.Equal(["14 件", "1 件"], archive.QuerySelectorAll("summary .album-nav-badge").Select(e => e.GetAttribute("aria-label")).ToList());   // バッジは読み上げ用の名前つき
        Assert.Equal([true, false], archive.QuerySelectorAll("details").Select(d => d.HasAttribute("open")));   // 最新の年だけ開く
        var links = archive.QuerySelectorAll("a.album-nav-sub").Select(a => (a.GetAttribute("href"), a.QuerySelector(".album-nav-label")!.TextContent.Trim(), a.QuerySelector(".album-nav-badge")!.TextContent.Trim())).ToList();
        Assert.Equal([("archive/2026/10", "10月", "12"), ("archive/2026/09", "9月", "2"), ("archive/2025/12", "12月", "1")], links);
    }

    [Fact]
    public async Task TheMainMenuHasFourItemsAndMarksTheCurrentPageWithAriaCurrent()
    {
        var cut = await RenderLayoutAsync();

        var main = cut.Find("nav[aria-label=メイン]");
        Assert.Equal(["ホーム", "ワールド", "服", "タグ"], main.QuerySelectorAll("a .album-nav-label").Select(e => e.TextContent.Trim()).ToList());
        Assert.Equal(["", "worlds", "outfits", "tags"], main.QuerySelectorAll("a").Select(a => a.GetAttribute("href")).ToList());
        // いまのページ（テストの URL は https://localhost/）はホーム。印は aria-current="page" だけ
        Assert.Equal("page", main.QuerySelector("a[href='']")!.GetAttribute("aria-current"));
        Assert.Single(cut.FindAll("[aria-current=page]"));
        Assert.Equal(3, cut.FindAll("nav").Count);                    // メイン・アーカイブ・サポート（どれも名前つき）
        Assert.All(cut.FindAll("nav"), n => Assert.False(string.IsNullOrEmpty(n.GetAttribute("aria-label"))));
    }

    [Fact]
    public async Task TheCurrentMenuItemFollowsNavigation()
    {
        var cut = await RenderLayoutAsync();
        var nav = Services.GetRequiredService<NavigationManager>();

        nav.NavigateTo("worlds/00000000-0000-4000-8000-000000000001");
        cut.WaitForAssertion(() => Assert.Equal("ワールド", cut.Find("[aria-current=page] .album-nav-label").TextContent.Trim()));
        nav.NavigateTo("archive/2026/09/page/2");
        cut.WaitForAssertion(() => Assert.Equal("9月", cut.Find("[aria-current=page] .album-nav-label").TextContent.Trim()));
        nav.NavigateTo("privacy");
        cut.WaitForAssertion(() => Assert.Equal("プライバシーポリシー", cut.Find("[aria-current=page] .album-nav-label").TextContent.Trim()));
    }

    [Fact]
    public async Task TheArchiveMenuIsOmittedWhenTheArchiveCannotBeRead()
    {
        var cut = await RenderLayoutAsync(archiveJson: null);

        Assert.Empty(cut.FindAll(".album-archive"));
    }

    [Fact]
    public async Task TheLayoutHasTheFooterReportAndPrivacyLinksAndThemeSwitch()
    {
        var cut = await RenderLayoutAsync();

        Assert.Contains(cut.FindAll("footer a"), a => (a.GetAttribute("href") ?? "").StartsWith("report"));
        Assert.Contains(cut.FindAll("footer a"), a => a.GetAttribute("href") == "privacy");
        Assert.NotEmpty(cut.FindAll(".fbc-theme-switch"));
        Assert.Contains("body-text", cut.Find(".fbc-content").InnerHtml);
    }

    [Fact]
    public async Task TheNoticeBannerShowsOnlyWhenMaintenanceIsNotice()
    {
        Services.AddSingleton(new MaintenanceInfo(MaintenanceState.Notice, "更新します"));   // 後から足した登録が優先される
        var cut = await RenderLayoutAsync();

        Assert.Contains("更新します", cut.Find(".fbc-notice-text").TextContent);
    }

    [Fact]
    public async Task TheFooterShowsTheFourSocialLinksInTheSameOrderAsTheOtherSites()
    {
        // フッターの SNS は、GitHub・X・Twitch・Discord の 4 つ（fara-labs・pokemon と同じ並び・同じ URL）。欠けると、ここで落ちる
        var cut = await RenderLayoutAsync();

        var links = cut.FindAll("footer a.fbc-footer-social");
        Assert.Equal(4, links.Count);
        Assert.Equal(["GitHub", "Twitter", "Twitch", "Discord"], links.Select(a => a.GetAttribute("aria-label") ?? "").ToArray());
        Assert.Equal(
            ["https://github.com/fara1991", "https://twitter.com/game_fara", "https://twitch.tv/game_fara", "https://discord.com/invite/B38eCSYyk5"],
            links.Select(a => a.GetAttribute("href") ?? "").ToArray());
    }

    [Fact]
    public void StorageKeysAreTheSiteSpecificOnes()
    {
        Assert.Equal("fara-vrc-album.settings", SiteInfo.StorageKey);
        Assert.Equal("fara-vrc-album.sidebar-hidden", SiteInfo.SidebarKey);
        var options = Services.GetRequiredService<SiteOptions>();
        Assert.Equal(SiteInfo.StorageKey, options.StorageKey);
        Assert.Equal(SiteInfo.SidebarKey, options.SidebarKey);
        Assert.Equal("vrc-album-dev.fara-labs.com", options.DevelopHost);
    }

    private sealed class Handler(Func<string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath);
            var (status, body) = respond(request.RequestUri.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task TheStoreLoadsSettingsAndTheIndexAndPostsOnDemand()
    {
        using var handler = new Handler(path => path switch
        {
            "/data/site.json" => (HttpStatusCode.OK, "{\"siteName\":\"Fara Photo Album\",\"author\":\"Fara\",\"imageBaseUrl\":\"https://i.test/\",\"postsPerPage\":2}"),
            "/data/posts-index.json" => (HttpStatusCode.OK, "[{\"slug\":\"a\",\"title\":\"A\",\"publishedAt\":\"2026-10-24T20:00:00+09:00\",\"summary\":\"\",\"tags\":[],\"photoCount\":0},{\"slug\":\"b\",\"title\":\"B\",\"publishedAt\":\"2026-10-23T20:00:00+09:00\",\"summary\":\"\",\"tags\":[],\"photoCount\":0},{\"slug\":\"c\",\"title\":\"C\",\"publishedAt\":\"2026-10-22T20:00:00+09:00\",\"summary\":\"\",\"tags\":[],\"photoCount\":0}]"),
            "/data/posts/a.json" => (HttpStatusCode.OK, "{\"slug\":\"a\",\"title\":\"A\",\"publishedAt\":\"2026-10-24T20:00:00+09:00\",\"summary\":\"\",\"bodyHtml\":\"<p>x</p>\",\"tags\":[],\"photos\":[]}"),
            _ => (HttpStatusCode.NotFound, ""),
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://vrc-album.test/") };
        var store = new AlbumStore(http);

        Assert.True(await store.LoadAsync());
        Assert.Equal(3, store.Posts.Count);
        Assert.Equal(2, store.PageCount);
        Assert.Equal("https://i.test/", store.Settings.ImageBaseUrl);
        Assert.Equal("<p>x</p>", (await store.GetPostAsync("a"))!.BodyHtml);
        Assert.Null(await store.GetPostAsync("missing"));
        Assert.Null(await store.GetPostAsync("../../etc/passwd"));            // slug の形でないものは取りに行かない
        Assert.DoesNotContain(handler.Requests, r => r.Contains(".."));
    }

    [Fact]
    public async Task TheStoreReportsFailureWhenTheDataCannotBeRead()
    {
        using var notFound = new Handler(_ => (HttpStatusCode.NotFound, ""));
        using var notFoundHttp = new HttpClient(notFound) { BaseAddress = new Uri("https://vrc-album.test/") };
        Assert.False(await new AlbumStore(notFoundHttp).LoadAsync());
        using var html = new Handler(_ => (HttpStatusCode.OK, "<html>"));
        using var htmlHttp = new HttpClient(html) { BaseAddress = new Uri("https://vrc-album.test/") };
        Assert.False(await new AlbumStore(htmlHttp).LoadAsync());   // 開発中などで index.html が返る
    }
}
