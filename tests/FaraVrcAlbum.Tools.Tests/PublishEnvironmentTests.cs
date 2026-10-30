using System.Diagnostics;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>アプリから起動する公開の子プロセスの環境: サンプル記事の関門（ALBUM_ALLOW_SAMPLE）は、親の環境から来ても外さない。</summary>
public sealed class PublishEnvironmentTests
{
    [Fact]
    public void TheSampleGateVariableIsNeverPassedToAPublishChildEvenWhenInheritedFromTheParent()
    {
        var info = new ProcessStartInfo("node");
        info.Environment["ALBUM_ALLOW_SAMPLE"] = "1";                                  // 親の環境から継承された状態
        info.Environment["PATH"] = "x";

        ProcessRunner.ApplyEnvironment(info, new Dictionary<string, string> { ["ALBUM_CONTENT_DIR"] = "C:/content" });

        Assert.False(info.Environment.ContainsKey("ALBUM_ALLOW_SAMPLE"));
        Assert.Equal("C:/content", info.Environment["ALBUM_CONTENT_DIR"]);             // 呼び出し側の指定はそのまま渡す
        Assert.Equal("x", info.Environment["PATH"]);
    }
}
