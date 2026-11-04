using System.Text.Json.Nodes;
using FaraVrcAlbum.Desktop.ViewModels;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>アバター・服の画面と、下書きの写真ごとのアバター選択。</summary>
public sealed class AvatarOutfitTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private string ContentDir => Path.Combine(_ws.Layout.RepoRoot, "content");

    private string Read(string name) => File.Exists(Path.Combine(ContentDir, name)) ? File.ReadAllText(Path.Combine(ContentDir, name)) : "";

    /// <summary>服 2 つと、それを着せたアバターを画面から登録する。</summary>
    private async Task<AvatarOutfitViewModel> SeedMastersAsync()
    {
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        foreach (var (id, name) in new[] { ("dress", "白いドレス"), ("ribbon", "リボン") })
        {
            vm.NewOutfitCommand.Execute(null);
            vm.OutfitId = id;
            vm.OutfitName = name;
            vm.OutfitUrl = "https://booth.pm/ja/items/1";
            await vm.SaveOutfitCommand.RunAsync();
        }
        vm.NewAvatarCommand.Execute(null);
        vm.AvatarId = "fairy";
        vm.AvatarName = "Fairy_Dress_v2";
        vm.AvatarAliases = "Fairy、Fairy v2";
        // 着る服は 1 つ（プルダウンで選ぶ）
        vm.SelectedOutfitPick = vm.OutfitPicks.Single(p => p.Id == "dress");
        await vm.SaveAvatarCommand.RunAsync();
        return vm;
    }

    [Fact]
    public async Task OutfitsAndAvatarsAreAddedAndLinkedWithTheChosenOutfitAfterConfirmation()
    {
        var vm = await SeedMastersAsync();

        Assert.Equal(["dress", "ribbon"], vm.Outfits.Select(o => o.Id));
        var avatar = Assert.Single(vm.Avatars);
        Assert.Equal(["dress"], avatar.OutfitIds);
        Assert.Equal(["Fairy", "Fairy v2"], avatar.Aliases);
        Assert.Equal("白いドレス", avatar.OutfitsText);
        Assert.Equal(1, vm.Outfits.Single(o => o.Id == "dress").Users);                 // 着せているアバターの数
        Assert.Contains("\"name\": \"白いドレス\"", Read("outfits.json"));
        Assert.Contains("\"name\": \"Fairy_Dress_v2\"", Read("avatars.json"));
    }

    [Fact]
    public async Task NothingIsWrittenWhenTheConfirmationIsDeclined()
    {
        _ws.Dialogs.Answer = false;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        vm.OutfitId = "dress";
        vm.OutfitName = "白いドレス";
        await vm.SaveOutfitCommand.RunAsync();

        Assert.Single(_ws.Dialogs.Asked);
        Assert.DoesNotContain("dress", Read("outfits.json"));
        Assert.Empty(vm.Outfits);
    }

    [Fact]
    public async Task AnInvalidEntryIsRejectedBeforeAskingAndNothingIsWritten()
    {
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        vm.OutfitId = "dress";
        vm.OutfitName = "白いドレス";
        vm.OutfitUrl = "http://example.test/";                                          // https 以外は拒否
        await vm.SaveOutfitCommand.RunAsync();

        Assert.Empty(_ws.Dialogs.Asked);                                                // 検査で落ちたら確認を出さない
        Assert.False(string.IsNullOrEmpty(vm.Status) || vm.Status.StartsWith("エラー（"), vm.Status);   // 型名は付けず、文だけ出す
        Assert.DoesNotContain("dress", Read("outfits.json"));
    }

    [Fact]
    public async Task AnOutfitInUseCannotBeRemoved()
    {
        var vm = await SeedMastersAsync();
        _ws.Dialogs.Asked.Clear();
        vm.SelectedOutfit = vm.Outfits.Single(o => o.Id == "dress");
        await vm.RemoveOutfitCommand.RunAsync();

        Assert.Empty(_ws.Dialogs.Asked);
        Assert.Contains("使われている", vm.Status);
        Assert.Contains("dress", Read("outfits.json"));
    }

    [Fact]
    public async Task EditingAnAvatarKeepsItsIdAndReplacesTheLinkedOutfit()
    {
        var vm = await SeedMastersAsync();
        vm.SelectedAvatar = vm.Avatars.Single();
        Assert.False(vm.IsNewAvatar);
        Assert.Equal("dress", vm.SelectedOutfitPick.Id);
        vm.SelectedOutfitPick = vm.OutfitPicks.Single(p => p.Id == "ribbon");
        await vm.SaveAvatarCommand.RunAsync();

        Assert.Equal(["ribbon"], vm.Avatars.Single().OutfitIds);
    }
}
