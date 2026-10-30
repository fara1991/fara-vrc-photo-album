using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;
using Xunit;

namespace FaraVrcAlbum.Content.Tests;

/// <summary>アバターと服の検査（ContentLoader）と、写真の服の解決（OutfitRules）。一時フォルダだけを使う（R2 には触らない）。</summary>
public sealed class OutfitRulesTests : IDisposable
{
    private const string Key = "2026-10/20261023-213312-345_a1b2c3d4";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fara-outfits-" + Guid.NewGuid().ToString("N")[..8]);

    public OutfitRulesTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "posts"));
        File.WriteAllText(Path.Combine(_dir, "worlds.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "tags.json"), "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_dir, name), text);

    private void Post(string photos) =>
        Write("posts/a.json", "{ \"schema\": 1, \"slug\": \"a\", \"title\": \"題\", \"publishedAt\": \"2026-10-24T20:00:00+09:00\", \"tags\": [], \"body\": \"本文\", \"photos\": [" + photos + "] }");

    private static string Photo(string extra = "") => "{ \"key\": \"" + Key + "\", \"w\": 2048, \"h\": 1152, \"takenAt\": \"2026-10-23T21:33:12+09:00\", \"color\": \"1b2a3c\"" + extra + " }";

    private const string GoodOutfits = "{ \"coat-red\": { \"name\": \"赤いコート\", \"shop\": \"テスト店\", \"url\": \"https://booth.pm/ja/items/1\" }, \"boots\": { \"name\": \"靴\" } }";
    private const string GoodAvatars = "{ \"avatar-a\": { \"name\": \"Avatar A\", \"aliases\": [\"アバターA\"], \"outfitIds\": [\"coat-red\", \"boots\"] } }";

    private List<string> Problems()
    {
        try
        {
            ContentLoader.Load(_dir);
            return [];
        }
        catch (ContentException e)
        {
            return e.Problems.ToList();
        }
    }

    [Fact]
    public void AWritePhotoWithoutOutfitsUsesTheAvatarsDefaultOrderAndAnOverrideReplacesIt()
    {
        Write("outfits.json", GoodOutfits);
        Write("avatars.json", GoodAvatars);
        Post(Photo(", \"avatar\": \"avatar-a\"") + ", " + Photo().Replace(Key, "2026-10/20261023-213312-345_b1b1b1b1").Replace("\"color\"", "\"avatar\": \"avatar-a\", \"outfits\": [\"boots\"], \"color\""));

        var content = ContentLoader.Load(_dir);
        var photos = content.Posts.Single().Photos;

        var defaults = OutfitRules.Resolve(photos[0], content.Avatars, content.Outfits);
        Assert.Equal(["赤いコート", "靴"], defaults!.Select(o => o.Name));
        Assert.Equal("https://booth.pm/ja/items/1", defaults![0].Url);
        Assert.Equal(["靴"], OutfitRules.Resolve(photos[1], content.Avatars, content.Outfits)!.Select(o => o.Name));
    }

    [Fact]
    public void AnEmptyOutfitsListMeansNoOutfitsEvenWithAnAvatar()
    {
        Write("outfits.json", GoodOutfits);
        Write("avatars.json", GoodAvatars);
        Post(Photo(", \"avatar\": \"avatar-a\", \"outfits\": []"));

        var content = ContentLoader.Load(_dir);

        Assert.Null(OutfitRules.Resolve(content.Posts.Single().Photos[0], content.Avatars, content.Outfits));
    }

    [Fact]
    public void UnknownIdsHttpUrlsAndDuplicatesAreRefused()
    {
        Write("outfits.json", "{ \"coat-red\": { \"name\": \"赤いコート\", \"url\": \"http://booth.pm/x\" }, \"Bad_ID\": { \"name\": \"靴\" } }");
        Write("avatars.json", "{ \"avatar-a\": { \"name\": \"Avatar A\", \"outfitIds\": [\"coat-red\", \"missing\", \"coat-red\"] } }");
        Post(Photo(", \"avatar\": \"avatar-z\", \"outfits\": [\"coat-red\", \"coat-red\"]"));

        var problems = Problems();

        Assert.Contains(problems, p => p.Contains("url は https://"));
        Assert.Contains(problems, p => p.Contains("outfits.json: 服の id の形式が不正です") && p.Contains("Bad_ID"));
        Assert.Contains(problems, p => p.Contains("avatars.json: avatar-a の outfitIds が outfits.json にありません") && p.Contains("missing"));
        Assert.Contains(problems, p => p.Contains("avatars.json: avatar-a の outfitIds に同じ服が 2 回"));
        Assert.Contains(problems, p => p.Contains("avatar が content/avatars.json にありません") && p.Contains("avatar-z"));
        Assert.Contains(problems, p => p.Contains("outfits に同じ服が 2 回"));
    }

    [Fact]
    public void NoOutfitFilesMeansNoOutfitsAndTheRepositoryPostsStillLoad()
    {
        Post(Photo());

        var content = ContentLoader.Load(_dir);

        Assert.Empty(content.Outfits);
        Assert.Null(OutfitRules.Resolve(content.Posts.Single().Photos[0], content.Avatars, content.Outfits));
    }
}
