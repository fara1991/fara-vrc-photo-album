using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>アプリからの公開: 公開済みの判定・記録・本番の前提・トークンの扱い（実際のプログラムは動かさない）。</summary>
public sealed class SitePublishTests : IDisposable
{
    private readonly RepoFixture _fixture = new();
    private readonly TempDir _work = new();

    public void Dispose()
    {
        _fixture.Dispose();
        _work.Dispose();
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 22, 30, 0, TimeSpan.FromHours(9));
    private const string Token = "cf-secret-token-abc123";
    private const string Account = "0123456789abcdef0123456789abcdef";

    private sealed record Call(string File, string[] Args, Dictionary<string, string> Env);

    /// <summary>動かすプログラムを記録し、決めた終了コードと出力を返す偽物。</summary>
    private sealed class FakeRunner(Func<Call, (int Code, string[] Lines)> behavior) : IProcessRunner
    {
        public List<Call> Calls { get; } = [];

        public Task<int> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, IReadOnlyDictionary<string, string> environment, Action<string> onLine, CancellationToken ct)
        {
            var call = new Call(fileName, [.. args], new Dictionary<string, string>(environment));
            Calls.Add(call);
            var (code, lines) = behavior(call);
            foreach (var line in lines) onLine(line);
            return Task.FromResult(code);
        }
    }

    private static FakeRunner Succeeds() => new(c => c.File == "git" ? (0, ["abc1234"]) : (0, ["ok"]));

    private RepoContext RepoWithCloudflare()
    {
        File.WriteAllText(Path.Combine(_fixture.Root, ".env.local"), $"CLOUDFLARE_API_TOKEN={Token}\nR2_ACCOUNT_ID={Account}\n");
        return _fixture.Repo;
    }

    private PublishLog NewLog() => new(_work.File("publish-state.json"));

    // ---------------------------------------------------------------- 公開済みの判定

    [Fact]
    public void TheContentHashFollowsTheContentOfTheFilesAndNothingElse()
    {
        _fixture.WriteExistingPost();
        var first = SitePublishService.ContentHash(_fixture.ContentDir);
        Assert.Equal(first, SitePublishService.ContentHash(_fixture.ContentDir));                       // 同じ中身は同じ値

        File.SetLastWriteTimeUtc(_fixture.PostPath("old-post"), DateTime.UtcNow.AddDays(-3));
        Assert.Equal(first, SitePublishService.ContentHash(_fixture.ContentDir));                       // 更新日時は見ない

        RepoFixture.Write(_fixture.PostPath("old-post"), File.ReadAllText(_fixture.PostPath("old-post")).Replace("既存の記事", "直した題"));
        Assert.NotEqual(first, SitePublishService.ContentHash(_fixture.ContentDir));                    // 中身が変わると変わる
    }

    [Fact]
    public void TheStateIsNeverUpToDateOrChangedAndTheRecordSurvivesAReload()
    {
        var log = NewLog();
        Assert.Equal(PublishState.Never, SitePublishService.StateOf(log.Get(PublishTarget.Develop), "h1"));
        Assert.Equal("まだ公開していません", SitePublishService.Describe(null, "h1"));

        log.Set(PublishTarget.Develop, new PublishRecord("h1", Now, "abc1234"));
        var loaded = new PublishLog(log.Path).Get(PublishTarget.Develop)!;
        Assert.Equal(PublishState.UpToDate, SitePublishService.StateOf(loaded, "h1"));
        Assert.Equal(PublishState.Changed, SitePublishService.StateOf(loaded, "h2"));
        Assert.Equal("abc1234", loaded.Commit);
        Assert.Equal("公開済み（2026-10-27 22:30）", SitePublishService.Describe(loaded, "h1"));
        Assert.Contains("変更があります", SitePublishService.Describe(loaded, "h2"));
        Assert.Null(log.Get(PublishTarget.Production));                                                 // 公開先ごとに別の記録
    }

    [Fact]
    public void ABrokenRecordFileCountsAsNeverPublished()
    {
        File.WriteAllText(_work.File("publish-state.json"), "{ broken");
        Assert.Null(NewLog().Get(PublishTarget.Develop));
        var log = NewLog();
        log.Set(PublishTarget.Develop, new PublishRecord("h1", Now, null));                              // 壊れていても、書き直せる
        Assert.NotNull(log.Get(PublishTarget.Develop));
    }

    // ---------------------------------------------------------------- 公開

    [Fact]
    public async Task PublishingToDevRunsTheDeployWithTheCredentialsOnlyInTheEnvironmentAndRecordsIt()
    {
        _fixture.WriteExistingPost();
        var repo = RepoWithCloudflare();
        var runner = Succeeds();
        var log = NewLog();
        var lines = new List<string>();

        var record = await SitePublishService.PublishAsync(repo, PublishTarget.Develop, runner, log, Now, lines.Add);

        var deploy = Assert.Single(runner.Calls, c => c.File == "node");
        Assert.Equal(["tools/site.mjs", "deploy", "develop"], deploy.Args);
        Assert.Equal(Token, deploy.Env["CLOUDFLARE_API_TOKEN"]);
        Assert.Equal(repo.ContentDir, deploy.Env["ALBUM_CONTENT_DIR"]);
        Assert.All(runner.Calls, c => Assert.DoesNotContain(c.Args, a => a.Contains(Token) || a.Contains(Account)));   // 引数には出さない
        Assert.Equal(SitePublishService.ContentHash(repo.ContentDir), record.ContentHash);
        Assert.Equal("abc1234", record.Commit);
        Assert.Equal(PublishState.UpToDate, SitePublishService.StateOf(log.Get(PublishTarget.Develop), record.ContentHash));
        Assert.Null(log.Get(PublishTarget.Production));
    }

    [Fact]
    public async Task AFailedPublishKeepsTheOldRecordAndNeverShowsTheToken()
    {
        _fixture.WriteExistingPost();
        var repo = RepoWithCloudflare();
        var log = NewLog();
        var old = new PublishRecord("old", Now.AddDays(-1), null);
        log.Set(PublishTarget.Develop, old);
        var runner = new FakeRunner(_ => (1, [$"Authentication error with token {Token}", "wrangler failed"]));
        var lines = new List<string>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SitePublishService.PublishAsync(repo, PublishTarget.Develop, runner, log, Now, lines.Add));

        Assert.Contains("失敗しました", ex.Message);
        Assert.Contains("wrangler failed", ex.Message);
        Assert.DoesNotContain(Token, ex.Message);
        Assert.DoesNotContain(lines, l => l.Contains(Token));
        Assert.Equal("old", log.Get(PublishTarget.Develop)!.ContentHash);                                // 記録は変えない
    }

    [Fact]
    public async Task ProductionNeedsTheCurrentContentPublishedToDevFirst()
    {
        _fixture.WriteExistingPost();
        var repo = RepoWithCloudflare();
        var log = NewLog();
        var runner = Succeeds();

        var never = await Assert.ThrowsAsync<InvalidOperationException>(() => SitePublishService.PublishAsync(repo, PublishTarget.Production, runner, log, Now, _ => { }));
        Assert.Contains("確認用（dev）に公開", never.Message);

        log.Set(PublishTarget.Develop, new PublishRecord("some-older-content", Now, null));
        var changed = await Assert.ThrowsAsync<InvalidOperationException>(() => SitePublishService.PublishAsync(repo, PublishTarget.Production, runner, log, Now, _ => { }));
        Assert.Contains("変わっています", changed.Message);
        Assert.Empty(runner.Calls);                                                                      // どちらも、何も動かさない
    }

    [Fact]
    public async Task ProductionChecksTheMaintenanceModeFirstAndStopsWhenItRefuses()
    {
        _fixture.WriteExistingPost();
        var repo = RepoWithCloudflare();
        var log = NewLog();
        log.Set(PublishTarget.Develop, new PublishRecord(SitePublishService.ContentHash(repo.ContentDir), Now, null));

        var refusing = new FakeRunner(c => c.Args.Any(a => a.EndsWith("require-maintenance.mjs")) ? (1, ["MAINTENANCE_MODE が notice になっていません"]) : (0, []));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SitePublishService.PublishAsync(repo, PublishTarget.Production, refusing, log, Now, _ => { }));
        Assert.Contains("止めました", ex.Message);
        Assert.Contains("notice", ex.Message);
        Assert.DoesNotContain(refusing.Calls, c => c.Args.Contains("deploy"));
        Assert.Null(log.Get(PublishTarget.Production));

        var passing = Succeeds();
        await SitePublishService.PublishAsync(repo, PublishTarget.Production, passing, log, Now, _ => { });
        Assert.Equal(["require-maintenance.mjs", "deploy"], passing.Calls.Where(c => c.File == "node").Select(c => c.Args.Contains("deploy") ? "deploy" : Path.GetFileName(c.Args[0])));
        Assert.Contains(passing.Calls, c => c.Args.SequenceEqual(["tools/site.mjs", "deploy", "production"]));
        Assert.NotNull(log.Get(PublishTarget.Production));
    }

    [Fact]
    public async Task PublishingNeedsTheCloudflareSettingsAndSomethingToPublish()
    {
        if (RepoContext.HasProcessEnv("CLOUDFLARE_API_TOKEN") || RepoContext.HasProcessEnv("R2_ACCOUNT_ID")) return;   // この PC の環境変数が優先されるので確かめられない
        _fixture.WriteExistingPost();
        var runner = Succeeds();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SitePublishService.PublishAsync(_fixture.Repo, PublishTarget.Develop, runner, NewLog(), Now, _ => { }));
        Assert.Contains("足りません", ex.Message);
        Assert.Contains("API トークン", ex.Message);
        Assert.Empty(runner.Calls);

        var empty = Directory.CreateDirectory(_work.File("empty")).FullName;
        LocalSettings.SetLocalConfig(_fixture.Repo.LocalConfigPath, new Dictionary<string, string?> { ["contentDir"] = empty });
        var none = await Assert.ThrowsAsync<InvalidOperationException>(() => SitePublishService.PublishAsync(_fixture.Repo, PublishTarget.Develop, runner, NewLog(), Now, _ => { }));
        Assert.Contains("記事の中身がありません", none.Message);
    }

    [Fact]
    public void CloudflareValuesAreRedactedLikeTheR2Ones()
    {
        var repo = RepoWithCloudflare();
        var redacted = Secrets.Redact($"token={Token} account={Account}", repo);
        Assert.DoesNotContain(Token, redacted);
        Assert.DoesNotContain(Account, redacted);
    }
}
