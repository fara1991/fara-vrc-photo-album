using FaraVrcAlbum.Content;
using Xunit;

namespace FaraVrcAlbum.Content.Tests;

/// <summary>本文の URL の検査: リンクと同じく、写真（画像）の URL も許可された形だけ通す。</summary>
public sealed class MarkdownUrlSafetyTests
{
    [Fact]
    public void AnImageWithAScriptUrlLosesItsSource()
    {
        var html = MarkdownRenderer.ToHtml("![説明](javascript:alert(1))");
        Assert.DoesNotContain("javascript", html);
    }

    [Fact]
    public void AnHttpsImageIsKept()
    {
        var html = MarkdownRenderer.ToHtml("![説明](https://vrc-photos.fara-labs.com/a/2048px.webp)");
        Assert.Contains("https://vrc-photos.fara-labs.com/a/2048px.webp", html);   // 許可された https は通る
    }

    [Fact]
    public void ASiteRelativeImageIsKept()
    {
        var html = MarkdownRenderer.ToHtml("![説明](/images/a.webp)");
        Assert.Contains("src=\"/images/a.webp\"", html);
    }

    // 注: \/ や \\ のような書き方は、Markdig が先に \ を外すので、出力は同じサイトのパス（/example.com/x）になる
    [Theory]
    [InlineData("//example.com/x")]
    [InlineData("/\\example.com/x")]
    public void ProtocolRelativeUrlsWithSlashesOrBackslashesAreRefusedForLinks(string url)
    {
        var html = MarkdownRenderer.ToHtml($"[リンク]({url})");
        Assert.DoesNotContain("example.com", html);
    }

    [Theory]
    [InlineData("//example.com/x.webp")]
    [InlineData("/\\example.com/x.webp")]
    public void ProtocolRelativeUrlsAreRefusedForImagesToo(string url)
    {
        var html = MarkdownRenderer.ToHtml($"![説明]({url})");
        Assert.DoesNotContain("example.com", html);
    }
}
