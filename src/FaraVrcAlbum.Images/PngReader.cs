using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace FaraVrcAlbum.Images;

/// <summary>PNG のチャンク（種類と本文の位置）。</summary>
public readonly record struct PngChunk(string Type, int DataOffset, int Length);

/// <summary>
/// PNG の寸法と、VRChat が入れる XMP（iTXt の "XML:com.adobe.xmp"）を読む。
/// 画素（IDAT）の手前で止まるので、先頭だけを読めばよい。
/// </summary>
public static class PngReader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    public const string XmpKeyword = "XML:com.adobe.xmp";

    public static bool IsPng(ReadOnlySpan<byte> data) => data.Length >= 8 && data[..8].SequenceEqual(Signature);

    /// <summary>IHDR の寸法。PNG でなければ null。</summary>
    public static (int Width, int Height)? Size(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data)) return null;
        var first = Chunks(data).FirstOrDefault();
        if (first.Type != "IHDR" || first.Length < 8) return null;
        var body = data.Slice(first.DataOffset, 8);
        return (BinaryPrimitives.ReadInt32BigEndian(body[..4]), BinaryPrimitives.ReadInt32BigEndian(body[4..8]));
    }

    /// <summary>XMP の本文（XML 文字列）。無ければ null。IDAT の手前だけを見る。</summary>
    public static string? FindXmp(ReadOnlySpan<byte> data)
    {
        if (!IsPng(data)) return null;
        foreach (var chunk in Chunks(data))
        {
            var text = FindXmpInChunk(chunk.Type, data.Slice(chunk.DataOffset, chunk.Length));
            if (text is not null) return text;
        }
        return null;
    }

    /// <summary>1 つのテキストチャンク（tEXt・zTXt・iTXt）の本文から XMP を取り出す。該当しなければ null。</summary>
    public static string? FindXmpInChunk(string type, ReadOnlySpan<byte> body) => type switch
    {
        "iTXt" => ReadItxt(body),
        "zTXt" => ReadZtxt(body),
        "tEXt" => ReadTextChunk(body),
        _ => null,
    };

    /// <summary>先頭から IDAT（IDAT を含む）か IEND までのチャンクを列挙する。壊れていれば途中で止める。</summary>
    public static List<PngChunk> Chunks(ReadOnlySpan<byte> data)
    {
        var result = new List<PngChunk>();
        var pos = Signature.Length;
        while (pos + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data.Slice(pos, 4));
            if (length < 0 || pos + 12 + length > data.Length) break;
            var type = Encoding.ASCII.GetString(data.Slice(pos + 4, 4));
            result.Add(new PngChunk(type, pos + 8, length));
            if (type is "IDAT" or "IEND") break;
            pos += 12 + length;
        }
        return result;
    }

    private static string? ReadTextChunk(ReadOnlySpan<byte> body)
    {
        var nul = body.IndexOf((byte)0);
        if (nul < 0 || Keyword(body[..nul]) != XmpKeyword) return null;
        return Encoding.Latin1.GetString(body[(nul + 1)..]);
    }

    private static string? ReadZtxt(ReadOnlySpan<byte> body)
    {
        var nul = body.IndexOf((byte)0);
        if (nul < 0 || nul + 2 > body.Length || Keyword(body[..nul]) != XmpKeyword) return null;
        return Encoding.Latin1.GetString(Inflate(body[(nul + 2)..]));
    }

    private static string? ReadItxt(ReadOnlySpan<byte> body)
    {
        var nul = body.IndexOf((byte)0);
        if (nul < 0 || Keyword(body[..nul]) != XmpKeyword) return null;
        var rest = body[(nul + 1)..];
        if (rest.Length < 2) return null;
        var compressed = rest[0] == 1;
        rest = rest[2..];
        var languageEnd = rest.IndexOf((byte)0);
        if (languageEnd < 0) return null;
        rest = rest[(languageEnd + 1)..];
        var translatedEnd = rest.IndexOf((byte)0);
        if (translatedEnd < 0) return null;
        rest = rest[(translatedEnd + 1)..];
        return Encoding.UTF8.GetString(compressed ? Inflate(rest) : rest);
    }

    private static string Keyword(ReadOnlySpan<byte> bytes) => Encoding.Latin1.GetString(bytes);

    private static byte[] Inflate(ReadOnlySpan<byte> zlib)
    {
        using var input = new MemoryStream(zlib.ToArray());
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
