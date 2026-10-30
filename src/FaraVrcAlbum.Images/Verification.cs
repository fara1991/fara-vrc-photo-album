using System.Buffers.Binary;
using System.Text;

namespace FaraVrcAlbum.Images;

/// <summary>WebP の寸法とチャンク（検査用）。</summary>
public static class WebpInspector
{
    /// <summary>WebP の寸法（VP8X / VP8L / VP8）。WebP でなければ null。先頭 30 バイトほどがあれば足りる。</summary>
    public static (int Width, int Height)? Dimensions(ReadOnlySpan<byte> data)
    {
        if (!IsWebp(data) || data.Length < 30) return null;
        var fourcc = Encoding.ASCII.GetString(data.Slice(12, 4));
        switch (fourcc)
        {
            case "VP8X":
                return (1 + ReadU24(data.Slice(24, 3)), 1 + ReadU24(data.Slice(27, 3)));
            case "VP8L":
                if (data[20] != 0x2F) return null;
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(21, 4));
                return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            case "VP8 ":
                if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A) return null;
                return (BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF);
            default:
                return null;
        }
    }

    /// <summary>トップレベルのチャンクの種類（"VP8 "・"VP8X"・"EXIF"・"XMP "・"ICCP" など）。</summary>
    public static List<string> ChunkIds(ReadOnlySpan<byte> data)
    {
        var ids = new List<string>();
        if (!IsWebp(data)) return ids;
        var pos = 12;
        while (pos + 8 <= data.Length)
        {
            ids.Add(Encoding.ASCII.GetString(data.Slice(pos, 4)));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos + 4, 4));
            pos += 8 + size + (size & 1);
        }
        return ids;
    }

    private static bool IsWebp(ReadOnlySpan<byte> data) =>
        data.Length >= 12 && Encoding.ASCII.GetString(data[..4]) == "RIFF" && Encoding.ASCII.GetString(data.Slice(8, 4)) == "WEBP";

    private static int ReadU24(ReadOnlySpan<byte> bytes) => bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
}

/// <summary>
/// 出力に EXIF・XMP・ICC が残っていないことの検査（計画書 §5.2）。残っていれば例外。
/// WebP は RIFF のチャンク（EXIF・XMP・ICCP）、JPEG は APP1〜APP15・COM のマーカーを見る（APP0 の JFIF は許す）。
/// </summary>
public static class MetadataGuard
{
    private static readonly string[] ForbiddenWebpChunks = ["EXIF", "XMP ", "ICCP"];

    public static void EnsureStripped(ReadOnlySpan<byte> data, string contentType)
    {
        var found = contentType switch
        {
            "image/webp" => WebpInspector.ChunkIds(data).Where(id => ForbiddenWebpChunks.Contains(id)).ToList(),
            "image/jpeg" => JpegAppMarkers(data),
            _ => throw new ArgumentException($"未対応の形式: {contentType}", nameof(contentType)),
        };
        if (found.Count > 0) throw new InvalidOperationException($"メタデータが残っています（{string.Join(", ", found)}）。出力を作りません");
    }

    /// <summary>SOS（画素）の手前までの APPn（APP0 以外）と COM マーカー。</summary>
    private static List<string> JpegAppMarkers(ReadOnlySpan<byte> data)
    {
        var found = new List<string>();
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) return found;
        var pos = 2;
        while (pos + 4 <= data.Length && data[pos] == 0xFF)
        {
            var marker = data[pos + 1];
            if (marker == 0xDA) break;
            var length = (data[pos + 2] << 8) | data[pos + 3];
            if ((marker >= 0xE1 && marker <= 0xEF) || marker == 0xFE) found.Add($"APP{marker - 0xE0}/COM");
            pos += 2 + length;
        }
        return found;
    }
}
