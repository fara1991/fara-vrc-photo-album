using System.Text;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>データの安全: 壊れた控えの除外・同期の失敗時の巻き戻し・記事の書き換えの巻き戻し（バイト単位）・パスの検査。</summary>
public sealed class DataSafetyTests : IDisposable
{
    private readonly RepoFixture _fixture = new();
    private readonly TempDir _work = new();

    public void Dispose()
    {
        _fixture.Dispose();
        _work.Dispose();
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 21, 30, 0, TimeSpan.FromHours(9));
    private const string PrivateCache = "private, no-store";

    [Fact]
    public async Task ABrokenManifestIsLeftOutOfTheListAndTheOtherSnapshotsStillShow()
    {
        _fixture.WriteExistingPost();
        var store = new MemoryObjectStore();
        var good = await ContentBackupService.BackupAsync(_fixture.ContentDir, store, Now);

        // 形の違う目録（files が無い・path が文字列でない・JSON として壊れている）を 3 つ置く
        await store.PutAsync("snapshots/20261027-220000/manifest.json", Encoding.UTF8.GetBytes("{\"createdAt\":\"x\"}"), "application/json", PrivateCache);
        await store.PutAsync("snapshots/20261027-221000/manifest.json", Encoding.UTF8.GetBytes("{\"createdAt\":\"x\",\"files\":[{\"path\":1,\"bytes\":1,\"sha256\":\"a\"}]}"), "application/json", PrivateCache);
        await store.PutAsync("snapshots/20261027-222000/manifest.json", Encoding.UTF8.GetBytes("{ not json"), "application/json", PrivateCache);

        var listed = await ContentBackupService.ListAsync(store);
        Assert.Equal([good.Id], listed.Select(s => s.Id).ToList());
    }

    [Theory]
    [InlineData("posts/a.json", true)]
    [InlineData("worlds.json", true)]
    [InlineData("../secret.json", false)]
    [InlineData("posts/../../x.json", false)]
    [InlineData("posts/./a.json", false)]
    [InlineData("/abs.json", false)]
    [InlineData("posts\\a.json", false)]
    [InlineData("posts//a.json", false)]
    [InlineData("a.txt", false)]
    [InlineData("", false)]
    public void ContentPathIsSafeRejectsEveryWayOutOfTheContentFolder(string path, bool expected)
    {
        Assert.Equal(expected, ContentPath.IsSafe(path));
    }

    [Fact]
    public async Task ASyncDownloadThatMakesTheContentUnreadableIsRolledBackAndSaysSo()
    {
        _fixture.WriteExistingPost();
        var before = File.ReadAllBytes(_fixture.PostPath("old-post"));
        var store = new MemoryObjectStore();
        await store.PutAsync("sync/posts/old-post.json", Encoding.UTF8.GetBytes("{ broken"), "application/json", PrivateCache);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ContentSyncService.ApplyAsync(_fixture.ContentDir, store, [("posts/old-post.json", SyncAction.Download)], _work.File("safety")));

        Assert.Contains("読めなくなる", error.Message);
        Assert.Equal(before, File.ReadAllBytes(_fixture.PostPath("old-post")));                       // ローカルは元のバイトに戻る
    }

    [Fact]
    public void AFailedEditRestoresTheExactBytesIncludingBomAndLineEndings()
    {
        var path = _work.File("post.json");
        var original = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("{\r\n  \"title\": \"題\"\r\n}\r\n")).ToArray();
        File.WriteAllBytes(path, original);

        var error = Assert.Throws<InvalidOperationException>(() =>
            FileSnapshot.Run([path], () =>
            {
                File.WriteAllText(path, "{\n\"title\": \"壊した\"\n}\n");                             // 改行と BOM が変わる書き方
                throw new InvalidOperationException("検査に落ちた");
            }));

        Assert.Equal("検査に落ちた", error.Message);                                                     // 元の例外をそのまま投げ直す
        Assert.Equal(original, File.ReadAllBytes(path));                                                 // バイト単位で戻る
    }

    [Fact]
    public void ARunThatSucceedsKeepsItsResultAndDoesNotRestore()
    {
        var path = _work.File("post.json");
        File.WriteAllText(path, "old");
        var result = FileSnapshot.Run([path], () =>
        {
            File.WriteAllText(path, "new");
            return 42;
        });
        Assert.Equal(42, result);
        Assert.Equal("new", File.ReadAllText(path));
    }
}
