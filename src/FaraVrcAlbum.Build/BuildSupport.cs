using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Build;

/// <summary>tools/site.json（kit の build-pages の設定）。ここから本番と確認用のホスト名を読む。</summary>
internal sealed class SiteConfigFile
{
    public required JsonObject Node { get; init; }
    public required string Origin { get; init; }
    public required string DevOrigin { get; init; }

    public static SiteConfigFile Read(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new InvalidDataException("tools/site.json が空です");
        string Text(string name) => node[name]?.GetValue<string>() ?? throw new InvalidDataException($"tools/site.json に {name} がありません");
        return new SiteConfigFile { Node = node, Origin = Text("origin").TrimEnd('/'), DevOrigin = Text("devOrigin").TrimEnd('/') };
    }
}

/// <summary>kit の build-pages に渡す site.json。tools/site.json に、画像の配信元（ImageBaseUrl のホスト）を CSP の img-src と connect-src に足す。</summary>
internal static class KitSiteConfig
{
    /// <summary>notFoundHtml は 404.html の本文（フッター入り。PageCatalog.NotFoundBodyHtml）。kit は、これが無いと 404 の本文を空にする。</summary>
    public static string Build(SiteConfigFile config, SiteSettings settings, string notFoundHtml)
    {
        var node = (JsonObject)config.Node.DeepClone();
        var notFound = node["notFoundPage"] as JsonObject ?? new JsonObject();
        notFound["bodyHtml"] = notFoundHtml;
        node["notFoundPage"] = notFound;
        var csp = node["csp"] as JsonObject ?? throw new InvalidDataException("tools/site.json に csp がありません");
        var origin = ImageOrigin(settings.PublicImageBaseUrl);
        // img-src: 写真（R2）。connect-src は tools/site.json の値だけ（記事・ワールドの JSON は同じオリジンから読む）
        var images = csp["imgSrc"] as JsonArray ?? new JsonArray();
        if (!images.Any(x => x?.GetValue<string>() == origin)) images.Add(origin);
        csp["imgSrc"] = images;
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>https://ホスト（パスなし）。kit の CSP の欄はこの形だけ受ける。</summary>
    public static string ImageOrigin(string imageBaseUrl) => "https://" + new Uri(imageBaseUrl).Authority;
}

/// <summary>_headers への追記。</summary>
internal static class HeadersFile
{
    /// <summary>
    /// /data/*（記事・ワールドの JSON などのデータ全体）に X-Robots-Tag: noindex を足す。本番は /* に noindex が無いので、ここで足さないと検索に載りうる。
    /// 確認用は /* に付いているが、同じ規則を足しておく（環境で _headers の形を変えない）。ルールは 1 つ増える（上限 100）。
    /// </summary>
    public static void AppendNoindexRules(string path)
    {
        var text = File.ReadAllText(path);
        if (!text.EndsWith('\n')) text += "\n";
        text += "\n# データの JSON は検索に載せない。robots.txt では Disallow しない（noindex を読ませるため）\n"
              + "/data/*\n  X-Robots-Tag: noindex\n";
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
