using System.Text.RegularExpressions;

namespace FaraVrcAlbum.Images;

/// <summary>
/// アップロード済みの写真の台帳に、アバターの控えが無いとき（ソフトを入れる前に取り込んだ・形式が違った）の補い:
/// 元の写真（VRChat の写真格納フォルダの「年-月」フォルダ）の XMP を読み直して、アバターを取り出す。
/// fara-vrc-photo-server の旧形式（faraxml。v1.0.0）も新形式（faratag。v1.0.1 から）も <see cref="XmpReader"/> が読む。読めなければ null（例外にしない）。
/// </summary>
public static partial class SourceAvatar
{
    [GeneratedRegex(@"^VRChat_(\d{4})-(\d{2})-\d{2}_", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();

    /// <summary>元の写真（<paramref name="sourceRoot"/>\年-月\<paramref name="sourceName"/>）の XMP から、アバター名と ID を読む。</summary>
    public static (string? Name, string? Id)? Read(string? sourceRoot, string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(sourceName)) return null;
        var match = FileNamePattern().Match(sourceName);
        if (!match.Success) return null;
        try
        {
            var path = Path.Combine(sourceRoot, $"{match.Groups[1].Value}-{match.Groups[2].Value}", Path.GetFileName(sourceName));
            if (!File.Exists(path)) return null;
            var xml = PngScanner.ScanFile(path)?.XmpText;
            if (xml is null) return null;
            var info = XmpReader.Parse(xml);
            return string.IsNullOrWhiteSpace(info.AvatarName) && info.AvatarId is null ? null : (info.AvatarName, info.AvatarId);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
