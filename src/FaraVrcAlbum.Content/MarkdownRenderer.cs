using Markdig;
using Markdig.Extensions.CustomContainers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace FaraVrcAlbum.Content;

/// <summary>
/// 本文の Markdown → HTML。生の HTML は無効（書いてあっても文字として出す）。リンクは http(s)・mailto・サイト内の相対パスだけ。
/// 取り消し線（~~）・下線（++。&lt;ins&gt;）・文字の揃え（:::left / :::center / :::right。CSS クラス album-align-*）に対応する。揃え以外のクラス・属性は出さない。
/// </summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Inserted)   // ~~取り消し線~~・++下線++
        .UseCustomContainers()                                                                     // :::center … :::（文字の揃え）
        .Build();

    public static string ToHtml(string markdown)
    {
        var document = Markdown.Parse(markdown ?? "", Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (!IsAllowedUrl(link.Url)) link.Url = "";
        }
        // <https://…> / <mailto:…> の形（AutolinkInline）も、同じ規則で検査する（<javascript:…> を通さない）
        foreach (var auto in document.Descendants<AutolinkInline>())
        {
            if (!IsAllowedUrl(auto.Url)) auto.Url = "";
        }
        // 文字の揃え（:::left / :::center / :::right）だけを通す。ほかの名前や属性は、HTML に出さない
        foreach (var container in document.Descendants<CustomContainer>())
        {
            var attributes = new HtmlAttributes();
            if (container.Info?.Trim() is "left" or "center" or "right") attributes.AddClass("album-align-" + container.Info.Trim());
            container.SetAttributes(attributes);
        }
        foreach (var inline in document.Descendants<CustomContainerInline>()) inline.SetAttributes(new HtmlAttributes());
        using var writer = new StringWriter();
        var renderer = new Markdig.Renderers.HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString().TrimEnd();
    }

    /// <summary>本文の先頭から、記号を除いた短い要約を作る（記事の summary が無いとき）。</summary>
    public static string PlainSummary(string markdown, int maxLength = 120)
    {
        var text = Markdown.ToPlainText(markdown ?? "", Pipeline);
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength].TrimEnd() + "…";
    }

    private static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        if (url.StartsWith("//", StringComparison.Ordinal)) return false;
        if (url.StartsWith('/') || url.StartsWith('#') || url.StartsWith("./") || url.StartsWith("../")) return true;
        var colon = url.IndexOf(':');
        if (colon < 0) return true; // スキームなしの相対パス
        var scheme = url[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto";
    }
}
