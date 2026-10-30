using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>記事の中身（content）の、設定したフォルダ・R2 への控えと復元・リポジトリの外へのコピー。</summary>
public sealed class ContentBackupTests : IDisposable
{
    private readonly RepoFixture _fixture = new();
    private readonly TempDir _work = new();

    public void Dispose()
    {
        _fixture.Dispose();
        _work.Dispose();
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 21, 30, 0, TimeSpan.FromHours(9));

    // ---------------------------------------------------------------- 控え用のバケット

    [Fact]
    public void TheBackupBucketMustBeSetValidAndDifferentFromThePhotoBucket()
    {
        Assert.Contains("設定されていません", Assert.Throws<InvalidOperationException>(() => ContentBackupService.RequireBackupBucket("", "fara-photos")).Message);
        Assert.Contains("設定されていません", Assert.Throws<InvalidOperationException>(() => ContentBackupService.RequireBackupBucket(null, "fara-photos")).Message);
        Assert.Contains("3〜63", Assert.Throws<InvalidOperationException>(() => ContentBackupService.RequireBackupBucket("Bad Name", "fara-photos")).Message);
        Assert.Contains("同じにはできません", Assert.Throws<InvalidOperationException>(() => ContentBackupService.RequireBackupBucket("fara-photos", "fara-photos")).Message);
        Assert.Equal("fara-content-backup", ContentBackupService.RequireBackupBucket(" fara-content-backup ", "fara-photos"));
    }

    // ---------------------------------------------------------------- 控えと復元

    [Fact]
    public async Task ASnapshotRoundTripsThroughTheStoreAndRestoreKeepsASafetyCopyAndRemovesFilesNotInTheSnapshot()
    {
        _fixture.WriteExistingPost();
        var store = new MemoryObjectStore();
        var info = await ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now);
        Assert.Equal("20261027-213000", info.Id);
        Assert.Equal(ContentBackupService.ContentFiles(_fixture.ContentDir).Count, info.FileCount);
        Assert.All(store.Objects.Keys, k => Assert.StartsWith("snapshots/20261027-213000/", k));       // 写真のバケットの形（yyyy-mm/…）には置かない
        var listed = Assert.Single(await ContentBackupService.ListAsync(store));
        Assert.Equal(info.Id, listed.Id);

        // 控えのあとで、記事を直し、新しい記事を足す
        var original = File.ReadAllText(_fixture.PostPath("old-post"));
        RepoFixture.Write(_fixture.PostPath("old-post"), original.Replace("既存の記事", "あとで直した題"));
        _fixture.WriteExistingPost("newer-post", cover: null);

        var safety = _work.File("safety");
        var restored = await ContentBackupService.RestoreAsync(store, info.Id, _fixture.ContentDir, safety);

        Assert.Equal(info.FileCount, restored);
        Assert.Equal(original, File.ReadAllText(_fixture.PostPath("old-post")));                      // 控えの内容に戻る
        Assert.False(File.Exists(_fixture.PostPath("newer-post")));                                    // 控えに無かった記事は消える
        Assert.True(File.Exists(Path.Combine(safety, "posts", "newer-post.json")));                    // 消える前の内容は、安全の控えに残る
        Assert.Contains("あとで直した題", File.ReadAllText(Path.Combine(safety, "posts", "old-post.json")));
    }

    [Fact]
    public async Task EachBackupIsANewSnapshotAndNeverOverwritesAnOldOne()
    {
        var store = new MemoryObjectStore();
        var first = await ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now);
        var second = await ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now.AddMinutes(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now));   // 同じ時刻は作らない

        var ids = (await ContentBackupService.ListAsync(store)).Select(s => s.Id).ToList();
        Assert.Equal([second.Id, first.Id], ids);                                                       // 新しい順
    }

    [Fact]
    public async Task RestoreRefusesACorruptedSnapshotAndChangesNothing()
    {
        _fixture.WriteExistingPost();
        var store = new MemoryObjectStore();
        var info = await ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now);
        var before = File.ReadAllText(_fixture.PostPath("old-post"));
        var key = store.Objects.Keys.First(k => k.EndsWith("posts/old-post.json", StringComparison.Ordinal));
        await store.PutAsync(key, System.Text.Encoding.UTF8.GetBytes("{ }"), "application/json", "private, no-store");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => ContentBackupService.RestoreAsync(store, info.Id, _fixture.ContentDir, _work.File("safety")));

        Assert.Contains("壊れています", ex.Message);
        Assert.Equal(before, File.ReadAllText(_fixture.PostPath("old-post")));
        Assert.False(Directory.Exists(_work.File("safety")));
    }

    [Fact]
    public async Task RestoreRefusesContentThatCannotBeLoadedAndChangesNothing()
    {
        // 壊れた content からでも控えは作れる（控えは何も検査しない）が、そこから戻すことはできない
        RepoFixture.Write(_fixture.PostPath("broken"), "{ \"schema\": 1, \"slug\": \"broken\" }");
        var store = new MemoryObjectStore();
        var info = await ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now);
        File.Delete(_fixture.PostPath("broken"));
        _fixture.WriteExistingPost();
        var before = File.ReadAllText(_fixture.PostPath("old-post"));

        await Assert.ThrowsAnyAsync<Exception>(() => ContentBackupService.RestoreAsync(store, info.Id, _fixture.ContentDir, _work.File("safety")));

        Assert.Equal(before, File.ReadAllText(_fixture.PostPath("old-post")));
        Assert.False(File.Exists(_fixture.PostPath("broken")));
        Assert.False(Directory.Exists(_work.File("safety")));
    }

    [Fact]
    public async Task AnEmptyContentFolderCannotBeBackedUp()
    {
        var empty = Directory.CreateDirectory(_work.File("empty")).FullName;
        await Assert.ThrowsAsync<InvalidOperationException>(() => ContentBackupService.BackupAsync(empty, new MemoryObjectStore(), Now));
    }

    // ---------------------------------------------------------------- リポジトリの外へのコピー

    [Fact]
    public void CopyContentCopiesTheWorkingFilesIncludingUncommittedAndNewOnesAndLeavesTheSourceAlone()
    {
        _fixture.WriteExistingPost();
        RepoFixture.Write(Path.Combine(_fixture.ContentDir, "outfits.json"), "{ }");                    // Git に入れていない新しいファイル
        RepoFixture.Write(Path.Combine(_fixture.ContentDir, "catalog", "photos.json"), "{ \"version\": 1, \"entries\": [] }");
        var dest = _work.File("outside");

        var count = ContentBackupService.CopyContent(_fixture.ContentDir, dest);

        Assert.Equal(Directory.EnumerateFiles(_fixture.ContentDir, "*", SearchOption.AllDirectories).Count(), count);
        Assert.True(File.Exists(Path.Combine(dest, "outfits.json")));
        Assert.True(File.Exists(Path.Combine(dest, "posts", "old-post.json")));
        Assert.True(File.Exists(_fixture.PostPath("old-post")), "元は変えない（移さない）");
    }

    [Fact]
    public void CopyContentRefusesANonEmptyDestinationAndADestinationInsideTheSource()
    {
        var full = Directory.CreateDirectory(_work.File("full")).FullName;
        File.WriteAllText(Path.Combine(full, "x.txt"), "x");
        Assert.Contains("空ではありません", Assert.Throws<InvalidOperationException>(() => ContentBackupService.CopyContent(_fixture.ContentDir, full)).Message);
        Assert.Contains("中には作れません", Assert.Throws<InvalidOperationException>(() => ContentBackupService.CopyContent(_fixture.ContentDir, Path.Combine(_fixture.ContentDir, "inner"))).Message);
    }

    [Fact]
    public void CopyContentRemovesTheCopyWhenItCannotBeLoadedAsContent()
    {
        RepoFixture.Write(_fixture.PostPath("broken"), "{ \"schema\": 1, \"slug\": \"broken\" }");
        var dest = _work.File("outside");

        Assert.ThrowsAny<Exception>(() => ContentBackupService.CopyContent(_fixture.ContentDir, dest));

        Assert.False(Directory.Exists(dest));
    }

    // ---------------------------------------------------------------- 設定したフォルダ

    [Fact]
    public void TheContentFolderDefaultsToTheRepositoryAndFollowsTheSettingWithoutFallingBack()
    {
        Assert.Null(_fixture.Repo.ConfiguredContentDir);
        Assert.Equal(_fixture.ContentDir, _fixture.Repo.ContentDir);                                    // 既定は変わらない

        var outside = Directory.CreateDirectory(_work.File("elsewhere")).FullName;
        LocalSettings.SetLocalConfig(_fixture.Repo.LocalConfigPath, new Dictionary<string, string?> { ["contentDir"] = outside });
        var repo = _fixture.Repo;
        Assert.Equal(outside, repo.ContentDir);
        Assert.Equal(Path.Combine(outside, "catalog", "photos.json"), repo.ProductionLedgerPath);        // 台帳も同じフォルダに従う

        // 設定したフォルダが無くなっても、リポジトリの content には切り替えない
        Directory.Delete(outside);
        Assert.Equal(outside, _fixture.Repo.ConfiguredContentDir);
        Assert.Contains("見つかりません", Assert.Throws<DirectoryNotFoundException>(() => _fixture.Repo.ContentDir).Message);   // 黙って別のフォルダを使わない
    }
}
