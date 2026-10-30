using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Content;

/// <summary>写真の服の解決（ビルドと検査で共用）。</summary>
public static class OutfitRules
{
    /// <summary>
    /// 写真の服（表示する順）。outfits があればそれ（空の配列なら服は無い）。省略なら、アバターの既定の服（outfitIds）。
    /// 登録の無い id は含めない（ContentLoader が記事の検査で止めている）。服が 1 点も無ければ null。
    /// </summary>
    public static List<OutfitRef>? Resolve(PhotoInfo photo, IReadOnlyDictionary<string, AvatarMasterEntry> avatars, IReadOnlyDictionary<string, OutfitMasterEntry> outfits)
    {
        IEnumerable<string> ids = photo.Outfits ?? DefaultOutfitIds(photo, avatars);
        var result = ids.Where(outfits.ContainsKey)
            .Select(id => new OutfitRef { Id = id, Name = outfits[id].Name, Url = outfits[id].Url })
            .ToList();
        return result.Count == 0 ? null : result;
    }

    private static IEnumerable<string> DefaultOutfitIds(PhotoInfo photo, IReadOnlyDictionary<string, AvatarMasterEntry> avatars) =>
        photo.Avatar is { } avatarId && avatars.TryGetValue(avatarId, out var avatar) ? avatar.OutfitIds : Array.Empty<string>();
}
