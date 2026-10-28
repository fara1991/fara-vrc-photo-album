using System.Security.Cryptography;

namespace FaraVrcAlbum.Images;

/// <summary>内容ハッシュ（SHA-256）。台帳の鍵はフルのハッシュ、写真のキーには先頭 8 桁を使う。</summary>
public static class Hashing
{
    /// <summary>SHA-256 の小文字 16 進 64 桁。</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
