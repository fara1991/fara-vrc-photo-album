using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>同期の画面: 比べる・足りない分を選ぶ・実行する（R2 は、メモリ上の控え用バケットに差し替える）。</summary>
public sealed class SyncTests : IDisposable
{
    private readonly Workspace _ws = new();
    private readonly MemoryObjectStore _store = new();
    private readonly List<string> _opened = [];

    public void Dispose() => _ws.Dispose();

    private SyncViewModel Create(bool offline = false, bool withBackupBucket = true)
    {
        var root = _ws.Layout.RepoRoot;
        var content = Path.Combine(root, "content");
        Directory.CreateDirectory(content);
        if (!Directory.EnumerateFiles(content, "*.json", SearchOption.AllDirectories).Any())
            File.WriteAllText(Path.Combine(content, "tags.json"), "{ }");
        var local = Path.Combine(root, "local");
        Directory.CreateDirectory(local);
        File.WriteAllText(Path.Combine(local, "album.local.json"),
            withBackupBucket ? """{ "bucket": "fara-photos-test", "backupBucket": "fara-backup-test" }""" : """{ "bucket": "fara-photos-test" }""");
        var session = new AlbumSession(RepoContext.Discover(root), _ws.Layout.UploadRoot, offline);
        return new SyncViewModel(new AppState(session), _ws.Dialogs, bucket =>
        {
            _opened.Add(bucket);
            return new StoreHandle(_store, owner: null);
        });
    }

    [Fact]
    public async Task ComparingListsEveryFileWithItsStatusAndOpensOnlyTheBackupBucket()
    {
        var vm = Create();

        await vm.CompareCommand.RunAsync();

        Assert.NotEmpty(vm.Rows);
        Assert.All(vm.Rows, r => Assert.Equal(SyncStatus.LocalOnly, r.Item.Status));
        Assert.Contains("ローカルにだけ", vm.Summary);
        Assert.Contains("fara-backup-test", vm.Summary);
        Assert.Equal(["fara-backup-test"], _opened.Distinct());                                         // 写真のバケットは開かない
        Assert.Contains("違いがあります", vm.Status);
        Assert.All(vm.Rows, r => Assert.Equal(SyncAction.None, r.Action));                              // 比べただけでは、何も選ばれていない
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectingTheMissingSideThenApplyingSendsTheFilesAndComparesAgain()
    {
        var vm = Create();
        await vm.CompareCommand.RunAsync();
        Assert.True(vm.SyncMissingCommand.CanExecute(null));

        vm.SyncMissingCommand.Execute(null);
        Assert.All(vm.Rows, r => Assert.Equal(SyncAction.Upload, r.Action));
        Assert.True(vm.ApplyCommand.CanExecute(null));

        _ws.Dialogs.Answer = true;
        await vm.ApplyCommand.RunAsync();

        Assert.All(_store.Objects.Keys, k => Assert.StartsWith("sync/", k));
        Assert.All(vm.Rows, r => Assert.Equal(SyncStatus.Same, r.Item.Status));                          // 実行のあと、比べ直している
        Assert.Contains("同期しました", vm.Status);
        Assert.Contains("ローカルと R2 は同じです", vm.Status);
        Assert.Contains(_ws.Dialogs.Informed, i => i.StartsWith("同期が終わりました", StringComparison.Ordinal) && i.Contains("R2 へ送った"));   // 終わったら、ダイアログで知らせる
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("選んだ操作で同期"));
    }

    [Fact]
    public async Task DecliningTheConfirmationChangesNothing()
    {
        var vm = Create();
        await vm.CompareCommand.RunAsync();
        vm.SyncMissingCommand.Execute(null);

        _ws.Dialogs.Answer = false;
        await vm.ApplyCommand.RunAsync();

        Assert.Empty(_store.Objects);
        Assert.Empty(_ws.Dialogs.Informed);                                                             // 断ったら、完了の知らせも出さない
    }

    [Fact]
    public async Task AFileOnlyInR2OffersImportingAndADifferingOneIsNeverChosenForYou()
    {
        var vm = Create();
        await vm.CompareCommand.RunAsync();
        var first = vm.Rows[0].Path;
        await ContentSyncService.ApplyAsync(_ws.Layout.RepoRoot + "/content", _store, [(first, SyncAction.Upload)], Path.Combine(_ws.Layout.RepoRoot, "safety"));
        File.WriteAllText(Path.Combine(_ws.Layout.RepoRoot, "content", first.Replace('/', Path.DirectorySeparatorChar)), "{ \"changed\": true }");
        await vm.CompareCommand.RunAsync();

        var different = vm.Rows.Single(r => r.Path == first);
        Assert.Equal(SyncStatus.Different, different.Item.Status);
        Assert.Equal(3, different.Choices.Count);                                                       // 何もしない・送る・取り込む（上書きになるので、自分で選ぶ）

        vm.SyncMissingCommand.Execute(null);

        Assert.Equal(SyncAction.None, different.Action);                                                // 「足りないものを写す」は、内容が違うものを変えない
    }

    [Fact]
    public async Task MirroringToR2SendsLocalOnlyFilesAndDeletesR2OnlyOnesAfterAStrongConfirmation()
    {
        var vm = Create();
        await vm.CompareCommand.RunAsync();
        vm.SyncMissingCommand.Execute(null);
        _ws.Dialogs.Answer = true;
        await vm.ApplyCommand.RunAsync();                                                               // いったん全部を R2 へ
        await _store.PutAsync("sync/old-only-in-r2.json", [1], "application/json", "no-store");          // R2 にだけあるファイル
        await vm.CompareCommand.RunAsync();
        var remoteOnly = vm.Rows.Single(r => r.Item.Status == SyncStatus.RemoteOnly);
        Assert.Equal(3, remoteOnly.Choices.Count);                                                       // 何もしない・取り込む・R2 から消す

        vm.MirrorToRemoteCommand.Execute(null);

        Assert.Equal(SyncAction.DeleteRemote, remoteOnly.Action);
        _ws.Dialogs.Asked.Clear();
        await vm.ApplyCommand.RunAsync();

        Assert.False(await _store.ExistsAsync("sync/old-only-in-r2.json"));
        Assert.Contains("R2 から消した 1 件", vm.Status);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("選んだ操作で同期"));
    }

    [Fact]
    public async Task MirroringToLocalDeletesLocalOnlyFilesButNeverWithoutChoosingIt()
    {
        var vm = Create();
        await vm.CompareCommand.RunAsync();
        var local = vm.Rows[0];
        Assert.Equal(SyncStatus.LocalOnly, local.Item.Status);
        Assert.Equal(3, local.Choices.Count);                                                            // 何もしない・R2 へ送る・ローカルから消す

        vm.SyncMissingCommand.Execute(null);
        Assert.Equal(SyncAction.Upload, local.Action);                                                   // 「足りないものを写す」は、消さない

        vm.MirrorToLocalCommand.Execute(null);
        Assert.Equal(SyncAction.DeleteLocal, local.Action);                                              // 「ローカルを R2 に合わせる」でだけ、消す操作が選ばれる
    }

    [Fact]
    public async Task TheDemoDoesNotTouchR2AndAMissingBackupBucketIsExplained()
    {
        var demo = Create(offline: true);
        await demo.CompareCommand.RunAsync();
        Assert.Contains("デモ", demo.Status);
        Assert.Empty(_opened);

        var noBucket = Create(withBackupBucket: false);
        await noBucket.CompareCommand.RunAsync();
        Assert.Contains("控え用のバケット", noBucket.Status);
        Assert.Empty(_opened);
    }
}
