using Xunit;

namespace FaraVrcAlbum.Content.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void RendersMarkdown()
    {
        var html = MarkdownRenderer.ToHtml("これは **強調** です。\n\n- a\n- b");
        Assert.Contains("<strong>強調</strong>", html);
        Assert.Contains("<li>a</li>", html);
    }

    [Fact]
    public void RawHtmlIsEscapedNotExecuted()
    {
        var html = MarkdownRenderer.ToHtml("<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>");
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JaVaScRiPt:alert(1))")]
    [InlineData("[x](data:text/html;base64,AAAA)")]
    [InlineData("[x](//evil.test/)")]
    public void DangerousLinksAreRemoved(string markdown)
    {
        var html = MarkdownRenderer.ToHtml(markdown);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("evil.test", html);
    }

    [Theory]
    [InlineData("<javascript:alert(1)>")]
    [InlineData("<JAVASCRIPT:alert(1)>")]
    [InlineData("<data:text/html;base64,AAAA>")]
    public void DangerousAutolinksAreRemoved(string markdown)
    {
        var html = MarkdownRenderer.ToHtml(markdown);
        Assert.DoesNotContain("javascript:", html.Replace("&#58;", ":"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=\"data:", html);
    }

    [Theory]
    [InlineData("<https://example.test/a>", "https://example.test/a")]
    [InlineData("<mailto:a@example.test>", "mailto:a@example.test")]
    public void SafeAutolinksAreKept(string markdown, string url) => Assert.Contains("href=\"" + url + "\"", MarkdownRenderer.ToHtml(markdown));

    [Theory]
    [InlineData("[x](https://example.test/)", "https://example.test/")]
    [InlineData("[x](/posts/a)", "/posts/a")]
    [InlineData("[x](posts/a)", "posts/a")]
    [InlineData("[x](mailto:a@example.test)", "mailto:a@example.test")]
    public void SafeLinksAreKept(string markdown, string url) => Assert.Contains("href=\"" + url + "\"", MarkdownRenderer.ToHtml(markdown));

    [Fact]
    public void SummaryIsPlainTextAndTruncated()
    {
        var summary = MarkdownRenderer.PlainSummary("# 見出し\n\n" + new string('あ', 300), 120);
        Assert.StartsWith("見出し", summary);
        Assert.DoesNotContain("#", summary);
        Assert.Equal(121, summary.Length);
        Assert.EndsWith("…", summary);
        Assert.Equal("短い", MarkdownRenderer.PlainSummary("短い"));
    }

    [Fact]
    public void StrikethroughUnderlineAndAlignmentAreRenderedAndOnlyTheAlignmentClassesAreAllowed()
    {
        var html = MarkdownRenderer.ToHtml("""
            ~~消す~~ と ++下線++

            :::center
            中央の文
            :::

            :::right
            右の文
            :::

            :::danger onclick=x
            悪い名前
            :::
            """);
        Assert.Contains("<del>消す</del>", html);
        Assert.Contains("<ins>下線</ins>", html);
        Assert.Contains("<div class=\"album-align-center\">", html);
        Assert.Contains("<div class=\"album-align-right\">", html);
        Assert.DoesNotContain("danger", html);                                          // 揃え以外のクラスは出さない
        Assert.DoesNotContain("onclick", html);
    }
}
