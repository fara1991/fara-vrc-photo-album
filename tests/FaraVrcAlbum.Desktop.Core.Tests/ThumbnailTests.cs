using FaraVrcAlbum.Desktop.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>アバター・服の一覧のサムネイル（Unity のプロジェクトの画像・BOOTH の URL）。</summary>
public sealed class ThumbnailTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fara-thumb-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1]);
        return path;
    }

    [Fact]
    public void AvatarThumbnailsAreFoundAutomaticallyFromTheAssetsFolderAboveThePrefabs()
    {
        var png = Touch("Unity", "Assets", "Fara", "Thumbnails", "FaraAlmarianDress.png");
        Touch("Unity", "Assets", "Fara", "Texture", "FaraOther.png");                                   // サムネイルのフォルダではない
        Touch("Unity", "Assets", "Fara", "Prefab", "VRM", "FaraAlmarianDress.prefab");
        var prefabs = Path.Combine(_root, "Unity", "Assets", "Fara", "Prefab", "VRM");

        var thumbnails = AvatarThumbnails.Build(null, prefabs);

        Assert.Equal(png, thumbnails.Find("FaraAlmarianDress"));
        Assert.Equal(png, thumbnails.Find("Fara AlmarianDress"));                                        // VRChat の表記（空白あり）でも合う
        Assert.Equal(png, thumbnails.Find("x", ["faraalmariandress"]));                                  // 別名・大文字小文字
        Assert.Null(thumbnails.Find("FaraOther"));
        Assert.Null(thumbnails.Find("Missing"));
    }

    [Fact]
    public void AConfiguredFolderIsUsedInsteadOfTheAutomaticSearch()
    {
        Touch("Unity", "Assets", "Fara", "Thumbnails", "FaraA.png");
        var mine = Touch("Mine", "sub", "FaraB.JPG");
        var prefabs = Path.Combine(_root, "Unity", "Assets", "Fara", "Prefab");
        Directory.CreateDirectory(prefabs);

        var thumbnails = AvatarThumbnails.Build(Path.Combine(_root, "Mine"), prefabs);

        Assert.Equal(mine, thumbnails.Find("FaraB"));
        Assert.Null(thumbnails.Find("FaraA"));                                                           // 設定したフォルダだけを見る
    }

    [Fact]
    public void NothingIsFoundWithoutAnAssetsFolderOrWithAMissingFolder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "NoAssets"));
        Assert.Equal(0, AvatarThumbnails.Build(null, Path.Combine(_root, "NoAssets")).Count);
        Assert.Equal(0, AvatarThumbnails.Build(Path.Combine(_root, "gone"), null).Count);
        Assert.Equal(0, AvatarThumbnails.Build(null, null).Count);
    }

    [Fact]
    public void TheBoothItemIdComesFromTheOutfitUrl()
    {
        Assert.Equal(6331919L, BoothLibrary.ItemIdOfUrl("https://booth.pm/ja/items/6331919"));
        Assert.Null(BoothLibrary.ItemIdOfUrl("https://example.test/"));
        Assert.Null(BoothLibrary.ItemIdOfUrl(null));
    }
}
