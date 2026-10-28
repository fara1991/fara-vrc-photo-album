using System.Net;
using FaraVrcAlbum.Web.Services;
using Xunit;

namespace FaraVrcAlbum.Web.Tests;

/// <summary>AlbumStore: 重複したタグ（起動を止めない）と、読めなかったタグ一覧の取り直し。</summary>
public sealed class AlbumStoreSafetyTests
{
    private const string SiteJson = "{\"siteName\":\"Fara Photo Album\",\"author\":\"Fara\",\"imageBaseUrl\":\"https://i.test/\",\"postsPerPage\":2}";

    private sealed class Routes : HttpMessageHandler
    {
        public Dictionary<string, string> Map { get; } = new(StringComparer.Ordinal);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var found = Map.TryGetValue(request.RequestUri!.AbsolutePath, out var body);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound) { Content = new StringContent(body ?? "", System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private static (AlbumStore Store, Routes Routes) Create()
    {
        var routes = new Routes();
        routes.Map["/data/site.json"] = SiteJson;
        routes.Map["/data/posts-index.json"] = "[]";
        return (new AlbumStore(new HttpClient(routes) { BaseAddress = new Uri("https://album.test/") }), routes);
    }

    [Fact]
    public async Task DuplicateTagSlugsDoNotStopStartupAndTheFirstNameWins()
    {
        var (store, routes) = Create();
        routes.Map["/data/tags.json"] = "[{\"slug\":\"night\",\"name\":\"夜景\",\"postCount\":1},{\"slug\":\"night\",\"name\":\"別名\",\"postCount\":2}]";

        Assert.True(await store.LoadAsync());                                            // 例外で起動が止まらない
        Assert.Equal("夜景", store.TagNames["night"]);
    }

    [Fact]
    public async Task AnUnreadTagListIsFetchedAgainTheNextTime()
    {
        var (store, routes) = Create();
        Assert.True(await store.LoadAsync());                                            // タグの一覧は無い（読めない）
        Assert.Null(await store.GetTagsAsync());

        routes.Map["/data/tags.json"] = "[{\"slug\":\"night\",\"name\":\"夜景\",\"postCount\":1}]";
        var tags = await store.GetTagsAsync();                                           // 失敗を覚えず、取り直す
        Assert.NotNull(tags);
        Assert.Single(tags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(500)]
    public async Task AnOutOfRangePostsPerPageIsReplacedWithTheBuildDefaultSoPagingWorks(int perPage)
    {
        var (store, routes) = Create();
        routes.Map["/data/site.json"] = SiteJson.Replace("\"postsPerPage\":2", $"\"postsPerPage\":{perPage}");
        Assert.True(await store.LoadAsync());                                            // 起動は止めない
        Assert.Equal(12, store.Settings.PostsPerPage);
        Assert.Equal(1, store.PageCount);                                                // 0 件の計算で例外にならない
    }

    [Fact]
    public async Task AnUnreadWorldAndOutfitListAreFetchedAgainTheNextTime()
    {
        var (store, routes) = Create();
        Assert.True(await store.LoadAsync());
        Assert.Null(await store.GetWorldsAsync());                                       // 読めなかった null を覚えない
        Assert.Null(await store.GetOutfitsAsync());

        routes.Map["/data/worlds.json"] = "[]";
        routes.Map["/data/outfits.json"] = "[]";
        Assert.NotNull(await store.GetWorldsAsync());                                    // 次に開くとき取り直す
        Assert.NotNull(await store.GetOutfitsAsync());
        Assert.NotNull(await store.GetWorldsAsync());                                    // 読めたものは覚える（同じ一覧）
    }
}
