using System.Reflection;
using FaraVrcAlbum.Desktop.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>「このアプリについて」に出すバージョン: ビルドに埋め込まれた版の読み取りと、表示。コミットは出さない・残さない。</summary>
public sealed class AppInfoTests
{
    [Theory]
    [InlineData("1.2.3+abcdef0123456789", "1.2.3")]
    [InlineData("1.2.3-beta.1+deadbeef", "1.2.3-beta.1")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3+", "1.2.3")]
    [InlineData("0.0.0-dev+1a2b3c4d", "0.0.0-dev")]
    [InlineData("", "0.0.0-dev")]
    [InlineData("   ", "0.0.0-dev")]
    [InlineData(null, "0.0.0-dev")]
    [InlineData("+abcdef0", "0.0.0-dev")]
    public void バージョン情報から_版の番号だけを取り出せる(string? informationalVersion, string expected)
        => Assert.Equal(expected, AppInfo.ParseInformationalVersion(informationalVersion));

    [Theory]
    [InlineData("1.2.3", "v1.2.3")]
    [InlineData("1.2.3-beta.1", "v1.2.3-beta.1")]
    [InlineData("0.0.0-dev", "開発版")]
    public void 表示用のバージョンは_開発版とリリース版で書き分ける(string version, string expected)
        => Assert.Equal(expected, AppInfo.FormatDisplayVersion(version));

    [Fact]
    public void アセンブリに埋め込まれたバージョンを表示用にできる()
    {
        var display = AppInfo.DisplayVersion(Assembly.GetExecutingAssembly());

        Assert.False(string.IsNullOrWhiteSpace(display));
        Assert.DoesNotContain("+", display);
        Assert.DoesNotContain("コミット", display);
    }
}
