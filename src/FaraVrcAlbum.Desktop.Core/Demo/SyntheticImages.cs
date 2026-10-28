using System.Buffers.Binary;
using System.Text;
using NetVips;

namespace FaraVrcAlbum.Desktop.Demo;

/// <summary>
/// 合成の PNG（デモとテスト用。実写真は使わない）。XMP は VRChat の形（属性）を模して iTXt に入れる。
/// CRC は正しく付けて、WPF などの一般的なデコーダーでも読めるようにする。
/// </summary>
public static class SyntheticImages
{
    public static byte[] Png(int width, int height, int shade, string? xmp)
    {
        using var image = NetVips.Image.Black(width, height).Linear(new double[] { 0, 0, 0 }, new double[] { shade, 80, 200 });
        var png = image.PngsaveBuffer();
        return xmp is null ? png : InsertAfterIhdr(png, Itxt("XML:com.adobe.xmp", xmp));
    }

    /// <summary>VRChat の XMP に近い属性の形（撮影時刻・ワールド）。</summary>
    public static string Xmp(string worldId, string worldName, string created) =>
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmlns:vrc=\"http://ns.vrchat.com/vrc/1.0/\"" +
        $" xmp:CreateDate=\"{created}\"" +
        $" vrc:WorldID=\"{worldId}\" vrc:WorldDisplayName=\"{worldName}\"/></rdf:RDF></x:xmpmeta>";

    private static byte[] Itxt(string keyword, string text)
    {
        var data = new List<byte>();
        data.AddRange(Encoding.Latin1.GetBytes(keyword));
        data.AddRange(new byte[] { 0, 0, 0, 0, 0 }); // 区切り・圧縮フラグ・圧縮方式・言語・翻訳キーワード
        data.AddRange(Encoding.UTF8.GetBytes(text));
        return Chunk("iTXt", data.ToArray());
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        var crc = Crc32(chunk.AsSpan(4, 4 + data.Length));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length, 4), crc);
        return chunk;
    }

    private static byte[] InsertAfterIhdr(byte[] png, byte[] chunk)
    {
        const int ihdrEnd = 8 + 25;
        var result = new List<byte>(png[..ihdrEnd]);
        result.AddRange(chunk);
        result.AddRange(png[ihdrEnd..]);
        return result.ToArray();
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0; n < 256; n++)
        {
            var c = (uint)n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
