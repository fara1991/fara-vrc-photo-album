using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>アバターと服のマスタの操作（プレビューは書かない・使われている服は外さない・検査に失敗したら戻す）。一時フォルダだけを使う。</summary>
public sealed class AvatarOutfitServiceTests : IDisposable
{
    private const string Key = "2026-10/20261023-213312-345_a1b2c3d4";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fara-avatar-" + Guid.NewGuid().ToString("N")[..8]);
    private string Content => Path.Combine(_root, "content");

    public AvatarOutfitServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(Content, "posts"));
        File.WriteAllText(Path.Combine(Content, "posts", "a.json"),
            "{ \"schema\": 1, \"slug\": \"a\", \"title\": \"題\", \"publishedAt\": \"2026-10-24T20:00:00+09:00\", \"tags\": [], \"body\": \"本文\", \"photos\": [ { \"key\": \"" + Key + "\", \"w\": 2048, \"h\": 1152, \"takenAt\": \"2026-10-23T21:33:12+09:00\", \"color\": \"1b2a3c\" } ] }");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ContentEditor Editor() => new(Content);

    private string OutfitsFile => Path.Combine(Content, "outfits.json");

    [Fact]
    public async Task TheCliAddsAnAvatarWithoutAliases()
    {
        // --aliases を省いても落ちない（以前は ArgumentNullException）
        File.WriteAllText(Path.Combine(_root, "FaraVrcAlbum.sln"), "");
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), "{ \"ImageBaseUrl\": \"https://img.example.test/\" }");
        var args = FaraVrcAlbum.Tools.Cli.Args.Parse(["avatar", "add", "fairy", "--name", "Fairy", "--apply"]);
        var code = await FaraVrcAlbum.Tools.Cli.Commands.RunAsync(args, new FaraVrcAlbum.Tools.Cli.RepoContext(_root), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Empty(Editor().LoadContent().Avatars["fairy"].Aliases);
    }

    [Fact]
    public void APreviewChecksTheChangeAndWritesNothing()
    {
        AvatarOutfitService.OutfitAdd(Editor(), apply: false, "coat-red", "赤いコート", null, "https://booth.pm/x", null);

        Assert.False(File.Exists(OutfitsFile));
    }

    [Fact]
    public void AnApplyWritesTheMasterAndAPhotoCanGetAnAvatarAndOutfits()
    {
        var editor = Editor();
        AvatarOutfitService.OutfitAdd(editor, apply: true, "coat-red", "赤いコート", null, "https://booth.pm/x", null);
        AvatarOutfitService.AvatarAdd(editor, apply: true, "avatar-a", "Avatar A", ["アバターA"], null);
        AvatarOutfitService.AvatarLink(editor, apply: true, "avatar-a", ["coat-red"]);
        AvatarOutfitService.PhotoAvatar(editor, apply: true, Key, "avatar-a", null);

        var content = Editor().LoadContent();
        Assert.Equal("赤いコート", content.Outfits["coat-red"].Name);
        Assert.Equal(["coat-red"], content.Avatars["avatar-a"].OutfitIds);
        Assert.Equal("avatar-a", content.Posts.Single().Photos.Single().Avatar);
    }

    [Fact]
    public void AUsedOutfitOrAvatarIsNotRemoved()
    {
        var editor = Editor();
        AvatarOutfitService.OutfitAdd(editor, apply: true, "coat-red", "赤いコート", null, null, null);
        AvatarOutfitService.AvatarAdd(editor, apply: true, "avatar-a", "Avatar A", [], null);
        AvatarOutfitService.AvatarLink(editor, apply: true, "avatar-a", ["coat-red"]);
        AvatarOutfitService.PhotoAvatar(editor, apply: true, Key, "avatar-a", null);

        Assert.Throws<InvalidOperationException>(() => AvatarOutfitService.OutfitRemove(editor, apply: true, "coat-red"));
        Assert.Throws<InvalidOperationException>(() => AvatarOutfitService.AvatarRemove(editor, apply: true, "avatar-a"));
        Assert.Contains("coat-red", File.ReadAllText(OutfitsFile));
    }

    [Fact]
    public void ALinkToAnUnknownOutfitIsRolledBack()
    {
        var editor = Editor();
        AvatarOutfitService.AvatarAdd(editor, apply: true, "avatar-a", "Avatar A", [], null);
        var before = File.ReadAllText(Path.Combine(Content, "avatars.json"));

        Assert.ThrowsAny<Exception>(() => AvatarOutfitService.AvatarLink(editor, apply: true, "avatar-a", ["missing"]));

        Assert.Equal(before, File.ReadAllText(Path.Combine(Content, "avatars.json")));
    }
}
