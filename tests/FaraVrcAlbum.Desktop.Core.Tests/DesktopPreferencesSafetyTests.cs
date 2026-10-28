using FaraVrcAlbum.Desktop.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>手元の設定（local/desktop.json）が壊れているときに、ほかの設定を消さないこと。</summary>
public sealed class DesktopPreferencesSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public DesktopPreferencesSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ABrokenFileReadsAsEmptyAndIsNotRewrittenByReading()
    {
        var path = Path.Combine(_dir, "desktop.json");
        File.WriteAllText(path, "{ broken");
        var prefs = new DesktopPreferences(path);

        Assert.Equal("", prefs.DefaultAvatar);
        Assert.Equal("{ broken", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_dir, "desktop.json.broken-*"));
    }

    [Fact]
    public void AWriteToABrokenFileMovesTheBrokenOneAsideFirst()
    {
        var path = Path.Combine(_dir, "desktop.json");
        File.WriteAllText(path, "{ broken");
        var prefs = new DesktopPreferences(path);

        prefs.DefaultAvatar = "avatar-1";

        Assert.Equal("avatar-1", prefs.DefaultAvatar);
        var aside = Assert.Single(Directory.GetFiles(_dir, "desktop.json.broken-*"));
        Assert.Equal("{ broken", File.ReadAllText(aside));                                  // 壊れた中身は別名で残る
    }

    [Fact]
    public void AWriteToAValidFileKeepsTheOtherSettings()
    {
        var path = Path.Combine(_dir, "desktop.json");
        var prefs = new DesktopPreferences(path);
        prefs.BoothDatabase = @"D:\booth\data.db";
        prefs.AvatarFolder = @"D:\unity";

        prefs.DefaultAvatar = "avatar-1";

        Assert.Equal(@"D:\booth\data.db", prefs.BoothDatabase);
        Assert.Equal(@"D:\unity", prefs.AvatarFolder);
        Assert.Equal("avatar-1", prefs.DefaultAvatar);
        Assert.Empty(Directory.GetFiles(_dir, "desktop.json.broken-*"));
    }
}
