using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>記事の中身（content の JSON）の、ローカルと R2（控え用のバケットの sync/）の同期。</summary>
public sealed class ContentSyncTests : IDisposable
{
    private readonly RepoFixture _fixture = new();
    private readonly TempDir _work = new();

    public void Dispose()
    {
        _fixture.Dispose();
        _work.Dispose();
    }

    private static SyncAction[] None(int count) => Enumerable.Repeat(SyncAction.None, count).ToArray();

    /// <summary>ローカルのすべての JSON を R2 へ送って、同じ状態にする。</summary>
    private async Task<MemoryObjectStore> SyncedStoreAsync()
    {
        var store = new MemoryObjectStore();
        var actions = ContentBackupService.ContentFiles(_fixture.ContentDir).Select(p => (p, SyncAction.Upload)).ToList();
        await ContentSyncService.ApplyAsync(_fixture.ContentDir, store, actions, _work.File("safety-initial"));
        return store;
    }

    [Fact]
    public async Task WithNothingInR2EveryLocalFileIsLocalOnlyAndUploadingMakesThemAllTheSame()
    {
        var store = new MemoryObjectStore();
        var before = await ContentSyncService.CompareAsync(_fixture.ContentDir, store);
        Assert.NotEmpty(before);
        Assert.All(before, i => Assert.Equal((SyncStatus.LocalOnly, null), (i.Status, i.RemoteBytes)));

        var files = ContentBackupService.ContentFiles(_fixture.ContentDir);
        var result = await ContentSyncService.ApplyAsync(_fixture.ContentDir, store, files.Select(p => (p, SyncAction.Upload)).ToList(), _work.File("safety"));

        Assert.Equal((files.Count, 0), (result.Uploaded, result.Downloaded));
        Assert.All(store.Objects.Keys, k => Assert.StartsWith("sync/", k));                          // 控え用のバケットの sync/ だけに置く（写真のバケットの形ではない）
        Assert.All(await ContentSyncService.CompareAsync(_fixture.ContentDir, store), i => Assert.Equal(SyncStatus.Same, i.Status));
    }

    [Fact]
    public async Task ItemsAreClassifiedAsSameLocalOnlyRemoteOnlyAndDifferent()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();

        // ローカルだけに足す・直す・消す（R2 には残る）
        _fixture.WriteExistingPost("newer-post", cover: null);                                                   // ローカルにだけ
        var original = File.ReadAllText(_fixture.PostPath("old-post"));
        RepoFixture.Write(_fixture.PostPath("old-post"), original.Replace("既存の記事", "直した題"));              // 内容が違う
        File.Delete(Path.Combine(_fixture.ContentDir, "tags.json"));                                              // R2 にだけ

        var items = (await ContentSyncService.CompareAsync(_fixture.ContentDir, store)).ToDictionary(i => i.Path);

        Assert.Equal(SyncStatus.LocalOnly, items["posts/newer-post.json"].Status);
        Assert.Equal(SyncStatus.Different, items["posts/old-post.json"].Status);
        Assert.Equal(SyncStatus.RemoteOnly, items["tags.json"].Status);
        Assert.Null(items["tags.json"].LocalBytes);
        Assert.Equal(SyncStatus.Same, items["worlds.json"].Status);
    }

    [Fact]
    public async Task DownloadingRestoresAMissingFileAndAnOverwrittenOneKeepsTheOldContentInTheSafetyCopyAndNothingIsDeleted()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();
        var original = File.ReadAllText(_fixture.PostPath("old-post"));
        var tags = File.ReadAllText(Path.Combine(_fixture.ContentDir, "tags.json"));
        RepoFixture.Write(_fixture.PostPath("old-post"), original.Replace("既存の記事", "直した題"));
        File.Delete(Path.Combine(_fixture.ContentDir, "tags.json"));
        _fixture.WriteExistingPost("newer-post", cover: null);
        var remoteCountBefore = store.Objects.Count;
        var safety = _work.File("safety");

        var result = await ContentSyncService.ApplyAsync(_fixture.ContentDir, store,
            [("posts/old-post.json", SyncAction.Download), ("tags.json", SyncAction.Download), ("posts/newer-post.json", SyncAction.None)], safety);

        Assert.Equal((0, 2), (result.Uploaded, result.Downloaded));
        Assert.Equal(original, File.ReadAllText(_fixture.PostPath("old-post")));                              // R2 の内容に上書き
        Assert.Equal(tags, File.ReadAllText(Path.Combine(_fixture.ContentDir, "tags.json")));                 // 無かったファイルが戻る
        Assert.Contains("直した題", File.ReadAllText(Path.Combine(safety, "posts", "old-post.json")));        // 上書き前の内容が残る
        Assert.True(File.Exists(_fixture.PostPath("newer-post")));                                             // 選ばなかったものは、そのまま
        Assert.Equal(remoteCountBefore, store.Objects.Count);                                                  // R2 は変えない・消さない
        Assert.DoesNotContain(Directory.EnumerateFiles(_fixture.ContentDir, "*.tmp", SearchOption.AllDirectories), _ => true);
    }

    [Fact]
    public async Task UploadingOverwritesTheR2CopyAndLeavesTheLocalFileAlone()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();
        var changed = File.ReadAllText(_fixture.PostPath("old-post")).Replace("既存の記事", "直した題");
        RepoFixture.Write(_fixture.PostPath("old-post"), changed);

        var result = await ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("posts/old-post.json", SyncAction.Upload)], _work.File("safety"));

        Assert.Equal((1, 0), (result.Uploaded, result.Downloaded));
        Assert.Equal(changed, File.ReadAllText(_fixture.PostPath("old-post")));
        Assert.Contains("直した題", System.Text.Encoding.UTF8.GetString((await store.GetAsync("sync/posts/old-post.json"))!));
    }

    [Fact]
    public async Task AnImportThatBreaksTheContentIsRolledBackAndNothingIsUploaded()
    {
        var store = await SyncedStoreAsync();
        var worldsBefore = File.ReadAllText(_fixture.Worlds);
        await store.PutAsync("sync/worlds.json", System.Text.Encoding.UTF8.GetBytes("{ これは JSON ではありません"), "application/json", "no-store");   // R2 の中身が壊れている
        File.Delete(Path.Combine(_fixture.ContentDir, "tags.json"));
        var objectsBefore = store.Objects.Count;

        var e = await Assert.ThrowsAsync<InvalidDataException>(() => ContentSyncService.ApplyAsync(_fixture.ContentDir, store,
            [("worlds.json", SyncAction.Download), ("tags.json", SyncAction.Upload)], _work.File("safety")));

        Assert.Contains("元に戻しました", e.Message);
        Assert.Equal(worldsBefore, File.ReadAllText(_fixture.Worlds));                                         // 取り込んだ分は元に戻る
        Assert.Equal(objectsBefore, store.Objects.Count);                                                      // 送る操作は、取り込みが落ちたら行わない
    }

    [Fact]
    public async Task UnsafePathsAreRefusedAndOddKeysInR2AreNotListed()
    {
        var store = new MemoryObjectStore();
        foreach (var key in new[] { "sync/../evil.json", "sync/readme.txt", "sync//x.json", "sync/a\\b.json" })
            await store.PutAsync(key, [1], "application/json", "no-store");

        var items = await ContentSyncService.CompareAsync(_fixture.ContentDir, store);
        Assert.DoesNotContain(items, i => i.Path.Contains("evil") || i.Path.EndsWith(".txt") || i.Path.Contains('\\') || i.Path.StartsWith('/'));

        foreach (var bad in new[] { "../evil.json", "/abs.json", "a/../b.json", "x.txt", "a\\b.json", "" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [(bad, SyncAction.Upload)], _work.File("safety")));
    }

    [Fact]
    public async Task DeletingALocalOnlyFileKeepsItsContentInTheSafetyCopyAndLeavesR2Alone()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();
        _fixture.WriteExistingPost("newer-post", cover: null);                                                  // ローカルにだけ
        var content = File.ReadAllText(_fixture.PostPath("newer-post"));
        var remoteCount = store.Objects.Count;
        var safety = _work.File("safety");

        var result = await ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("posts/newer-post.json", SyncAction.DeleteLocal)], safety);

        Assert.Equal((0, 0, 1, 0), (result.Uploaded, result.Downloaded, result.DeletedLocal, result.DeletedRemote));
        Assert.False(File.Exists(_fixture.PostPath("newer-post")));
        Assert.Equal(content, File.ReadAllText(Path.Combine(safety, "posts", "newer-post.json")));            // 消す前の内容が残る
        Assert.Equal(remoteCount, store.Objects.Count);
    }

    [Fact]
    public async Task DeletingAnR2OnlyFileKeepsItsContentLocallyAndLeavesTheLocalFilesAlone()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();
        File.Delete(_fixture.PostPath("old-post"));                                                             // ローカルから消えた → R2 にだけ
        var remoteContent = System.Text.Encoding.UTF8.GetString((await store.GetAsync("sync/posts/old-post.json"))!);
        var safety = _work.File("safety");

        var result = await ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("posts/old-post.json", SyncAction.DeleteRemote)], safety);

        Assert.Equal((0, 0, 0, 1), (result.Uploaded, result.Downloaded, result.DeletedLocal, result.DeletedRemote));
        Assert.False(await store.ExistsAsync("sync/posts/old-post.json"));
        Assert.Equal(remoteContent, File.ReadAllText(Path.Combine(safety, ContentSyncService.RemoteBackupFolder, "posts", "old-post.json")));     // R2 には履歴が無いので、消す前の内容をローカルに残す
        Assert.True(File.Exists(Path.Combine(_fixture.ContentDir, "tags.json")));                               // ほかのローカルのファイルは、そのまま
    }

    [Fact]
    public async Task OnlyFilesMissingOnTheOtherSideCanBeDeleted()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();                                                                   // どちらにもある
        var safety = _work.File("safety");

        var local = await Assert.ThrowsAsync<InvalidOperationException>(() => ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("posts/old-post.json", SyncAction.DeleteLocal)], safety));
        Assert.Contains("R2 にもあるファイル", local.Message);
        var remote = await Assert.ThrowsAsync<InvalidOperationException>(() => ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("posts/old-post.json", SyncAction.DeleteRemote)], safety));
        Assert.Contains("ローカルにもあるファイル", remote.Message);

        Assert.True(File.Exists(_fixture.PostPath("old-post")));                                                // どちらも消えていない
        Assert.True(await store.ExistsAsync("sync/posts/old-post.json"));
    }

    [Fact]
    public async Task ALocalDeleteThatBreaksTheContentIsRolledBackAndR2IsNotTouched()
    {
        _fixture.WriteExistingPost();
        var store = await SyncedStoreAsync();
        await store.DeleteAsync("sync/worlds.json");                                                            // worlds.json は、ローカルにだけ。記事がワールドを参照している
        var worldsBefore = File.ReadAllText(_fixture.Worlds);
        var objects = store.Objects.Count;

        var e = await Assert.ThrowsAsync<InvalidDataException>(() => ContentSyncService.ApplyAsync(_fixture.ContentDir, store,
            [("worlds.json", SyncAction.DeleteLocal), ("tags.json", SyncAction.Upload)], _work.File("safety")));

        Assert.Contains("元に戻しました", e.Message);
        Assert.Equal(worldsBefore, File.ReadAllText(_fixture.Worlds));                                          // 消したファイルが戻る
        Assert.Equal(objects, store.Objects.Count);                                                             // R2 は変えない
    }

    [Fact]
    public async Task DownloadingAFileThatIsNotInR2IsAnErrorNotASilentSkip()
    {
        var store = new MemoryObjectStore();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("tags.json", SyncAction.Download)], _work.File("safety")));
        Assert.Contains("R2 にファイルがありません", e.Message);
    }
}
