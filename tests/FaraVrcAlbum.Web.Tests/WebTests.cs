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

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    private const string ArchiveJson = "{\"2025\":{\"12\":1},\"2026\":{\"09\":2,\"10\":12}}";

    private IRenderedComponent<MainLayout> RenderLayout(string? archiveJson = ArchiveJson)
    {
        var handler = new Handler(path => path switch
        {
            "/data/site.json" => (HttpStatusCode.OK, "{\"siteName\":\"Fara Photo Album\",\"author\":\"Fara\",\"imageBaseUrl\":\"https://i.test/\",\"postsPerPage\":12}"),
            "/data/posts-index.json" => (HttpStatusCode.OK, "[]"),
            "/data/archive.json" when archiveJson is not null => (HttpStatusCode.OK, archiveJson),
            _ => (HttpStatusCode.NotFound, ""),
        });
        var store = Store(handler);
        Assert.True(store.LoadAsync().GetAwaiter().GetResult());
        Services.AddSingleton(store);
        JSInterop.Setup<bool>("faraBlazorComponent.watchMobile", _ => true).SetResult(false);
        return Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p class=\"body-text\">本文</p>"))));
    }

    [Fact]
    public void TheHeaderBrandIsTheSiteNameTextAndTheImageIsAtTheStartOfTheBody()
    {
        var cut = RenderLayout();

        var brand = cut.Find(".fbc-header a.fbc-brand");
        Assert.Equal("./", brand.GetAttribute("href"));
        Assert.Equal("ホームへ", brand.GetAttribute("title"));
        Assert.Equal("Fara Photo Album", brand.TextContent.Trim());         // ヘッダーの名前は文字（サイト名）
        Assert.Empty(brand.QuerySelectorAll("img"));
        var img = cut.Find(".album-banner img");                            // 画像は本文の最初（本文より前）
        Assert.Equal("", img.GetAttribute("alt"));
        Assert.Equal(SiteRules.HeaderImage, img.GetAttribute("src"));
        Assert.Equal("960", img.GetAttribute("width"));                     // CLS: 寸法を指定（表示の高さは CSS）
        Assert.Equal("240", img.GetAttribute("height"));
        Assert.Equal("high", img.GetAttribute("fetchpriority"));
        Assert.Null(img.GetAttribute("loading"));                            // 遅延読み込みにしない
        Assert.True(cut.Markup.IndexOf("album-banner", StringComparison.Ordinal) < cut.Markup.IndexOf("body-text", StringComparison.Ordinal));
        Assert.Single(cut.FindAll("a.fbc-brand"));          // kit の文字リンクが重ならない
        Assert.Empty(cut.FindAll(".fbc-header h1"));        // ヘッダーの表示名は h1 にしない
        Assert.Contains("Fara Photo Album", cut.Find("footer").TextContent);     // フッターのサイト名も同じ
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
    public void TheSidebarListsTheMonthlyArchiveNewestFirstWithCounts()
    {
        var cut = RenderLayout();

        var archive = cut.Find(".album-archive");
        Assert.Equal("NAV", archive.TagName);
        Assert.Equal("アーカイブ（公開月別）", archive.GetAttribute("aria-label"));                              // ランドマークに名前（メインのナビと区別）
        Assert.Empty(archive.QuerySelectorAll("nav"));                                                       // nav を入れ子にしない（axe landmark-unique）
        Assert.Equal(["2026年（14）", "2025年（1）"], archive.QuerySelectorAll("summary").Select(e => e.TextContent.Trim()).ToList());
        Assert.Equal([true, false], archive.QuerySelectorAll("details").Select(d => d.HasAttribute("open")));   // 最新の年だけ開く
        var links = archive.QuerySelectorAll("a.album-archive-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ToList();
        Assert.Equal([("archive/2026/10", "10月（12）"), ("archive/2026/09", "9月（2）"), ("archive/2025/12", "12月（1）")], links);
    }

    [Fact]
    public void TheArchiveMenuIsOmittedWhenTheArchiveCannotBeRead()
    {
        var cut = RenderLayout(archiveJson: null);

        Assert.Empty(cut.FindAll(".album-archive"));
    }

    [Fact]
    public void TheLayoutHasTheFooterReportAndPrivacyLinksAndThemeSwitch()
    {
        var cut = RenderLayout();

        Assert.Contains(cut.FindAll("footer a"), a => (a.GetAttribute("href") ?? "").StartsWith("report"));
        Assert.Contains(cut.FindAll("footer a"), a => a.GetAttribute("href") == "privacy");
        Assert.NotEmpty(cut.FindAll(".fbc-theme-switch"));
        Assert.Contains("body-text", cut.Find(".fbc-content").InnerHtml);
    }

    [Fact]
    public void TheNoticeBannerShowsOnlyWhenMaintenanceIsNotice()
    {
        Services.AddSingleton(new MaintenanceInfo(MaintenanceState.Notice, "更新します"));   // 後から足した登録が優先される
        var cut = RenderLayout();

        Assert.Contains("更新します", cut.Find(".fbc-notice-text").TextContent);
    }

    [Fact]
    public void TheFooterShowsTheFourSocialLinksInTheSameOrderAsTheOtherSites()
    {
        // フッターの SNS は、GitHub・X・Twitch・Discord の 4 つ（fara-labs・pokemon と同じ並び・同じ URL）。欠けると、ここで落ちる
        var cut = RenderLayout();

        var links = cut.FindAll("footer a.fbc-footer-social");
        Assert.Equal(4, links.Count);
        Assert.Equal(["GitHub", "Twitter", "Twitch", "Discord"], links.Select(a => a.GetAttribute("aria-label")).ToArray());
        Assert.Equal(
            ["https://github.com/fara1991", "https://twitter.com/game_fara", "https://twitch.tv/game_fara", "https://discord.com/invite/B38eCSYyk5"],
            links.Select(a => a.GetAttribute("href")).ToArray());
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

    private static AlbumStore Store(Handler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://vrc-album.test/") });

    [Fact]
    public async Task TheStoreLoadsSettingsAndTheIndexAndPostsOnDemand()
    {
        var handler = new Handler(path => path switch
        {
            "/data/site.json" => (HttpStatusCode.OK, "{\"siteName\":\"Fara Photo Album\",\"author\":\"Fara\",\"imageBaseUrl\":\"https://i.test/\",\"postsPerPage\":2}"),
            "/data/posts-index.json" => (HttpStatusCode.OK, "[{\"slug\":\"a\",\"title\":\"A\",\"publishedAt\":\"2026-10-24T20:00:00+09:00\",\"summary\":\"\",\"tags\":[],\"photoCount\":0},{\"slug\":\"b\",\"title\":\"B\",\"publishedAt\":\"2026-10-23T20:00:00+09:00\",\"summary\":\"\",\"tags\":[],\"photoCount\":0},{\"slug\":\"c\",\"title\":\"C\",\"publishedAt\":\"2026-10-22T20:00:00+09:00\",\"summary\":\"\",\"tags\":[],\"photoCount\":0}]"),
            "/data/posts/a.json" => (HttpStatusCode.OK, "{\"slug\":\"a\",\"title\":\"A\",\"publishedAt\":\"2026-10-24T20:00:00+09:00\",\"summary\":\"\",\"bodyHtml\":\"<p>x</p>\",\"tags\":[],\"photos\":[]}"),
            _ => (HttpStatusCode.NotFound, ""),
        });
        var store = Store(handler);

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
        Assert.False(await Store(new Handler(_ => (HttpStatusCode.NotFound, ""))).LoadAsync());
        Assert.False(await Store(new Handler(_ => (HttpStatusCode.OK, "<html>"))).LoadAsync());   // 開発中などで index.html が返る
    }
}
