using FaraVrcAlbum.Desktop.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>壊れた設定（desktop.json）の退避: 退避先の名前は衝突せず、退避したことを知らせる。</summary>
public sealed class PreferencesRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fara-prefs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ABrokenSettingsFileIsMovedAsideWithAUniqueNameAndTheUserIsTold()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "desktop.json");
        var notified = new List<string>();
        Action<string> handler = notified.Add;
        DesktopPreferences.Recovered += handler;
        try
        {
            // 同じ時刻（ms まで）に 2 回壊れても、退避先が衝突しない
            for (var i = 0; i < 2; i++)
            {
                File.WriteAllText(path, "{ broken " + i);
                new DesktopPreferences(path).DefaultAvatar = "avatar-" + i;
            }
        }
        finally
        {
            DesktopPreferences.Recovered -= handler;
        }

        var asides = Directory.GetFiles(_dir, "desktop.json.broken-*");
        Assert.Equal(2, asides.Length);
        Assert.Equal(2, notified.Count);                          // 退避したことを知らせている
        Assert.Equal("avatar-1", new DesktopPreferences(path).DefaultAvatar);
    }
}
