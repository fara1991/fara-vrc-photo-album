using System.Globalization;
using System.Text;
using System.Xml;

namespace FaraVrcAlbum.Build;

/// <summary>feed.xml（RSS 2.0）。最新 20 件。noindex の記事は入れない（sitemap と同じ扱い）。pubDate は公開日（JST）。</summary>
internal static class FeedWriter
{
    public const int MaxItems = 20;

    public static void Write(string path, SiteModel model, string origin)
    {
        var settings = model.Settings;
        var items = model.Posts.Where(p => !p.Noindex).Take(MaxItems).ToList();
        using var stream = File.Create(path);
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true });
        xml.WriteStartDocument();
        xml.WriteStartElement("rss");
        xml.WriteAttributeString("version", "2.0");
        xml.WriteAttributeString("xmlns", "atom", null, "http://www.w3.org/2005/Atom");
        xml.WriteStartElement("channel");
        xml.WriteElementString("title", settings.SiteName);
        xml.WriteElementString("link", origin + "/");
        xml.WriteElementString("description", "VRChat で撮った写真を、記事にして並べています。");
        xml.WriteElementString("language", "ja");
        xml.WriteStartElement("atom", "link", "http://www.w3.org/2005/Atom");
        xml.WriteAttributeString("href", origin + "/feed.xml");
        xml.WriteAttributeString("rel", "self");
        xml.WriteAttributeString("type", "application/rss+xml");
        xml.WriteEndElement();
        if (items.Count > 0) xml.WriteElementString("lastBuildDate", Rfc822(items.Max(p => p.PublishedAt)));
        foreach (var post in items)
        {
            var url = origin + SiteRules.PostPath(post.Slug);
            xml.WriteStartElement("item");
            xml.WriteElementString("title", post.Title);
            xml.WriteElementString("link", url);
            xml.WriteStartElement("guid");
            xml.WriteAttributeString("isPermaLink", "true");
            xml.WriteString(url);
            xml.WriteEndElement();
            xml.WriteElementString("pubDate", Rfc822(post.PublishedAt));
            xml.WriteElementString("description", SiteModel.SummaryOf(post));
            if (SiteModel.CoverOf(post) is { } cover)
            {
                // enclosure の length は必須だが、画像の実サイズは Build からは分からない（0）
                xml.WriteStartElement("enclosure");
                xml.WriteAttributeString("url", SiteRules.OgImageUrl(settings.PublicImageBaseUrl, cover.Key));
                xml.WriteAttributeString("length", "0");
                xml.WriteAttributeString("type", "image/jpeg");
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    /// <summary>RFC 822 の日時（JST 固定の +0900）。InvariantGlobalization でも同じ結果。</summary>
    public static string Rfc822(DateTimeOffset value) =>
        value.ToOffset(TimeSpan.FromHours(9)).ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " +0900";
}
