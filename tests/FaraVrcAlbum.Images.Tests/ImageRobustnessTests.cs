using System.Text;
using FaraVrcAlbum.Images;
using Xunit;

namespace FaraVrcAlbum.Images.Tests;

/// <summary>壊れた画像・極端な PNG チャンクで、例外の種類が呼び出し側の想定どおりになること。</summary>
public sealed class ImageRobustnessTests
{
    [Fact]
    public void AChunkLengthNearIntMaxStopsTheListInsteadOfOverflowing()
    {
        // 署名（8 バイト）のあと、長さ 0x7FFFFFF0 の IHDR。12 + 長さ を int で足すと溢れて、境界の検査を通り抜けていた
        byte[] data =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x7F, 0xFF, 0xFF, 0xF0, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        ];

        Assert.Empty(PngReader.Chunks(data));
    }

    [Fact]
    public void AnUnreadableImageIsAnInvalidDataExceptionSoCallersCanSkipIt()
    {
        var error = Assert.Throws<InvalidDataException>(() => WorldImageEncoder.Encode(Encoding.ASCII.GetBytes("これは画像ではありません")));
        Assert.Contains("画像として読めません", error.Message);
    }
}
