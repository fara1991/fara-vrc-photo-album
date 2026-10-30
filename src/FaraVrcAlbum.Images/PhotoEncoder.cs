using NetVips;

namespace FaraVrcAlbum.Images;

/// <summary>1 つの出力ファイル（R2 のオブジェクト）。Suffix は写真のキーに付ける部分（_640.webp など）。</summary>
public sealed record EncodedVariant(string Name, string Suffix, string ContentType, byte[] Data);

/// <summary>変換の結果。W/H は 2048 版の寸法（元が小さければ元の寸法）。Color は代表色（6 桁の 16 進）。</summary>
public sealed record EncodedPhoto(int W, int H, string Color, IReadOnlyList<EncodedVariant> Variants);

/// <summary>
/// PNG → WebP 640/1280/2048（計画書 §5.2）。OGP（_og.jpg）はカバー写真だけ <see cref="EncodeOg"/> で別に作る。
/// 拡大はしない。メタデータはすべて除く（keep = None）。出力は同じ入力なら同じバイト列になる。
/// </summary>
public static class PhotoEncoder
{
    /// <summary>変換設定の世代（台帳の encoder）。設定を変えたら上げる（既存のキーは変えない）。</summary>
    public const int Generation = 1;

    public const int OgWidth = 1200;
    public const int OgHeight = 630;

    private static readonly (int Size, int Quality)[] WebpSizes = [(640, 76), (1280, 78), (2048, 80)];

    public static EncodedPhoto Encode(byte[] png)
    {
        using var source = Image.NewFromBuffer(png);
        using var srgb = source.Colourspace(Enums.Interpretation.Srgb);
        var longEdge = Math.Max(srgb.Width, srgb.Height);

        var variants = new List<EncodedVariant>();
        int w2048 = srgb.Width, h2048 = srgb.Height;
        foreach (var (size, quality) in WebpSizes)
        {
            var scale = Math.Min(1.0, size / (double)longEdge);
            using var resized = scale < 1.0 ? srgb.Resize(scale, kernel: Enums.Kernel.Lanczos3) : srgb.Copy();
            var data = resized.WebpsaveBuffer(q: quality, keep: Enums.ForeignKeep.None);
            variants.Add(new EncodedVariant(size.ToString(), $"_{size}.webp", "image/webp", data));
            if (size == 2048)
            {
                w2048 = resized.Width;
                h2048 = resized.Height;
            }
        }

        foreach (var variant in variants)
        {
            MetadataGuard.EnsureStripped(variant.Data, variant.ContentType);
        }

        return new EncodedPhoto(w2048, h2048, AverageColor(srgb), variants);
    }

    /// <summary>
    /// カバー写真だけの OGP 画像（_og.jpg）。写真の登録時には作らない（表紙は記事を書くときに決まるため）。
    /// `album cover` が元の PNG から作る。
    /// </summary>
    public static EncodedVariant EncodeOg(byte[] png)
    {
        using var source = Image.NewFromBuffer(png);
        using var srgb = source.Colourspace(Enums.Interpretation.Srgb);
        var data = OgImage(srgb);
        MetadataGuard.EnsureStripped(data, "image/jpeg");
        return new EncodedVariant("og", "_og.jpg", "image/jpeg", data);
    }

    /// <summary>OGP 用（中央で 1200×630 に切り抜く。元が小さければ拡大する）。平坦化して JPEG にする。</summary>
    private static byte[] OgImage(Image srgb)
    {
        var scale = Math.Max(OgWidth / (double)srgb.Width, OgHeight / (double)srgb.Height);
        using var scaled = srgb.Resize(scale, kernel: Enums.Kernel.Lanczos3);
        using var flat = scaled.HasAlpha() ? scaled.Flatten(background: new double[] { 0, 0, 0 }) : scaled.Copy();
        var width = Math.Min(OgWidth, flat.Width);
        var height = Math.Min(OgHeight, flat.Height);
        using var cropped = flat.Crop((flat.Width - width) / 2, (flat.Height - height) / 2, width, height);
        return cropped.JpegsaveBuffer(q: 82, keep: Enums.ForeignKeep.None);
    }

    /// <summary>代表色: 1 画素に縮小した平均（RGB の 16 進 6 桁）。</summary>
    private static string AverageColor(Image srgb)
    {
        using var one = srgb.Shrink(srgb.Width, srgb.Height);
        var px = one.Getpoint(0, 0);
        return string.Concat(px.Take(3).Select(v => Math.Clamp((int)Math.Round(v), 0, 255).ToString("x2")));
    }
}
