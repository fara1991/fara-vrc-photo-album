using FaraVrcAlbum.Models;
using Xunit;

namespace FaraVrcAlbum.Content.Tests;

public class SiteRulesTests
{
    [Theory]
    [InlineData("a", true)]
    [InlineData("2026-10-night-garden", true)]
    [InlineData("sample-first-post", true)]
    [InlineData("", false)]
    [InlineData("Night", false)]
    [InlineData("night_garden", false)]
    [InlineData("night--garden", false)]
    [InlineData("-night", false)]
    [InlineData("night-", false)]
    [InlineData("夜景", false)]
    [InlineData("a/b", false)]
    public void SlugRule(string slug, bool expected) => Assert.Equal(expected, SiteRules.IsValidSlug(slug));

    [Theory]
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", true)]
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4-r2", true)]
    [InlineData("2026-10/20261023-213312-345_A1B2C3D4", false)]
    [InlineData("2026-10/20261023-213312-345_a1b2c3d", false)]
    [InlineData("2026-10/20261023-213312_a1b2c3d4", false)]
    [InlineData("20261023-213312-345_a1b2c3d4", false)]
    [InlineData("../2026-10/20261023-213312-345_a1b2c3d4", false)]
    public void PhotoKeyRule(string key, bool expected) => Assert.Equal(expected, SiteRules.IsValidPhotoKey(key));

    [Fact]
    public void ImageUrlsFollowTheKeyAndSize()
    {
        Assert.Equal("https://vrc-photos.fara-labs.com/2026-10/20261023-213312-345_a1b2c3d4_1280.webp",
            SiteRules.ImageUrl("https://vrc-photos.fara-labs.com/", "2026-10/20261023-213312-345_a1b2c3d4", 1280));
        Assert.Equal("https://vrc-photos.fara-labs.com/k_og.jpg", SiteRules.OgImageUrl("https://vrc-photos.fara-labs.com/", "k"));
    }

    [Theory]
    [InlineData(0, 12, 1)]
    [InlineData(1, 12, 1)]
    [InlineData(12, 12, 1)]
    [InlineData(13, 12, 2)]
    [InlineData(300, 12, 25)]
    public void PageCountRoundsUpAndIsAtLeastOne(int total, int perPage, int expected) => Assert.Equal(expected, SiteRules.PageCount(total, perPage));

    [Fact]
    public void SliceReturnsThePageAndEmptyOutOfRange()
    {
        var items = Enumerable.Range(1, 25).ToList();
        Assert.Equal(Enumerable.Range(1, 12), SiteRules.Slice(items, 1, 12));
        Assert.Equal(Enumerable.Range(13, 12), SiteRules.Slice(items, 2, 12));
        Assert.Equal([25], SiteRules.Slice(items, 3, 12));
        Assert.Empty(SiteRules.Slice(items, 4, 12));
        Assert.Empty(SiteRules.Slice(items, 0, 12));
    }

    [Fact]
    public void PageLinksAreRelativeAndPathsStartWithASlash()
    {
        Assert.Equal("./", SiteRules.HomeHref(1));
        Assert.Equal("page/2", SiteRules.HomeHref(2));
        Assert.Equal("posts/x", SiteRules.PostHref("x"));
        Assert.Equal("/", SiteRules.HomePath(1));
        Assert.Equal("/page/3", SiteRules.HomePath(3));
        Assert.Equal("/posts/x", SiteRules.PostPath("x"));
    }

    [Fact]
    public void DatesAreFormattedInJstWithoutCulture()
    {
        // UTC の 15:30 は JST では翌日 0:30
        Assert.Equal("2026年10月25日", SiteRules.FormatDate(new DateTimeOffset(2026, 10, 24, 15, 30, 0, TimeSpan.Zero)));
        Assert.Equal("2026年10月24日", SiteRules.FormatDate(new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(9))));
        Assert.Equal("2026-10-25", SiteRules.FormatIsoDate(new DateTimeOffset(2026, 10, 24, 15, 30, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData("https://vrc-photos.fara-labs.com/", "https://vrc-photos.fara-labs.com/")]
    [InlineData("https://vrc-photos.fara-labs.com", "https://vrc-photos.fara-labs.com/")]
    [InlineData("https://example.test/img/", "https://example.test/img/")]
    public void ImageBaseUrlIsNormalisedWithATrailingSlash(string value, string expected) => Assert.Equal(expected, SiteRules.NormalizeImageBaseUrl(value));

    [Theory]
    [InlineData("")]
    [InlineData("http://vrc-photos.fara-labs.com/")]
    [InlineData("/img/")]
    [InlineData("https://x.test/?a=1")]
    public void ImageBaseUrlMustBeHttps(string value) => Assert.Throws<InvalidDataException>(() => SiteRules.NormalizeImageBaseUrl(value));

    [Fact]
    public void TheShootingTimeIsShownInJstOrNullWhenUnreadable()
    {
        Assert.Equal("2026年10月23日 21:33", SiteRules.FormatTakenAt("2026-10-23T21:33:12+09:00"));
        Assert.Equal("2026年10月24日 00:05", SiteRules.FormatTakenAt("2026-10-23T15:05:00Z"));         // UTC → JST（日付をまたぐ）
        Assert.Null(SiteRules.FormatTakenAt(null));
        Assert.Null(SiteRules.FormatTakenAt(""));
        Assert.Null(SiteRules.FormatTakenAt("昨日"));
    }

    [Fact]
    public void TheArchiveMenuIsNewestYearAndMonthFirstAndDropsMalformedEntries()
    {
        var counts = new Dictionary<string, Dictionary<string, int>>
        {
            ["2025"] = new() { ["12"] = 1, ["11"] = 4 },
            ["2026"] = new() { ["09"] = 2, ["10"] = 12, ["13"] = 5, ["xx"] = 1, ["08"] = 0 },
            ["abc"] = new() { ["01"] = 1 },
        };

        var years = SiteRules.ArchiveYears(counts);

        Assert.Equal([2026, 2025], years.Select(y => y.Year));
        Assert.Equal([(10, 12), (9, 2)], years[0].Months.Select(m => (m.Month, m.Count)));      // 月の形式が不正・0 件は捨てる
        Assert.Equal([(12, 1), (11, 4)], years[1].Months.Select(m => (m.Month, m.Count)));
        Assert.Equal(14, years[0].Count);
        Assert.Empty(SiteRules.ArchiveYears(null));
    }

    private static List<PhotoInfo> Photos(params (int W, int H)[] sizes) => sizes.Select(s => new PhotoInfo { W = s.W, H = s.H }).ToList();

    [Fact]
    public void GallerySizesFollowTheLayoutByPhotoCount()
    {
        // 1 枚: 本文の幅（896px）まで。縦長は高さの上限（78vh）で狭い
        Assert.Equal("(min-width: 768px) min(896px, calc(78vh * 1.7778)), 92vw", SiteRules.GallerySizes(Photos((2048, 1152)), 0));
        Assert.Equal("(min-width: 768px) min(896px, calc(78vh * 0.5625)), 92vw", SiteRules.GallerySizes(Photos((1152, 2048)), 0));
        // 2 枚: 1 行を縦横比で分け合う（同じ縦横比なら 440px ずつ）
        var two = Photos((2048, 1152), (2048, 1152));
        Assert.Equal("(min-width: 768px) 444px, 92vw", SiteRules.GallerySizes(two, 0));
        Assert.Equal(SiteRules.GallerySizes(two, 0), SiteRules.GallerySizes(two, 1));
        // 3 枚以上: 縦横比 × 340px（150〜896px）。縦長・正方形は 640 版で足り、パノラマは広い
        var many = Photos((1152, 2048), (1600, 1600), (2048, 1152), (2048, 683));
        Assert.Equal(["191px", "340px", "604px", "896px"], many.Select((_, i) => SiteRules.GallerySizes(many, i).Split(' ')[2].TrimEnd(',')));
        Assert.All(many.Select((_, i) => SiteRules.GallerySizes(many, i)), s => Assert.EndsWith(", 92vw", s));
    }
}
