using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>id の自動採番と、Unity のフォルダからのアバター・服の一括登録。</summary>
public sealed class AvatarImportTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private string UnityFolder()
    {
        var root = Path.Combine(_ws.Layout.RepoRoot, "..", "unity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "A"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "B"));
        File.WriteAllText(Path.Combine(root, "Assets", "A", "FaraDress.prefab"), "");
        File.WriteAllText(Path.Combine(root, "Assets", "A", "FaraDress.prefab.meta"), "");
        File.WriteAllText(Path.Combine(root, "Assets", "B", "FaraCasual.prefab"), "");
        File.WriteAllText(Path.Combine(root, "Assets", "B", "FaraDress.prefab"), "");   // 同じ名前は 1 つにまとめる
        return root;
    }

    [Fact]
    public void NextIdIsTheLargestNumberPlusOneAndIgnoresNonNumericIds()
    {
        Assert.Equal("1", FolderScan.NextId([]));
        Assert.Equal("11", FolderScan.NextId(["2", "10", "dress"]));
    }

    [Fact]
    public void PrefabNamesSkipMetaFilesAndDuplicates()
    {
        Assert.Equal(["FaraCasual", "FaraDress"], FolderScan.PrefabNames(UnityFolder()));
    }

    [Fact]
    public async Task CheckedPrefabsAreRegisteredWithAutomaticIdsAndRegisteredNamesCannotBeChecked()
    {
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        var folder = UnityFolder();
        vm.AvatarImport.Folder = folder;
        vm.AvatarImport.Scan();
        Assert.Equal(2, vm.AvatarImport.Candidates.Count);
        vm.AvatarImport.Candidates.Single(c => c.Name == "FaraDress").IsChecked = true;
        await vm.AvatarImport.ImportCommand.RunAsync();

        var added = Assert.Single(vm.Avatars, a => a.Name == "FaraDress");
        Assert.True(int.Parse(added.Id) > 0);
        // 読み直すと登録済みになる（アバターのインポートでは、登録済みも「服を着せる」ためにチェックできる）
        var again = vm.AvatarImport.Candidates.Single(c => c.Name == "FaraDress");
        Assert.True(again.Registered);
        Assert.False(vm.AvatarImport.Candidates.Single(c => c.Name == "FaraCasual").Registered);
        Assert.Equal(folder, _ws.Session.Preferences.AvatarFolder);                      // フォルダは覚える
    }

    [Fact]
    public async Task NewOutfitsAndAvatarsGetAutomaticIdsWithoutTyping()
    {
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        vm.NewOutfitCommand.Execute(null);
        vm.OutfitName = "白いドレス";
        await vm.SaveOutfitCommand.RunAsync();
        vm.NewOutfitCommand.Execute(null);
        vm.OutfitName = "リボン";
        await vm.SaveOutfitCommand.RunAsync();
        Assert.Equal(["1", "2"], vm.Outfits.Select(o => o.Id).Order());
        vm.NewAvatarCommand.Execute(null);
        vm.AvatarName = "Fairy";
        await vm.SaveAvatarCommand.RunAsync();
        Assert.Equal("1", Assert.Single(vm.Avatars).Id);
    }
}
