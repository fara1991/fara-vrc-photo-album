using System.Buffers.Binary;
using System.Text;

namespace FaraVrcAlbum.Images;

/// <summary>ファイル 1 つの PNG ヘッダー（寸法と XMP の有無・本文）。画素は読まない。</summary>
public sealed record PngHeaderInfo(int Width, int Height, string? XmpText);

/// <summary>
/// 大量のファイルを走査するための、ストリーム版のヘッダー読み取り。IHDR・iTXt・tEXt・zTXt だけを読み、
/// 他のチャンクは読み飛ばし、IDAT で止まる（数 MB の画素を読まない）。PNG でなければ null。
/// </summary>
public static class PngScanner
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PngHeaderInfo? ScanFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        return Scan(stream);
    }

    public static PngHeaderInfo? Scan(Stream stream)
    {
        var signature = new byte[8];
        if (stream.ReadAtLeast(signature, 8, throwOnEndOfStream: false) < 8 || !signature.AsSpan().SequenceEqual(Signature)) return null;

        int? width = null, height = null;
        string? xmp = null;
        var header = new byte[8];
        while (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(header, 4, 4);
            if (length < 0) break;

            if ((type is "IHDR" or "iTXt" or "tEXt" or "zTXt") && length <= 16 * 1024 * 1024)
            {
                var body = new byte[length];
                if (stream.ReadAtLeast(body, length, throwOnEndOfStream: false) < length) break;
                if (type == "IHDR" && length >= 8)
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(0, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(4, 4));
                }
                else
                {
                    xmp ??= PngReader.FindXmpInChunk(type, body);
                }
                SkipBytes(stream, 4);
            }
            else
            {
                SkipBytes(stream, (long)length + 4);
            }

            if (type is "IDAT" or "IEND") break;
        }

        return width is { } w && height is { } h ? new PngHeaderInfo(w, h, xmp) : null;
    }

    private static void SkipBytes(Stream stream, long count)
    {
        if (stream.CanSeek)
        {
            stream.Seek(count, SeekOrigin.Current);
            return;
        }
        var buffer = new byte[8192];
        while (count > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read <= 0) break;
            count -= read;
        }
    }
}
