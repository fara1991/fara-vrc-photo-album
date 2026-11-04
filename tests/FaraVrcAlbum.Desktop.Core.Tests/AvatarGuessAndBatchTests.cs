using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>アバター名からの服の予測と、服・アバターのまとめて削除。</summary>
public sealed class AvatarGuessAndBatchTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private async Task<AvatarOutfitViewModel> WithOutfitsAsync(params string[] names)
    {
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        foreach (var name in names)
        {
            vm.NewOutfitCommand.Execute(null);
            vm.OutfitName = name;
            await vm.SaveOutfitCommand.RunAsync();
        }
        return vm;
    }

    [Fact]
    public void TheOutfitIsGuessedFromTheAvatarNameByTheirSharedWords()
    {
        var outfits = new List<(string Id, string Name)> { ("1", "ALMARIAN RE:"), ("2", "VRoid SFウィッチドレス v1.2"), ("3", "Moonlight Maid") };

        Assert.Equal([("1", "ALMARIAN RE:")], OutfitGuess.Suggest("FaraAlmarianDress", outfits));
        Assert.Equal([("3", "Moonlight Maid")], OutfitGuess.Suggest("Fara_MoonlightMaid_v2", outfits));
        Assert.Empty(OutfitGuess.Suggest("FaraBlackHeart", outfits));                      // 手がかりが無ければ予測しない
        Assert.Equal(["fara", "almarian", "dress"], OutfitGuess.Tokens("FaraAlmarianDress"));
    }

    [Fact]
    public async Task ImportingAvatarsShowsTheGuessAndLinksItWhenAsked()
    {
        var vm = await WithOutfitsAsync("ALMARIAN RE:", "Other Outfit");
        var folder = Path.Combine(_ws.Layout.RepoRoot, "..", "unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "FaraAlmarianDress.prefab"), "");
        File.WriteAllText(Path.Combine(folder, "FaraBlackHeart.prefab"), "");
        vm.AvatarImport.Folder = folder;
        vm.AvatarImport.Scan();
        var almarian = vm.AvatarImport.Candidates.Single(c => c.Name == "FaraAlmarianDress");
        Assert.Equal("ALMARIAN RE:", almarian.Suggestion);
        Assert.Equal("", vm.AvatarImport.Candidates.Single(c => c.Name == "FaraBlackHeart").Suggestion);

        foreach (var c in vm.AvatarImport.Candidates) c.IsChecked = true;
        await vm.AvatarImport.ImportCommand.RunAsync();

        var linked = vm.Avatars.Single(a => a.Name == "FaraAlmarianDress");
        Assert.Equal("ALMARIAN RE:", linked.OutfitsText);                                  // 予測した服が着せてある
        Assert.Equal("（服なし）", vm.Avatars.Single(a => a.Name == "FaraBlackHeart").OutfitsText);
    }

    [Fact]
    public async Task CheckedOutfitsAreDeletedTogetherButNothingIsDeletedIfOneIsInUse()
    {
        var vm = await WithOutfitsAsync("服A", "服B", "服C");
        vm.NewAvatarCommand.Execute(null);
        vm.AvatarName = "TestAvatar";
        vm.SelectedOutfitPick = vm.OutfitPicks.Single(p => p.Name == "服C");
        await vm.SaveAvatarCommand.RunAsync();

        // 使われている服（服C）が混じっていると、何も消さない
        vm.AllOutfitsChecked = true;
        await vm.RemoveOutfitCommand.RunAsync();
        Assert.Equal(3, vm.Outfits.Count);
        Assert.Contains("使われている服", vm.Status);

        // 使われていない服だけを選べば、まとめて消える
        vm.AllOutfitsChecked = false;
        vm.Outfits.Single(o => o.Name == "服A").IsChecked = true;
        vm.Outfits.Single(o => o.Name == "服B").IsChecked = true;
        Assert.True(vm.RemoveOutfitCommand.CanExecute(null));
        await vm.RemoveOutfitCommand.RunAsync();
        Assert.Equal(["服C"], vm.Outfits.Select(o => o.Name));
        Assert.Equal(1, _ws.Dialogs.Asked.Count(t => t is "服を削除します"));                // 使われている服が混じった 1 回目は、確認まで進まずに止まる
    }

    [Fact]
    public async Task CheckedAvatarsAreDeletedTogether()
    {
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        foreach (var name in new[] { "AvatarOne", "AvatarTwo", "AvatarThree" })
        {
            vm.NewAvatarCommand.Execute(null);
            vm.AvatarName = name;
            await vm.SaveAvatarCommand.RunAsync();
        }
        Assert.False(vm.RemoveAvatarCommand.CanExecute(null) && vm.Avatars.All(a => !a.IsChecked) && vm.SelectedAvatar is null);

        vm.AllAvatarsChecked = true;
        vm.Avatars.Single(a => a.Name == "AvatarTwo").IsChecked = false;
        await vm.RemoveAvatarCommand.RunAsync();

        Assert.Equal(["AvatarTwo"], vm.Avatars.Select(a => a.Name));
    }

    [Fact]
    public async Task WhileTwoOrMoreRowsAreCheckedSavingIsDisabledAndTheNoteIsShown()
    {
        var vm = await WithOutfitsAsync("服A", "服B");
        vm.SelectedOutfit = vm.Outfits.First();
        Assert.True(vm.SaveOutfitCommand.CanExecute(null));
        vm.Outfits[0].IsChecked = true;
        Assert.True(vm.SaveOutfitCommand.CanExecute(null));                             // 1 件だけなら保存できる
        vm.Outfits[1].IsChecked = true;

        Assert.False(vm.SaveOutfitCommand.CanExecute(null));                            // 2 件以上チェックしていると保存できない
        Assert.Contains("保存はできません", vm.OutfitMultiNote);
        Assert.True(vm.AllOutfitsChecked);                                              // 見出しのチェックは全選択の状態
        vm.AllOutfitsChecked = false;
        Assert.All(vm.Outfits, o => Assert.False(o.IsChecked));
        Assert.True(vm.SaveOutfitCommand.CanExecute(null));
        Assert.Equal("", vm.OutfitMultiNote);
    }

    [Fact]
    public async Task AnAvatarWithSeveralLinkedOutfitsOpensWithANoteAndSavesOnlyTheChosenOne()
    {
        var vm = await WithOutfitsAsync("服A", "服B", "服C");
        vm.NewAvatarCommand.Execute(null);
        vm.AvatarName = "Multi";
        await vm.SaveAvatarCommand.RunAsync();
        var id = vm.Avatars.Single().Id;
        FaraVrcAlbum.Tools.Services.AvatarOutfitService.AvatarLink(_ws.Session.Editor, true, id, ["1", "2"]);   // CLI で 2 つ紐づけた状態
        vm.Refresh();

        vm.SelectedAvatar = vm.Avatars.Single();

        Assert.Equal("1", vm.SelectedOutfitPick.Id);
        Assert.Contains("2 件紐づいています", vm.ExtraOutfitsNote);
        vm.SelectedOutfitPick = vm.OutfitPicks.Single(p => p.Id == "3");
        await vm.SaveAvatarCommand.RunAsync();
        Assert.Equal(["3"], vm.Avatars.Single().OutfitIds);
    }

    [Fact]
    public async Task RegisteredAvatarsCanGetTheGuessedOutfitAndExistingOnesOnlyWhenOverwriteIsChosen()
    {
        var vm = await WithOutfitsAsync("ALMARIAN RE:", "Other Outfit");
        vm.NewAvatarCommand.Execute(null);
        vm.AvatarName = "FaraAlmarianDress";
        await vm.SaveAvatarCommand.RunAsync();                                          // 服なしで登録済み
        var folder = Path.Combine(_ws.Layout.RepoRoot, "..", "unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "FaraAlmarianDress.prefab"), "");
        vm.AvatarImport.Folder = folder;
        vm.AvatarImport.Scan();
        var candidate = vm.AvatarImport.Candidates.Single();
        Assert.True(candidate.Registered);
        Assert.True(candidate.CanCheck);                                                // 登録済みも、服を着せるためにチェックできる
        Assert.Equal("ALMARIAN RE:", candidate.Suggestion);

        vm.AvatarImport.AllChecked = true;                                              // 見出しのチェックで全選択
        Assert.True(candidate.IsChecked);
        Assert.False(vm.AvatarImport.ImportCommand.CanExecute(null));                   // 登録済みはインポートの対象ではない
        await vm.AvatarImport.ApplyRegisteredCommand.RunAsync();
        Assert.Equal("ALMARIAN RE:", vm.Avatars.Single().OutfitsText);

        // すでに服があるものは、置き換えを選ばないと変えない
        vm.SelectedAvatar = vm.Avatars.Single();
        vm.SelectedOutfitPick = vm.OutfitPicks.Single(p => p.Name == "Other Outfit");
        await vm.SaveAvatarCommand.RunAsync();
        vm.AvatarImport.Scan();
        vm.AvatarImport.AllChecked = true;
        await vm.AvatarImport.ApplyRegisteredCommand.RunAsync();
        Assert.Equal("Other Outfit", vm.Avatars.Single().OutfitsText);
        Assert.Contains("着せられるアバターがありません", vm.Status);
        vm.AvatarImport.LinkOverwrite = true;
        vm.AvatarImport.AllChecked = true;
        await vm.AvatarImport.ApplyRegisteredCommand.RunAsync();
        Assert.Equal("ALMARIAN RE:", vm.Avatars.Single().OutfitsText);
    }
}
