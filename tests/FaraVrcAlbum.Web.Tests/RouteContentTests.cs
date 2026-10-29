using System.Net;
using Bunit;
using FaraBlazorComponent.Core;
using FaraBlazorComponent.Razor;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Web.Pages;
using FaraVrcAlbum.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FaraVrcAlbum.Web.Tests;

/// <summary>アーカイブ・タグ・ワールドのページ（ルートの部品が渡す Page を含む）。</summary>
public sealed class RouteContentTests : BunitContext, IAsyncLifetime
{
    private const string Site = "https://album.test/";
    private Handler? _handler;
    private HttpClient? _http;
    private const string Uuid = "00000000-0000-4000-8000-000000000001";
    private const string Key = "2026-10/20261023-213312-345_a1b2c3d4";

    private sealed class Handler(Dictionary<string, string> routes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var found = routes.TryGetValue(request.RequestUri!.AbsolutePath, out var body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new StringContent(body ?? "", System.Text.Encoding.UTF8, "application/json") });
        }
    }

    public RouteContentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // 部品の描画の前に、記事の一覧などを読んで登録する（コンストラクターでは非同期の読み込みを待てないので、ここで行う）
    async Task IAsyncLifetime.InitializeAsync()
    {
        var routes = new Dictionary<string, string>
        {
            ["/data/site.json"] = "{\"siteName\":\"Fara Photo Album\",\"author\":\"Fara\",\"imageBaseUrl\":\"https://i.test/\",\"postsPerPage\":2}",
            ["/data/posts-index.json"] = "[" + string.Join(",", Enumerable.Range(1, 5).Select(i =>
                $"{{\"slug\":\"p{i}\",\"title\":\"記事{i}\",\"publishedAt\":\"2026-10-{20 + i}T12:00:00+09:00\",\"summary\":\"\",\"tags\":[\"night\"],\"photoCount\":0}}")) + "]",
            ["/data/tags.json"] = "[{\"slug\":\"night\",\"name\":\"夜景\",\"postCount\":5}]",
            ["/data/outfits.json"] = "[{\"id\":\"coat-red\",\"name\":\"赤いコート\",\"shop\":\"テスト店\",\"photoCount\":3}]",
            ["/data/outfits/coat-red.json"] = "{\"id\":\"coat-red\",\"name\":\"赤いコート\",\"shop\":\"テスト店\",\"url\":\"https://booth.pm/ja/items/1\",\"photos\":["
                + string.Join(",", Enumerable.Range(1, 3).Select(i => $"{{\"key\":\"{Key}\",\"w\":2,\"h\":1,\"slug\":\"p{i}\"}}")) + "]}",
            ["/data/worlds/" + Uuid + ".json"] = "{\"id\":\"wrld_" + Uuid + "\",\"name\":\"ワールド\",\"photos\":["
                + string.Join(",", Enumerable.Range(1, 3).Select(i => $"{{\"key\":\"{Key}\",\"w\":2,\"h\":1,\"slug\":\"p{i}\"}}")) + "]}",
        };
        _handler = new Handler(routes);
        _http = new HttpClient(_handler) { BaseAddress = new Uri(Site) };
        var store = new AlbumStore(_http);
        Assert.True(await store.LoadAsync());
        Services.AddSingleton(store);
        Services.AddFaraBlazorComponent(options => SiteInfo.Configure(options, store.Settings));
        Services.AddSingleton(MaintenanceInfo.Off);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        _http?.Dispose();
        _handler?.Dispose();
        await base.DisposeAsync();
    }

    [Fact]
    public void TheArchiveShowsTheMonthsPostsInPagesAndNotFoundOtherwise()
    {
        var first = Render<ArchiveContent>(p => p.Add(c => c.Year, 2026).Add(c => c.Month, 10).Add(c => c.Page, 1));
        Assert.Equal("2026年10月の記事", first.Find("h1").TextContent);
        Assert.Equal(2, first.FindAll(".album-card").Count);                              // 1 ページ 2 件（site.json）
        Assert.Equal("archive/2026/10/page/2", first.Find("a[rel=next]").GetAttribute("href"));

        var last = Render<ArchiveContent>(p => p.Add(c => c.Year, 2026).Add(c => c.Month, 10).Add(c => c.Page, 3));
        Assert.Single(last.FindAll(".album-card"));

        Assert.Contains("見つかりません", Render<ArchiveContent>(p => p.Add(c => c.Year, 2026).Add(c => c.Month, 10).Add(c => c.Page, 4)).Markup);
        Assert.Contains("見つかりません", Render<ArchiveContent>(p => p.Add(c => c.Year, 2026).Add(c => c.Month, 10).Add(c => c.Page, 0)).Markup);
        Assert.Contains("見つかりません", Render<ArchiveContent>(p => p.Add(c => c.Year, 2020).Add(c => c.Month, 1).Add(c => c.Page, 1)).Markup);
    }

    [Fact]
    public void TheTagPageShowsTheDisplayNameAndItsPosts()
    {
        var cut = Render<TagContent>(p => p.Add(c => c.Slug, "night").Add(c => c.Page, 1));

        cut.WaitForAssertion(() => Assert.Equal("タグ: 夜景", cut.Find("h1").TextContent));
        Assert.Equal(2, cut.FindAll(".album-card").Count);
        Assert.Equal("tags/night/page/2", cut.Find("a[rel=next]").GetAttribute("href"));
        var missing = Render<TagContent>(p => p.Add(c => c.Slug, "nope").Add(c => c.Page, 1));
        missing.WaitForAssertion(() => Assert.Contains("見つかりません", missing.Markup));
    }

    [Fact]
    public void TheWorldPageShowsTheNameTheVrchatLinkAndThePhotos()
    {
        var cut = Render<WorldContent>(p => p.Add(c => c.Uuid, Uuid).Add(c => c.Page, 1));

        cut.WaitForAssertion(() => Assert.Equal("ワールド", cut.Find("h1").TextContent));
        Assert.Equal("https://vrchat.com/home/world/wrld_" + Uuid + "/info", cut.Find("a.album-external").GetAttribute("href"));
        Assert.Equal(2, cut.FindAll(".album-card").Count);
        Assert.Equal("worlds/" + Uuid + "/page/2", cut.Find("a[rel=next]").GetAttribute("href"));
    }

    [Fact]
    public void AMalformedOrUnknownWorldIsNotFound()
    {
        foreach (var uuid in new[] { "not-a-uuid", "wrld_" + Uuid, "00000000-0000-4000-8000-0000000000ff", "../x" })
        {
            var cut = Render<WorldContent>(p => p.Add(c => c.Uuid, uuid).Add(c => c.Page, 1));
            cut.WaitForAssertion(() => Assert.Contains("見つかりません", cut.Markup));
        }
    }

    // ルートの部品（@page）が、ルートの値を中身の部品へ正しく渡すこと。string の値は @ を付けないとリテラルになる
    [Fact]
    public void TheRouteComponentsPassTheRouteValuesOn()
    {
        var archive = Render<ArchiveRoute>(p => p.Add(c => c.Year, 2026).Add(c => c.Month, 10));
        Assert.Equal("2026年10月の記事", archive.Find("h1").TextContent);
        var archive2 = Render<ArchivePagedRoute>(p => p.Add(c => c.Year, 2026).Add(c => c.Month, 10).Add(c => c.Page, 2));
        Assert.Equal("archive/2026/10", archive2.Find("a[rel=prev]").GetAttribute("href"));

        var tag = Render<TagRoute>(p => p.Add(c => c.Slug, "night"));
        tag.WaitForAssertion(() => Assert.Equal("タグ: 夜景", tag.Find("h1").TextContent));
        var tag2 = Render<TagPagedRoute>(p => p.Add(c => c.Slug, "night").Add(c => c.Page, 2));
        tag2.WaitForAssertion(() => Assert.Equal("tags/night", tag2.Find("a[rel=prev]").GetAttribute("href")));

        var world = Render<WorldRoute>(p => p.Add(c => c.Uuid, Uuid));
        world.WaitForAssertion(() => Assert.Equal("ワールド", world.Find("h1").TextContent));
        var world2 = Render<WorldPagedRoute>(p => p.Add(c => c.Uuid, Uuid).Add(c => c.Page, 2));
        world2.WaitForAssertion(() => Assert.Equal("worlds/" + Uuid, world2.Find("a[rel=prev]").GetAttribute("href")));
    }

    [Fact]
    public void TheOutfitPageShowsTheNameAsALinkToTheShopPageAndThePhotosAndAnUnknownOutfitIsNotFound()
    {
        var cut = Render<OutfitContent>(p => p.Add(c => c.Id, "coat-red").Add(c => c.Page, 1));

        cut.WaitForAssertion(() => Assert.Equal("赤いコート", cut.Find("h1").TextContent.Trim()));
        var link = cut.Find("h1 a");
        Assert.Equal("https://booth.pm/ja/items/1", link.GetAttribute("href"));                          // 名前を押すと、商品ページ（BOOTH）
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer nofollow", link.GetAttribute("rel"));
        Assert.Contains("<dt>お店</dt><dd>テスト店</dd>", cut.Markup.Replace(" ", "").Replace("\n", "").Replace("<dt>お店</dt><dd>テスト店</dd>", "<dt>お店</dt><dd>テスト店</dd>"));
        Assert.Equal(2, cut.FindAll(".album-card").Count);                                                // 1 ページ 2 件
        Assert.Equal("outfits/coat-red/page/2", cut.Find("a[rel=next]").GetAttribute("href"));

        foreach (var id in new[] { "unknown", "Bad_Id", "../x" })
        {
            var notFound = Render<OutfitContent>(p => p.Add(c => c.Id, id).Add(c => c.Page, 1));
            notFound.WaitForAssertion(() => Assert.Contains("見つかりません", notFound.Markup));
        }
    }

    [Fact]
    public void TheOutfitListShowsEachOutfitAsACardLinkingToItsPage()
    {
        var cut = Render<OutfitsPage>();

        cut.WaitForAssertion(() => Assert.Equal("服", cut.Find("h1").TextContent));
        Assert.Equal("outfits/coat-red", cut.Find("a.album-card-link").GetAttribute("href"));
        Assert.Contains("テスト店・3 枚", cut.Markup);
    }
}
