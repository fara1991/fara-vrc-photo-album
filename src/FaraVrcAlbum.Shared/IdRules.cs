using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FaraVrcAlbum;

/// <summary>ワールド ID（wrld_ + UUID。小文字 16 進 8-4-4-4-12）。URL の path には wrld_ を除いた UUID を使う（kit の path は小文字・数字・ハイフンだけ）。</summary>
public static partial class WorldId
{
    [GeneratedRegex(@"^wrld_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")]
    private static partial Regex UuidPattern();

    public static bool IsValid(string? id) => id is not null && IdPattern().IsMatch(id);

    /// <summary>wrld_ + UUID → UUID（URL の 1 区間）。形式が不正なら例外。</summary>
    public static string ToPathSegment(string id) =>
        IsValid(id) ? id["wrld_".Length..] : throw new ArgumentException($"ワールド ID の形式が不正です: {id}", nameof(id));

    /// <summary>UUID（URL の 1 区間）→ wrld_ + UUID。形式が不正なら null。</summary>
    public static string? FromPathSegment(string? segment) =>
        segment is not null && UuidPattern().IsMatch(segment) ? "wrld_" + segment : null;
}

/// <summary>VRChat のリンクの組み立て（形式が変わってもここだけ直す）。</summary>
public static partial class VrchatLinks
{
    public static string World(string worldId) =>
        WorldId.IsValid(worldId) ? $"https://vrchat.com/home/world/{worldId}/info" : throw new ArgumentException($"ワールド ID の形式が不正です: {worldId}", nameof(worldId));

    // VRChat の API が返すワールドの画像の URL（imageUrl）。file_ の ID と版だけを取り出す
    [GeneratedRegex(@"^https://api\.vrchat\.cloud/api/1/file/(file_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/([1-9][0-9]{0,5})/file$")]
    private static partial Regex ImageUrlPattern();

    /// <summary>imageUrl が、VRChat の API のワールドの画像の形か。</summary>
    public static bool IsWorldImageUrl(string? imageUrl) => imageUrl is not null && ImageUrlPattern().IsMatch(imageUrl);

    /// <summary>R2 に置くワールドの画像のキー（worlds/{uuid}/{file_…}_{版}）。形式が違えば例外。</summary>
    public static string WorldImageKey(string worldId, string imageUrl)
    {
        var m = ImageUrlPattern().Match(imageUrl);
        if (!m.Success) throw new ArgumentException($"ワールドの画像の URL の形式が不正です: {imageUrl}", nameof(imageUrl));
        return $"worlds/{WorldId.ToPathSegment(worldId)}/{m.Groups[1].Value}_{m.Groups[2].Value}";
    }

    [GeneratedRegex(@"^worlds/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})/file_[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}_[1-9][0-9]{0,5}$")]
    private static partial Regex ImageKeyPattern();

    /// <summary>R2 のワールドの画像のキーが、そのワールドのものとして正しい形か。</summary>
    public static bool IsWorldImageKey(string worldId, string? key)
    {
        var m = key is null ? null : ImageKeyPattern().Match(key);
        return m is { Success: true } && WorldId.IsValid(worldId) && m.Groups[1].Value == WorldId.ToPathSegment(worldId);
    }

    /// <summary>R2 に置くワールドの画像の幅（_512.webp・_1024.webp）。</summary>
    public static readonly int[] WorldImageWidths = [512, 1024];
}
