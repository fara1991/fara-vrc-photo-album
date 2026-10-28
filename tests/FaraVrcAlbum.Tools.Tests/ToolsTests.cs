using System.Net;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using TestImages = FaraVrcAlbum.Images.Tests.TestImages;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

public sealed class ArgsAndRepoTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ParsesCommandPositionalsValuesAndSwitches()
    {
        var args = Args.Parse(["draft", "--post", "night", "--keys", "a,b", "--append", "extra"]);

        Assert.Equal("draft", args.Command);
        Assert.Equal("night", args.Value("post"));
        Assert.Equal("a,b", args.Value("keys"));
        Assert.True(args.Has("append"));
        Assert.False(args.Has("apply"));
        Assert.Equal(["extra"], args.Positional);
    }

    [Fact]
    public void ValueOptionWithoutValueIsAUsageError()
    {
        Assert.Throws<UsageException>(() => Args.Parse(["upload", "--bucket"]));
        Assert.Throws<UsageException>(() => Args.Parse(["upload", "--bucket", "--apply"]));
    }

    [Fact]
    public void DiscoverFindsTheSolutionFromANestedFolder()
    {
        using var fixture = new RepoFixture();
        var nested = Path.Combine(fixture.Root, "src", "deep");
        Directory.CreateDirectory(nested);

        var repo = RepoContext.Discover(nested);

        Assert.Equal(Path.GetFullPath(fixture.Root), repo.Root);
        Assert.Equal("https://img.example.test/", repo.ImageBaseUrl);
    }

    [Fact]
    public void BucketsSplitTheLedgerSoTestRunsNeverTouchTheProductionLedger()
    {
        using var fixture = new RepoFixture();
        var repo = fixture.Repo;

        Assert.True(RepoContext.IsProductionBucket(RepoContext.ProductionBucket));
        Assert.Equal(repo.ProductionLedgerPath, repo.LedgerPathFor(RepoContext.ProductionBucket));
        Assert.NotEqual(repo.ProductionLedgerPath, repo.LedgerPathFor("fara-vrc-photos-test"));
        Assert.StartsWith(repo.LocalDir, repo.LedgerPathFor("fara-vrc-photos-test"));
    }

    [Fact]
    public void LockFileRefusesASecondRunUntilDisposed()
    {
        var path = Path.Combine(_dir.Path, "album.lock");

        using (LockFile.Acquire(path))
        {
            Assert.Throws<UsageException>(() => LockFile.Acquire(path));
        }

        using (LockFile.Acquire(path))
        {
        }
    }
}

public sealed class DraftTests : IDisposable
{
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(9));

    private PhotoLedger Ledger() => _fixture.Ledger(
        ("2026-10/20261024-090000-000_11223344", "1111", LedgerStates.Uploaded, "2026-10-24T09:00:00+09:00"),
        ("2026-10/20261025-100000-000_55667788", "2222", LedgerStates.Uploaded, "2026-10-25T10:00:00+09:00"),
        ("2026-10/20261026-100000-000_99aabbcc", "3333", LedgerStates.Removed, "2026-10-26T10:00:00+09:00"));

    [Fact]
    public void CreatesDraftFromUploadedPhotosInTakenOrderAndRegistersTheWorld()
    {
        var result = DraftService.Run(_fixture.Editor, Ledger(), new DraftRequest
        {
            Slug = "new-post",
            Title = "新しい記事",
            Since = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9)),
        }, Now);

        Assert.True(result.Created);
        Assert.Equal(2, result.Added);
        var post = ContentEditor.ReadObject(_fixture.PostPath("new-post"));
        Assert.True(post["draft"]!.GetValue<bool>());
        var photos = post["photos"]!.AsArray();
        Assert.Equal(["2026-10/20261024-090000-000_11223344", "2026-10/20261025-100000-000_55667788"], photos.Select(p => p!["key"]!.GetValue<string>()));
        Assert.Equal("新しいワールド", photos[1]!["world"]!["name"]!.GetValue<string>());

        var worlds = ContentEditor.ReadObject(_fixture.Worlds);
        Assert.Equal("新しいワールド", worlds[RepoFixture.WorldNew]!["name"]!.GetValue<string>());
        Assert.Equal("旧名のワールド", worlds[RepoFixture.WorldOld]!["name"]!.GetValue<string>());
        _fixture.Editor.ValidateOrThrow();
    }

    [Fact]
    public void DifferentWorldNameOfANewerPhotoReplacesTheNameAndKeepsTheOldOneAsAlias()
    {
        // 既存のマスタ（nameAt 無し）: 名前の違う新しい写真で name を更新し、旧名は aliases に残す（詳細は WorldNameTests）
        var ledger = _fixture.Ledger(("2026-10/20261024-090000-000_11223344", "1111", LedgerStates.Uploaded, "2026-10-24T09:00:00+09:00"));
        ledger.BySha["1111"].World = new LedgerWorld { Id = RepoFixture.WorldOld, Name = "改名後の名前" };

        var result = DraftService.Run(_fixture.Editor, ledger, new DraftRequest { Slug = "alias-post", Title = "改名", Keys = [ledger.BySha["1111"].Key] }, Now);

        Assert.Contains(result.Messages, m => m.Contains("名前を更新"));
        var worlds = ContentEditor.ReadObject(_fixture.Worlds);
        Assert.Equal("改名後の名前", worlds[RepoFixture.WorldOld]!["name"]!.GetValue<string>());
        Assert.Equal("旧名のワールド", worlds[RepoFixture.WorldOld]!["aliases"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void NewPostNeedsATitleAndExistingPostNeedsAppend()
    {
        _fixture.WriteExistingPost("old-post");

        Assert.Throws<ArgumentException>(() => DraftService.Run(_fixture.Editor, Ledger(), new DraftRequest { Slug = "x-post", Keys = ["2026-10/20261024-090000-000_11223344"] }, Now));
        Assert.Throws<InvalidOperationException>(() => DraftService.Run(_fixture.Editor, Ledger(), new DraftRequest { Slug = "old-post", Title = "t", Keys = ["2026-10/20261024-090000-000_11223344"] }, Now));
    }

    [Fact]
    public void AppendAddsOnlyPhotosThatAreNotAlreadyThere()
    {
        _fixture.WriteExistingPost("old-post");
        var ledger = Ledger();
        var keys = new[] { RepoFixture.Key1, "2026-10/20261024-090000-000_11223344" };
        ledger.Put("aaaa", new LedgerEntry { Key = RepoFixture.Key1, State = LedgerStates.Uploaded, TakenAt = "2026-10-23T21:33:12+09:00", W = 640, H = 360, Color = "1b2a3c", Bytes = new() { ["640"] = 1 } });

        var result = DraftService.Run(_fixture.Editor, ledger, new DraftRequest { Slug = "old-post", Keys = keys, Append = true }, Now);

        Assert.Equal(1, result.Added);
        var photos = ContentEditor.ReadObject(_fixture.PostPath("old-post"))["photos"]!.AsArray();
        Assert.Equal(3, photos.Count);
        Assert.Equal("2026-10/20261024-090000-000_11223344", photos[2]!["key"]!.GetValue<string>());
    }

    [Fact]
    public void SelectionRejectsKeysThatAreNotUploadedAndNeedsASelection()
    {
        Assert.Throws<InvalidOperationException>(() => DraftService.Run(_fixture.Editor, Ledger(), new DraftRequest { Slug = "p", Title = "t", Keys = ["2026-10/20261026-100000-000_99aabbcc"] }, Now));
        Assert.Throws<ArgumentException>(() => DraftService.Run(_fixture.Editor, Ledger(), new DraftRequest { Slug = "p", Title = "t" }, Now));
    }

    [Fact]
    public void FailedValidationRollsBackPostAndWorlds()
    {
        RepoFixture.Write(Path.Combine(_fixture.Posts, "broken.json"), "{ \"schema\": 2 }");
        var worldsBefore = File.ReadAllText(_fixture.Worlds);

        Assert.Throws<InvalidDataException>(() => DraftService.Run(_fixture.Editor, Ledger(), new DraftRequest { Slug = "rolled-back", Title = "t", Since = DateTimeOffset.MinValue }, Now));

        Assert.False(File.Exists(_fixture.PostPath("rolled-back")));
        Assert.Equal(worldsBefore, File.ReadAllText(_fixture.Worlds));
    }
}

public sealed class RemoveTests : IDisposable
{
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void PlanRefusesWhileCoverOrBodyStillPointsAtThePhoto()
    {
        _fixture.WriteExistingPost("old-post", cover: RepoFixture.Key1);
        var ledger = _fixture.Ledger((RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"));

        var error = Assert.Throws<InvalidOperationException>(() => RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/"));
        Assert.Contains("cover", error.Message);

        RepoFixture.Write(_fixture.PostPath("old-post"), File.ReadAllText(_fixture.PostPath("old-post")).Replace("\"cover\": \"" + RepoFixture.Key1 + "\",", "").Replace("本文です。", "本文に " + RepoFixture.Key1));
        var bodyError = Assert.Throws<InvalidOperationException>(() => RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/"));
        Assert.Contains("本文", bodyError.Message);
    }

    [Fact]
    public async Task ApplyDeletesR2AndMarksRemovedWhenNoPostUsesThePhoto()
    {
        var ledger = _fixture.Ledger((RepoFixture.Key1, "1111", LedgerStates.Uploaded, "2026-10-23T21:33:12+09:00"));
        var store = new MemoryObjectStore();
        foreach (var suffix in new[] { "_640.webp", "_1280.webp", "_2048.webp", "_og.jpg" })
        {
            await store.PutAsync(RepoFixture.Key1 + suffix, [1], "image/webp", ObjectHeaders.ImmutableImage);
        }

        var plan = RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/");
        Assert.Equal(4, plan.R2Keys.Count);
        Assert.Equal("https://img.example.test/" + RepoFixture.Key1 + "_og.jpg", plan.PurgeUrls[^1]);
        await RemoveService.ApplyAsync(plan, "fara-vrc-photos", ledger, _fixture.LedgerPath, store, new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero));

        Assert.Empty(store.Objects);
        Assert.Equal(LedgerStates.Removed, ledger.BySha["1111"].State);
        Assert.True(File.Exists(_fixture.LedgerPath));
        Assert.Throws<InvalidOperationException>(() => RemoveService.Plan(RepoFixture.Key1, ledger, _fixture.Editor, "https://img.example.test/"));
    }
}

public sealed class VerifyTests : IDisposable
{
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private const string ImmutableCache = "public, max-age=31536000, immutable";

    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new FakeHandler(respond));

    private List<VerifyPhoto> Photos()
    {
        _fixture.WriteExistingPost();
        return VerifyService.Collect(ContentLoader.Load(_fixture.ContentDir)).ToList();
    }

    [Fact]
    public async Task PassesWhenEveryObjectHasTheRightHeadersAndSize()
    {
        var photos = Photos();
        using var http = Client(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.EndsWith("_og.jpg")) return FakeImages.Response(request, "image/jpeg", ImmutableCache, [0xFF, 0xD8]);
            var (width, height) = url.Contains(RepoFixture.Key2) ? (1280, 720) : (640, 360);
            return FakeImages.Response(request, "image/webp", ImmutableCache, FakeImages.WebpHeader(width, height));
        });

        var problems = await VerifyService.RunAsync(photos, "https://img.example.test/", http);

        Assert.Empty(problems);
    }

    [Fact]
    public async Task ReportsMissingObjectsWrongHeadersAndWrongSize()
    {
        var photos = Photos();
        using var http = Client(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains(RepoFixture.Key1 + "_1280.webp")) return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
            if (url.Contains(RepoFixture.Key1 + "_640.webp")) return FakeImages.Response(request, "image/webp", "no-store", FakeImages.WebpHeader(640, 360));
            if (url.Contains(RepoFixture.Key1 + "_2048.webp")) return FakeImages.Response(request, "image/webp", ImmutableCache, FakeImages.WebpHeader(100, 100));
            if (url.EndsWith("_og.jpg")) return FakeImages.Response(request, "image/jpeg", ImmutableCache, [0xFF, 0xD8]);
            return FakeImages.Response(request, "image/webp", ImmutableCache, FakeImages.WebpHeader(1280, 720));
        });

        var problems = await VerifyService.RunAsync(photos, "https://img.example.test/", http);

        Assert.Contains(problems, p => p.Key == RepoFixture.Key1 && p.Suffix == "_1280.webp" && p.Message.Contains("HTTP 404"));
        Assert.Contains(problems, p => p.Key == RepoFixture.Key1 && p.Suffix == "_640.webp" && p.Message.Contains("Cache-Control"));
        Assert.Contains(problems, p => p.Message.Contains("寸法が JSON と違います"));
    }
}

public sealed class UploadTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly string _uploadRoot;

    public UploadTests()
    {
        _uploadRoot = Path.Combine(_dir.Path, "upload");
        Directory.CreateDirectory(Path.Combine(_uploadRoot, "inbox", "2026-10"));
    }

    public void Dispose() => _dir.Dispose();

    private string Inbox(string name, bool old = true)
    {
        var path = Path.Combine(_uploadRoot, "inbox", "2026-10", name);
        File.WriteAllBytes(path, TestImages.Png(640, 360, TestImages.XmpAttributes()));
        File.SetLastWriteTimeUtc(path, old ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow);
        return path;
    }

    private string Ledger => Path.Combine(_dir.Path, "content", "catalog", "photos.json");

    [Fact]
    public async Task DryRunChangesNothing()
    {
        Inbox("VRChat_2026-10-23_21-33-12.345_640x360.png");
        var store = new MemoryObjectStore();

        var report = await UploadService.RunAsync(Path.Combine(_uploadRoot, "inbox"), Path.Combine(_uploadRoot, "uploaded"),
            new UploadOptions { Apply = false }, PhotoLedger.Empty(), Ledger, store, DateTimeOffset.UtcNow);

        Assert.Equal(1, report.Count(IngestState.Planned));
        Assert.Equal(0, store.PutCount);
        Assert.False(File.Exists(Ledger));
        Assert.True(File.Exists(Path.Combine(_uploadRoot, "inbox", "2026-10", "VRChat_2026-10-23_21-33-12.345_640x360.png")));
    }

    [Fact]
    public async Task ApplyUploadsRegistersMovesAndSecondRunSkips()
    {
        Inbox("VRChat_2026-10-23_21-33-12.345_640x360.png");
        var store = new MemoryObjectStore();
        var inbox = Path.Combine(_uploadRoot, "inbox");
        var uploaded = Path.Combine(_uploadRoot, "uploaded");

        var first = await UploadService.RunAsync(inbox, uploaded, new UploadOptions { Apply = true }, PhotoLedger.Empty(), Ledger, store, DateTimeOffset.UtcNow);
        var second = await UploadService.RunAsync(inbox, uploaded, new UploadOptions { Apply = true }, PhotoLedger.Load(Ledger), Ledger, store, DateTimeOffset.UtcNow);

        Assert.Equal(1, first.Count(IngestState.Uploaded));
        // 画像 3 件（640 / 1280 / 2048。og は表紙だけ）
        Assert.Equal(3, store.PutCount);
        Assert.Single(first.Items, i => i.MovedTo is not null);
        Assert.True(File.Exists(Path.Combine(uploaded, "2026-10", "VRChat_2026-10-23_21-33-12.345_640x360.png")));
        Assert.True(File.Exists(Ledger));
        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task TestBucketKeepsFilesAndUsesItsOwnLedger()
    {
        Inbox("VRChat_2026-10-23_21-33-12.345_640x360.png");
        var store = new MemoryObjectStore();
        var testLedger = Path.Combine(_dir.Path, "local", "catalog-test.json");

        var report = await UploadService.RunAsync(Path.Combine(_uploadRoot, "inbox"), Path.Combine(_uploadRoot, "uploaded"),
            new UploadOptions { Apply = true, TestMode = true }, PhotoLedger.Empty(), testLedger, store, DateTimeOffset.UtcNow);

        Assert.Equal(1, report.Count(IngestState.Uploaded));
        Assert.Null(report.Items.Single().MovedTo);
        Assert.True(File.Exists(testLedger));
        Assert.False(File.Exists(Ledger));
    }

    [Fact]
    public async Task FilesWrittenInTheLastFiveSecondsAreSkipped()
    {
        Inbox("VRChat_2026-10-23_21-33-12.345_640x360.png", old: false);

        var report = await UploadService.RunAsync(Path.Combine(_uploadRoot, "inbox"), Path.Combine(_uploadRoot, "uploaded"),
            new UploadOptions { Apply = true }, PhotoLedger.Empty(), Ledger, new MemoryObjectStore(), DateTimeOffset.UtcNow);

        Assert.Single(report.Items.Where(i => i.Skipped));
        Assert.Equal(0, report.Count(IngestState.Uploaded));
    }

    [Fact]
    public void PrepareCreatesMonthFoldersOnceAndRejectsBadMonths()
    {
        var root = Path.Combine(_dir.Path, "prep");

        var created = PrepareService.Run(root, "2025-11", "2026-01");
        var again = PrepareService.Run(root, "2025-11", "2026-01");

        Assert.Equal(3 + 2, created.Count);
        Assert.Empty(again);
        Assert.True(Directory.Exists(Path.Combine(root, "inbox", "2025-12")));
        Assert.Throws<ArgumentException>(() => PrepareService.Run(root, "2026/01", "2026-02"));
    }
}

public sealed class ScanAndAuditTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void ScanCountsByMonthAndUsesLedgerStatus()
    {
        var folder = Path.Combine(_dir.Path, "VRChat", "2026-10");
        Directory.CreateDirectory(folder);
        var withXmp = Path.Combine(folder, "VRChat_2026-10-23_21-33-12.345_320x180.png");
        var noXmp = Path.Combine(folder, "VRChat_2026-10-24_10-00-00.000_320x180.png");
        File.WriteAllBytes(withXmp, TestImages.Png(320, 180, TestImages.XmpAttributes()));
        File.WriteAllBytes(noXmp, TestImages.Png(320, 180));
        var ledger = PhotoLedger.Empty();
        ledger.Put(Hashing.Sha256Hex(File.ReadAllBytes(withXmp)), new LedgerEntry { Key = "2026-10/20261023-213312-345_aaaaaaaa", State = LedgerStates.Uploaded });
        var cache = HashCache.Load(Path.Combine(_dir.Path, "local", "cache", "hashes.json"));

        var rows = ScanService.Run(Path.Combine(_dir.Path, "VRChat"), ledger, cache);
        cache.Save();

        var row = Assert.Single(rows);
        Assert.Equal("2026-10", row.Month);
        Assert.Equal(2, row.Total);
        Assert.Equal(1, row.WithXmp);
        Assert.Equal(1, row.Uploaded);
        Assert.Equal(1, row.Unregistered);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "local", "cache", "hashes.json")));
    }

    [Fact]
    public async Task AuditFindsOrphansAndMissingObjects()
    {
        var store = new MemoryObjectStore();
        var kept = "2026-10/20261023-213312-345_aaaaaaaa";      // 記事にあり、表紙で og もある
        var orphan = "2026-10/20261020-100000-000_bbbbbbbb";    // どの記事からも参照されない
        var missing = "2026-10/20261021-100000-000_cccccccc";   // 記事にあるが _2048 が無い
        var coverNoOg = "2026-10/20261022-100000-000_dddddddd"; // 表紙だが _og が無い（欠け）
        var notCover = "2026-10/20261019-100000-000_eeeeeeee";  // 表紙ではなくなったのに _og が残っている（孤児）
        foreach (var suffix in new[] { "_640.webp", "_1280.webp", "_2048.webp", "_og.jpg" }) await store.PutAsync(kept + suffix, [1], "image/webp", "x");
        await store.PutAsync(orphan + "_2048.webp", [1], "image/webp", "x");
        await store.PutAsync(missing + "_640.webp", [1], "image/webp", "x");
        foreach (var suffix in new[] { "_640.webp", "_1280.webp", "_2048.webp" }) await store.PutAsync(coverNoOg + suffix, [1], "image/webp", "x");
        foreach (var suffix in new[] { "_640.webp", "_1280.webp", "_2048.webp", "_og.jpg" }) await store.PutAsync(notCover + suffix, [1], "image/webp", "x");

        var referenced = new HashSet<string> { kept, missing, coverNoOg, notCover };
        var report = await AuditService.RunAsync(store, referenced, new HashSet<string> { kept, coverNoOg }, new HashSet<string> { kept, missing, coverNoOg, notCover });

        Assert.Equal(13, report.ObjectCount);
        Assert.Equal(13, report.CountByMonth["2026-10"]);
        Assert.Equal(2, report.Orphans.Count);
        Assert.Contains(orphan, report.Orphans);
        Assert.Contains(notCover + "_og.jpg", report.Orphans);
        Assert.Equal(2, report.Missing.Count);
        Assert.Contains(missing, report.Missing);
        Assert.Contains(coverNoOg, report.Missing);
    }
}

public sealed class CommandSmokeTests : IDisposable
{
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task HelpAndUnknownCommandExitCleanly()
    {
        Assert.Equal(0, await Program.Main(["help"]));
        Assert.Equal(2, await Program.Main(["no-such-command", "--repo", _fixture.Root]));
        Assert.Equal(2, await Program.Main(["draft", "--repo", _fixture.Root]));
    }

    [Fact]
    public void RepoLayoutMatchesWhatTheCliExpects()
    {
        _fixture.WriteExistingPost();

        var content = ContentLoader.Load(_fixture.ContentDir);

        Assert.Single(content.Posts);
        Assert.Equal("old-post", content.Posts[0].Slug);
    }
}
