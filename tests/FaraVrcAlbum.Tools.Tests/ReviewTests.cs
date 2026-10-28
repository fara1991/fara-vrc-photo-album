using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using TestImages = FaraVrcAlbum.Images.Tests.TestImages;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>削除の順序（記事 → R2 → 台帳）と、表紙の暗黙指定（cover 省略時は先頭の写真）。</summary>
public sealed class RemoveOrderTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private PhotoLedger Ledger() => _fixture.Ledger((RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"));

    private static async Task<MemoryObjectStore> StoreWithPhoto1()
    {
        var store = new MemoryObjectStore();
        foreach (var suffix in new[] { "_640.webp", "_1280.webp", "_2048.webp", "_og.jpg" })
        {
            await store.PutAsync(RepoFixture.Key1 + suffix, [1], "image/webp", ObjectHeaders.ImmutableImage);
        }
        return store;
    }

    [Fact]
    public void OmittedCoverMakesTheFirstPhotoBlocked()
    {
        _fixture.WriteExistingPost("old-post", cover: null); // 表紙の省略 = 先頭の写真（Key1）

        var error = Assert.Throws<InvalidOperationException>(() => RemoveService.Plan(RepoFixture.Key1, Ledger(), _fixture.Editor, "https://img.example.test/"));
        Assert.Contains("表紙", error.Message);
    }

    [Fact]
    public void OmittedCoverDoesNotBlockAPhotoThatIsNotFirst()
    {
        _fixture.WriteExistingPost("old-post", cover: null);
        var ledger = _fixture.Ledger((RepoFixture.Key2, "2222", LedgerStates.Uploaded, "2026-10-23T21:40:01+09:00"));

        var plan = RemoveService.Plan(RepoFixture.Key2, ledger, _fixture.Editor, "https://img.example.test/");

        Assert.Single(plan.PostPaths);
    }

    [Fact]
    public async Task ValidationFailureRestoresThePostAndLeavesR2AndLedgerAlone()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key2);
        RepoFixture.Write(Path.Combine(_fixture.Posts, "broken.json"), "{ \"schema\": 2 }");
        var before = File.ReadAllText(_fixture.PostPath("old-post"));
        var ledger = Ledger();
        var store = await StoreWithPhoto1();
        var plan = RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/");

        await Assert.ThrowsAsync<InvalidDataException>(() => RemoveService.ApplyAsync(plan, "fara-vrc-photos", ledger, _fixture.LedgerPath, store, _fixture.Editor, Now));

        Assert.Equal(before, File.ReadAllText(_fixture.PostPath("old-post")));
        Assert.Equal(4, store.Objects.Count);
        Assert.Equal(0, store.DeleteCount);
        Assert.Equal(LedgerStates.Uploaded, ledger.BySha["1111"].State);
        Assert.False(File.Exists(_fixture.LedgerPath));
    }

    [Fact]
    public async Task R2DeleteFailureRestoresThePostAndKeepsTheLedgerUploaded()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key2);
        var before = File.ReadAllText(_fixture.PostPath("old-post"));
        var ledger = Ledger();
        var store = await StoreWithPhoto1();
        store.OnDelete = key => key.EndsWith("_1280.webp", StringComparison.Ordinal) ? new IOException("通信エラー（試験）") : null;
        var plan = RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/");

        await Assert.ThrowsAsync<RemoveService.RemoveIncompleteException>(() => RemoveService.ApplyAsync(plan, "fara-vrc-photos", ledger, _fixture.LedgerPath, store, _fixture.Editor, Now));

        Assert.Equal(before, File.ReadAllText(_fixture.PostPath("old-post")));
        Assert.Equal(LedgerStates.Uploaded, ledger.BySha["1111"].State);
        Assert.False(File.Exists(_fixture.LedgerPath));
    }

    [Fact]
    public async Task SuccessRemovesFromPostsThenR2ThenMarksTheLedgerRemoved()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key2);
        var ledger = Ledger();
        var store = await StoreWithPhoto1();
        var plan = RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/");

        await RemoveService.ApplyAsync(plan, "fara-vrc-photos", ledger, _fixture.LedgerPath, store, _fixture.Editor, Now);

        Assert.Empty(store.Objects);
        Assert.Equal(LedgerStates.Removed, ledger.BySha["1111"].State);
        Assert.True(File.Exists(_fixture.LedgerPath));
    }
}

/// <summary>秘密の除去（形・表示の経路・--repo の .env.local）。</summary>
public sealed class RedactionReviewTests : IDisposable
{
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SixtyFourLowerHexIsMasked()
    {
        var sha256 = new string('f', 64);

        var masked = Redactor.Redact($"hash {sha256} done", []);

        Assert.DoesNotContain(sha256, masked);
    }

    [Fact]
    public void FortyLowerHexGitShaIsKept()
    {
        const string gitSha = "3f2a9c1d8e7b6a5049382716a5b4c3d2e1f00918";

        Assert.Equal($"commit {gitSha}", Redactor.Redact($"commit {gitSha}", []));
    }

    [Fact]
    public void MixedCaseFortyCharSecretIsMasked()
    {
        const string secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKY1";
        Assert.Equal(40, secret.Length);

        Assert.DoesNotContain(secret, Redactor.Redact($"key {secret}", []));
    }

    [Fact]
    public async Task FileErrorsAreRedactedBeforeTheyAreReported()
    {
        using var dir = new TempDir();
        var inbox = Path.Combine(dir.Path, "inbox", "2026-10");
        Directory.CreateDirectory(inbox);
        var file = Path.Combine(inbox, "VRChat_2026-10-23_21-33-12.345_640x360.png");
        File.WriteAllBytes(file, TestImages.Png(640, 360, TestImages.XmpAttributes()));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-10));
        var store = new MemoryObjectStore { OnPut = (_, _) => new IOException("rejected with token SUPERSECRET1234") };

        var report = await UploadService.RunAsync(
            Path.Combine(dir.Path, "inbox"), Path.Combine(dir.Path, "uploaded"),
            new UploadOptions { Apply = true, Secrets = ["SUPERSECRET1234"] },
            PhotoLedger.Empty(), Path.Combine(dir.Path, "photos.json"), store, DateTimeOffset.UtcNow);

        var item = Assert.Single(report.Items);
        Assert.NotNull(item.Error);
        Assert.DoesNotContain("SUPERSECRET1234", item.Error);
        Assert.Contains("***", item.Error);
    }

    [Fact]
    public void KnownSecretsIncludeTheRepoEnvFileAndAreMasked()
    {
        RepoFixture.Write(Path.Combine(_fixture.Root, ".env.local"), "R2_SECRET_ACCESS_KEY=envfilesecret987\n");
        var repo = _fixture.Repo;

        Assert.Contains("envfilesecret987", Secrets.Known(repo));
        Assert.DoesNotContain("envfilesecret987", Secrets.Redact("failed envfilesecret987", repo));
    }
}

public sealed class MoveFailureTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task AMoveFailureIsOnlyThatFilesFailureAndTheFileStaysInTheInbox()
    {
        var inbox = Path.Combine(_dir.Path, "inbox", "2026-10");
        Directory.CreateDirectory(inbox);
        var file = Path.Combine(inbox, "VRChat_2026-10-23_21-33-12.345_640x360.png");
        File.WriteAllBytes(file, TestImages.Png(640, 360, TestImages.XmpAttributes()));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-10));
        // 移動先の月のフォルダを作れないように、同じ名前のファイルを置く
        Directory.CreateDirectory(Path.Combine(_dir.Path, "uploaded"));
        File.WriteAllText(Path.Combine(_dir.Path, "uploaded", "2026-10"), "blocker");
        var ledgerPath = Path.Combine(_dir.Path, "content", "catalog", "photos.json");

        var report = await UploadService.RunAsync(
            Path.Combine(_dir.Path, "inbox"), Path.Combine(_dir.Path, "uploaded"),
            new UploadOptions { Apply = true }, PhotoLedger.Empty(), ledgerPath, new MemoryObjectStore(), DateTimeOffset.UtcNow);

        var item = Assert.Single(report.Items);
        Assert.NotNull(item.Error);
        Assert.Contains("移せませんでした", item.Error);
        Assert.NotNull(item.Result); // R2 と台帳は登録済み
        Assert.True(File.Exists(file));
        Assert.True(File.Exists(ledgerPath));
    }
}

/// <summary>album cover: 元の PNG のハッシュ照合・uploaded の状態・本番バケット・dry-run。</summary>
public sealed class CoverServiceTests : IDisposable
{
    private readonly RepoFixture _fixture = new();
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        _fixture.Dispose();
        _dir.Dispose();
    }

    private string UploadRoot => _dir.Path;

    private static DateTimeOffset TakenAt => new(2026, 10, 23, 21, 33, 12, 345, TimeSpan.FromHours(9));

    /// <summary>PNG を uploaded\2026-10 に置き、その内容ハッシュで台帳に登録する。</summary>
    private (string Key, PhotoLedger Ledger) Register(byte[] original, string state = LedgerStates.Uploaded)
    {
        var sha = Hashing.Sha256Hex(original);
        var key = PhotoKeys.Build(TakenAt, sha);
        var folder = Path.Combine(UploadRoot, "uploaded", "2026-10");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "VRChat_2026-10-23_21-33-12.345_640x360.png"), original);
        var ledger = PhotoLedger.Empty();
        ledger.Put(sha, new LedgerEntry
        {
            Key = key,
            State = state,
            TakenAt = PhotoKeys.TakenAtText(TakenAt),
            W = 640,
            H = 360,
            Color = "abcdef",
            Bytes = new Dictionary<string, long> { ["640"] = 1, ["1280"] = 1, ["2048"] = 1 },
        });
        return (key, ledger);
    }

    [Fact]
    public async Task DryRunWritesNothing()
    {
        var (key, ledger) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()));
        var store = new MemoryObjectStore();

        var plan = await CoverService.PlanAsync(key, ledger, UploadRoot, store);

        Assert.False(plan.AlreadyInR2);
        Assert.True(File.Exists(plan.SourcePath));
        Assert.Equal(0, store.PutCount);
        Assert.False(File.Exists(_fixture.LedgerPath));
        Assert.False(ledger.FindByKey(key)!.Bytes.ContainsKey("og"));
    }

    [Fact]
    public async Task ApplyPutsTheOgAndRecordsItsSizeInTheLedger()
    {
        var (key, ledger) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()));
        var store = new MemoryObjectStore();
        var plan = await CoverService.PlanAsync(key, ledger, UploadRoot, store);

        await CoverService.ApplyAsync(plan, "fara-vrc-photos", ledger, _fixture.LedgerPath, store);

        Assert.Equal(1, store.PutCount);
        var og = store.Objects[key + "_og.jpg"];
        Assert.Equal("image/jpeg", og.ContentType);
        Assert.Equal(ObjectHeaders.ImmutableImage, og.CacheControl);
        Assert.Equal(og.Data.LongLength, ledger.FindByKey(key)!.Bytes["og"]);
        Assert.True(File.Exists(_fixture.LedgerPath));
    }

    [Fact]
    public async Task WhenTheOgIsAlreadyInR2ItDoesNotPutAgain()
    {
        var (key, ledger) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()));
        var store = new MemoryObjectStore();
        await store.PutAsync(key + "_og.jpg", [1, 2, 3], "image/jpeg", ObjectHeaders.ImmutableImage);
        var plan = await CoverService.PlanAsync(key, ledger, UploadRoot, store);
        var putsBefore = store.PutCount;

        await CoverService.ApplyAsync(plan, "fara-vrc-photos", ledger, _fixture.LedgerPath, store);

        Assert.True(plan.AlreadyInR2);
        Assert.Equal(putsBefore, store.PutCount);
        Assert.True(ledger.FindByKey(key)!.Bytes.ContainsKey("og"));
    }

    [Fact]
    public async Task HashMismatchIsNotFound()
    {
        var (key, ledger) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()));
        // uploaded\ に同じ名前で別の内容を置く（ハッシュが台帳と違う）
        File.WriteAllBytes(Path.Combine(UploadRoot, "uploaded", "2026-10", "VRChat_2026-10-23_21-33-12.345_640x360.png"), TestImages.Png(320, 180, TestImages.XmpAttributes()));

        await Assert.ThrowsAsync<FileNotFoundException>(() => CoverService.PlanAsync(key, ledger, UploadRoot, new MemoryObjectStore()));
    }

    [Fact]
    public async Task RemovedPhotoIsRefused()
    {
        var (key, ledger) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()), LedgerStates.Removed);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CoverService.PlanAsync(key, ledger, UploadRoot, new MemoryObjectStore()));
        Assert.Contains("uploaded ではありません", error.Message);
    }

    [Fact]
    public async Task UnknownKeyIsRefused()
    {
        var (_, ledger) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CoverService.PlanAsync("2026-10/20261023-213312-345_ffffffff", ledger, UploadRoot, new MemoryObjectStore()));
        Assert.Contains("台帳にありません", error.Message);
    }

    [Fact]
    public async Task NonProductionBucketIsRefusedBeforeAnyStoreIsOpened()
    {
        var (key, _) = Register(TestImages.Png(640, 360, TestImages.XmpAttributes()));

        var exit = await Program.Main(["cover", key, "--repo", _fixture.Root, "--bucket", "fara-vrc-photos-test", "--upload-root", UploadRoot]);

        Assert.Equal(2, exit);
        Assert.False(File.Exists(_fixture.LedgerPath));
    }
}
