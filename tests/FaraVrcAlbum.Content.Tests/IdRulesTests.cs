using Xunit;

namespace FaraVrcAlbum.Content.Tests;

public class IdRulesTests
{
    private const string Uuid = "951b8ef4-bc5a-47c5-a156-2b62057d313f";

    [Fact]
    public void WorldIdConvertsToAndFromThePathSegment()
    {
        Assert.Equal(Uuid, WorldId.ToPathSegment("wrld_" + Uuid));
        Assert.Equal("wrld_" + Uuid, WorldId.FromPathSegment(Uuid));
        Assert.Equal("worlds/" + Uuid, SiteRules.WorldHref("wrld_" + Uuid));
        Assert.Equal("worlds/" + Uuid + "/page/3", SiteRules.WorldHref("wrld_" + Uuid, 3));
        Assert.Equal("/worlds/" + Uuid, SiteRules.WorldPath("wrld_" + Uuid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrld_")]
    [InlineData("wrld_951B8EF4-BC5A-47C5-A156-2B62057D313F")]      // 大文字
    [InlineData("wrld_951b8ef4bc5a47c5a1562b62057d313f")]          // ハイフンなし
    [InlineData("wrld_951b8ef4-bc5a-47c5-a156-2b62057d313f:123")]  // インスタンス ID つき
    [InlineData("usr_00000000-0000-0000-0000-000000000000")]
    public void AnythingButWrldAndAUuidIsRejected(string id)
    {
        Assert.False(WorldId.IsValid(id));
        Assert.Throws<ArgumentException>(() => WorldId.ToPathSegment(id));
        Assert.Throws<ArgumentException>(() => VrchatLinks.World(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrld_" + Uuid)]
    [InlineData("951b8ef4-bc5a-47c5-a156-2b62057d313")]
    [InlineData("../951b8ef4-bc5a-47c5-a156-2b62057d313f")]
    [InlineData("951B8EF4-BC5A-47C5-A156-2B62057D313F")]
    public void APathSegmentThatIsNotAUuidIsNull(string segment) => Assert.Null(WorldId.FromPathSegment(segment));

    [Fact]
    public void ArchiveMonthsAreByJstAndPathsAreZeroPadded()
    {
        // 2026-10-31T15:30Z = JST 2026-11-01 00:30 → 11 月
        Assert.Equal((2026, 11), SiteRules.MonthOf(new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero)));
        Assert.Equal((2026, 10), SiteRules.MonthOf(new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.FromHours(9))));
        Assert.Equal((2026, 11), SiteRules.MonthOf(new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.FromHours(9))));
        Assert.Equal((2027, 1), SiteRules.MonthOf(new DateTimeOffset(2026, 12, 31, 15, 0, 0, TimeSpan.Zero)));      // 年の境目も JST
        Assert.Equal("archive/2026/09", SiteRules.ArchiveHref(2026, 9));
        Assert.Equal("/archive/2026/09/page/2", SiteRules.ArchivePath(2026, 9, 2));
        Assert.Equal("tags/night", SiteRules.TagHref("night"));
        Assert.Equal("/tags/night/page/2", SiteRules.TagPath("night", 2));
    }

    [Fact]
    public void PhotoKeyTimeIsJstAndMonthFolderIsChecked()
    {
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 21, 33, 12, TimeSpan.FromHours(9)), SiteRules.PhotoKeyTime("2026-10/20261023-213312-345_a1b2c3d4"));
        Assert.True(SiteRules.PhotoKeyMonthMatches("2026-10/20261023-213312-345_a1b2c3d4"));
        Assert.False(SiteRules.PhotoKeyMonthMatches("2026-09/20261023-213312-345_a1b2c3d4"));
    }
}
