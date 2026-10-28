using System.Buffers.Binary;
using System.Text;
using NetVips;

namespace FaraVrcAlbum.Images.Tests;

/// <summary>テスト用の一時フォルダ（bin 配下。%TEMP% は使わない）。</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

/// <summary>合成の画像（実写真は使わない）。VRChat の XMP の形を模して、iTXt に入れる。</summary>
internal static class TestImages
{
    public const string WorldId = "wrld_11111111-2222-4333-8444-555555555555";
    public const string AuthorId = "usr_11111111-2222-4333-8444-555555555555";

    public static string XmpAttributes(string? worldId = WorldId, string worldName = "テストのワールド", string created = "2026-10-23T21:33:12.345+09:00") =>
        "<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>" +
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmlns:vrc=\"http://ns.vrchat.com/vrc/1.0/\"" +
        $" xmp:CreateDate=\"{created}\" xmp:Author=\"テスト撮影者\" vrc:AuthorID=\"{AuthorId}\"" +
        (worldId is null ? "" : $" vrc:WorldID=\"{worldId}\"") +
        $" vrc:WorldDisplayName=\"{worldName}\"/></rdf:RDF></x:xmpmeta>";

    /// <summary>子要素の形（属性ではなく要素で入っている場合）。</summary>
    public static string XmpElements(string worldId = WorldId, string created = "2026-10-23T21:33:12.345+09:00") =>
        "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">" +
        "<rdf:Description xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmlns:vrc=\"http://ns.vrchat.com/vrc/1.0/\">" +
        $"<xmp:CreateDate>{created}</xmp:CreateDate>" +
        "<xmp:Author><rdf:Seq><rdf:li>テスト撮影者</rdf:li></rdf:Seq></xmp:Author>" +
        $"<vrc:WorldID>{worldId}</vrc:WorldID>" +
        "<vrc:WorldDisplayName>要素のワールド</vrc:WorldDisplayName>" +
        "</rdf:Description></rdf:RDF></x:xmpmeta>";

    /// <summary>グラデーションの PNG（幅・高さ指定）。xmp を渡すと iTXt の XMP を IHDR の直後に入れる（CRC は合成のため 0）。</summary>
    public static byte[] Png(int width, int height, string? xmp = null, int shade = 120)
    {
        using var image = NetVips.Image.Black(width, height).Linear(new double[] { 0, 0, 0 }, new double[] { shade, 80, 200 });
        var png = image.PngsaveBuffer();
        return xmp is null ? png : InsertAfterIhdr(png, Itxt("XML:com.adobe.xmp", xmp));
    }

    /// <summary>VRChat の写真のファイル名（撮影時刻・寸法）。</summary>
    public static string VrchatName(string time = "2026-10-23_21-33-12.345", int width = 3840, int height = 2160) =>
        $"VRChat_{time}_{width}x{height}.png";

    /// <summary>JPEG（APP1 の Exif を入れる。検査の試験用）。</summary>
    public static byte[] JpegWithExif()
    {
        using var image = NetVips.Image.Black(64, 64).Linear(new double[] { 0, 0, 0 }, new double[] { 90, 90, 90 });
        var jpeg = image.JpegsaveBuffer(q: 80);
        var app1 = new List<byte> { 0xFF, 0xE1, 0x00, 0x0A };
        app1.AddRange("Exif\0\0"u8.ToArray());
        app1.AddRange(new byte[] { 0x01, 0x02 });
        var result = new List<byte>(jpeg[..2]);
        result.AddRange(app1);
        result.AddRange(jpeg[2..]);
        return result.ToArray();
    }

    private static byte[] Itxt(string keyword, string text)
    {
        var data = new List<byte>();
        data.AddRange(Encoding.Latin1.GetBytes(keyword));
        data.Add(0);
        data.Add(0);
        data.Add(0);
        data.Add(0);
        data.Add(0);
        data.AddRange(Encoding.UTF8.GetBytes(text));
        return Chunk("iTXt", data.ToArray());
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(0, 4), data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
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
}
