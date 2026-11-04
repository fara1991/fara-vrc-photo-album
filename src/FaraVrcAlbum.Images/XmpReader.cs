using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace FaraVrcAlbum.Images;

/// <summary>VRChat の写真の XMP から読む項目（必要なものだけ）。形式が不正な ID は null にする。</summary>
/// <param name="WorldNameRaw">XMP に書かれていたワールド名（整える前）。無い（null/空）と、あるが使えない（制御文字・長すぎる）を区別するため。</param>
/// <param name="AvatarName">fara-vrc-photo-server が追記した、撮影時のアバター名（faratag:AvatarName。v1.0.0 の旧形式 faraxml:AvatarName も読む。無ければ null）。</param>
/// <param name="AvatarId">同じく、アバターの ID（faratag:AvatarID。旧形式 faraxml:AvatarId も読む。分かったときだけ）。</param>
public sealed record XmpInfo(string? WorldId, string? WorldName, DateTimeOffset? CreateDate, string? WorldNameRaw = null, string? AvatarName = null, string? AvatarId = null);

/// <summary>
/// XMP を名前空間の URI と要素名（ローカル名）で探す。属性の形でも子要素の形でも読む（書き方が変わっても取れるように）。
/// インスタンス ID・位置など公開しない値は、ここでは読まない。
/// </summary>
public static partial class XmpReader
{
    public const string VrcNamespace = "http://ns.vrchat.com/vrc/1.0/";
    public const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";

    /// <summary>fara-vrc-photo-server（v1.0.1 から）が写真に追記する、撮影時のアバターの情報の名前空間（faratag。rdf:Description の子要素 AvatarName・AvatarID・Source・Version）。</summary>
    public const string FaraTagNamespace = "http://fara-labs.com/ns/faratag/1.0/";

    /// <summary>fara-vrc-photo-server v1.0.0 の旧形式の名前空間（faraxml。rdf:Description の属性）。読むだけ。</summary>
    public const string FaraXmlNamespace = "http://fara-labs.com/ns/faraxml/1.0/";

    public static XmpInfo Parse(string xml)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        var doc = XDocument.Load(reader);

        var worldId = Find(doc, VrcNamespace, "WorldID");
        var createDate = Find(doc, XmpNamespace, "CreateDate");
        var worldNameRaw = Find(doc, VrcNamespace, "WorldDisplayName");
        return new XmpInfo(
            FaraVrcAlbum.WorldId.IsValid(worldId) ? worldId : null,
            // ワールド名は共通の表示名の規則で整える（制御・書式文字を除き、長すぎるものは空）
            DisplayName.Clean(worldNameRaw),
            ParseCreateDate(createDate),
            worldNameRaw,
            DisplayName.Clean(Find(doc, FaraTagNamespace, "AvatarName") ?? Find(doc, FaraXmlNamespace, "AvatarName"), 200),
            (Find(doc, FaraTagNamespace, "AvatarID") ?? Find(doc, FaraTagNamespace, "AvatarId") ?? Find(doc, FaraXmlNamespace, "AvatarId") ?? Find(doc, FaraXmlNamespace, "AvatarID")) is { Length: <= 60 } id
                && id.StartsWith("avtr_", StringComparison.Ordinal) ? id : null);
    }

    /// <summary>
    /// CreateDate。オフセットが無ければ JST とみなす（VRChat の写真は JST の時刻）。読めなければ null。
    /// </summary>
    public static DateTimeOffset? ParseCreateDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (HasOffset().IsMatch(text))
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset) ? withOffset : null;
        }
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(local, PhotoKeys.Jst)
            : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"(Z|[+-]\d{2}:?\d{2})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex HasOffset();

    /// <summary>名前空間とローカル名で最初の値を探す（要素の値・rdf:Seq / rdf:Bag の最初の項目・属性の値）。無ければ null。</summary>
    private static string? Find(XDocument doc, string ns, string local)
    {
        foreach (var element in doc.Descendants().Where(e => e.Name.NamespaceName == ns && e.Name.LocalName == local))
        {
            var li = element.Descendants().FirstOrDefault(d => d.Name.LocalName == "li");
            var text = (li?.Value ?? element.Value).Trim();
            if (text.Length > 0) return text;
        }
        foreach (var attribute in doc.Descendants().Attributes().Where(a => a.Name.NamespaceName == ns && a.Name.LocalName == local))
        {
            var text = attribute.Value.Trim();
            if (text.Length > 0) return text;
        }
        return null;
    }
}
