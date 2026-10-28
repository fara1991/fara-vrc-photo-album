using NetVips;

namespace FaraVrcAlbum.Images;

/// <summary>変換したワールドの画像。W/H は _1024 版の寸法（元が小さければ元の寸法）。</summary>
public sealed record EncodedWorldImage(int W, int H, IReadOnlyList<EncodedVariant> Variants);

/// <summary>
/// VRChat のワールドの画像（PNG など）→ WebP 512/1024。拡大はしない。メタデータはすべて除く。
/// </summary>
public static class WorldImageEncoder
{
    private static readonly (int Width, int Quality)[] Sizes = [(512, 78), (1024, 80)];

    public static EncodedWorldImage Encode(byte[] image)
    {
        using var source = Image.NewFromBuffer(image);
        using var srgb = source.Colourspace(Enums.Interpretation.Srgb);
        var variants = new List<EncodedVariant>();
        int w = srgb.Width, h = srgb.Height;
        foreach (var (width, quality) in Sizes)
        {
            var scale = Math.Min(1.0, width / (double)srgb.Width);
            using var resized = scale < 1.0 ? srgb.Resize(scale, kernel: Enums.Kernel.Lanczos3) : srgb.Copy();
            var data = resized.WebpsaveBuffer(q: quality, keep: Enums.ForeignKeep.None);
            MetadataGuard.EnsureStripped(data, "image/webp");
            variants.Add(new EncodedVariant(width.ToString(), $"_{width}.webp", "image/webp", data));
            if (width == 1024)
            {
                w = resized.Width;
                h = resized.Height;
            }
        }
        return new EncodedWorldImage(w, h, variants);
    }
}
