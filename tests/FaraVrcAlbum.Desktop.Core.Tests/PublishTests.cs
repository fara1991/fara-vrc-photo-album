using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>公開の画面: 公開済みの表示・確認ダイアログ・本番の前提（実際のプログラムは動かさない）。</summary>
public sealed class PublishTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private sealed class FakeRunner : IProcessRunner
    {
        public List<string> Commands { get; } = [];

        public Task<int> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, IReadOnlyDictionary<string, string> environment, Action<string> onLine, CancellationToken ct)
        {
            Commands.Add(fileName + " " + string.Join(' ', args));
            onLine(fileName == "git" ? "abc1234" : "ok");
            return Task.FromResult(0);
        }
    }

    /// <summary>Cloudflare の設定があり、記事が 1 つある、実接続の（デモでない）セッション。</summary>
    private PublishViewModel Create(FakeRunner runner, bool offline = false, Func<bool, Action<string>, CancellationToken, Task>? worldFetch = null, bool withWorldNeedingFetch = false)
    {
        var root = _ws.Layout.RepoRoot;
        File.WriteAllText(Path.Combine(root, ".env.local"), "CLOUDFLARE_API_TOKEN=cf-test-token\nR2_ACCOUNT_ID=0123456789abcdef0123456789abcdef\n");
        var posts = Path.Combine(root, "content", "posts");
        Directory.CreateDirectory(posts);
        if (!Directory.EnumerateFiles(Path.Combine(root, "content"), "*.json", SearchOption.AllDirectories).Any())
            File.WriteAllText(Path.Combine(root, "content", "tags.json"), "{ }");
        if (withWorldNeedingFetch)
            File.WriteAllText(Path.Combine(root, "content", "worlds.json"), """{ "wrld_00000000-0000-4000-8000-000000000001": { "name": "一つ目", "aliases": [] } }""");
        var session = new AlbumSession(RepoContext.Discover(root), _ws.Layout.UploadRoot, offline);
        return new PublishViewModel(new AppState(session), _ws.Dialogs, runner, worldFetch ?? ((_, _, _) => Task.CompletedTask));   // VRChat には繋がない
    }

    [Fact]
    public async Task TheDemoNeverPublishes()
    {
        var runner = new FakeRunner();
        var vm = Create(runner, offline: true);
        _ws.Dialogs.Answer = true;

        await vm.PublishDevCommand.RunAsync();

        Assert.Contains("デモでは公開できません", vm.Status);
        Assert.Empty(runner.Commands);
    }

    [Fact]
    public async Task EachTargetShowsWhetherItIsPublishedAndProductionWaitsForDev()
    {
        var runner = new FakeRunner();
        var vm = Create(runner);
        Assert.Equal("まだ公開していません", vm.DevText);
        Assert.Equal("まだ公開していません", vm.ProductionText);
        Assert.False(vm.PublishProductionCommand.CanExecute(null));                                     // dev に公開するまで、本番は押せない
        Assert.Contains("先に今の内容を確認用（dev）へ公開", vm.ProductionHint);

        _ws.Dialogs.Answer = false;
        await vm.PublishDevCommand.RunAsync();
        Assert.Empty(runner.Commands);                                                                  // 確認で「いいえ」なら何もしない
        Assert.Equal("まだ公開していません", vm.DevText);

        _ws.Dialogs.Answer = true;
        await vm.PublishDevCommand.RunAsync();
        Assert.Contains(runner.Commands, c => c == "node tools/site.mjs deploy develop");
        Assert.StartsWith("公開済み（", vm.DevText);
        Assert.Contains("abc1234", vm.DevText);
        Assert.Equal("まだ公開していません", vm.ProductionText);
        Assert.True(vm.PublishProductionCommand.CanExecute(null));
        Assert.Contains("確認用（dev）へ公開しました", vm.Status);

        await vm.PublishProductionCommand.RunAsync();
        Assert.Contains(runner.Commands, c => c == "node tools/site.mjs deploy production");
        Assert.StartsWith("公開済み（", vm.ProductionText);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("本番へ公開"));
    }

    [Fact]
    public async Task ChangingTheContentAfterPublishingShowsItAndLocksProductionAgain()
    {
        var vm = Create(new FakeRunner());
        _ws.Dialogs.Answer = true;
        await vm.PublishDevCommand.RunAsync();
        Assert.True(vm.PublishProductionCommand.CanExecute(null));

        File.WriteAllText(Path.Combine(_ws.Layout.RepoRoot, "content", "changed-after-publish.json"), "{ }");
        vm.Refresh();

        Assert.Contains("変更があります", vm.DevText, StringComparison.Ordinal);
        Assert.False(vm.PublishProductionCommand.CanExecute(null));
    }

    [Fact]
    public async Task PublishingNeverFetchesWorldResourcesNotForDevAndNotForProduction()
    {
        var fetched = 0;
        var vm = Create(new FakeRunner(), worldFetch: (_, _, _) => { fetched++; return Task.CompletedTask; }, withWorldNeedingFetch: true);
        _ws.Dialogs.Answer = true;

        await vm.PublishDevCommand.RunAsync();
        await vm.PublishProductionCommand.RunAsync();

        Assert.Equal(0, fetched);                                                                       // 画像・情報の取得は、公開とは別のボタン（事前に行う）
        Assert.StartsWith("公開済み（", vm.DevText);
        Assert.StartsWith("公開済み（", vm.ProductionText);
    }

    [Fact]
    public async Task TheFetchButtonShowsWhatIsMissingAsksFirstAndThenFetchesWithTheRetryChoice()
    {
        var calls = new List<bool>();
        var vm = Create(new FakeRunner(), worldFetch: (retry, log, _) => { calls.Add(retry); log("取りました"); return Task.CompletedTask; }, withWorldNeedingFetch: true);
        Assert.Contains("画像のないワールド 1 件", vm.WorldsText);
        Assert.Contains("情報（訪問数・説明など）のないワールド 1 件", vm.WorldsText);
        Assert.Contains("約 2 分", vm.WorldsText);                                                       // 問い合わせ 1 回 + 画像 1 回

        _ws.Dialogs.Answer = false;
        await vm.FetchWorldsCommand.RunAsync();
        Assert.Empty(calls);                                                                            // 確認で「いいえ」なら取らない

        _ws.Dialogs.Answer = true;
        vm.RetryFailed = true;
        await vm.FetchWorldsCommand.RunAsync();
        Assert.Equal([true], calls);
        Assert.Contains("取りました", vm.Log);
        Assert.Contains("更新が終わりました", vm.Status);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("サイトの画像・情報を更新"));
        Assert.Contains(_ws.Dialogs.Informed, i => i.StartsWith("更新が終わりました", StringComparison.Ordinal));        // 終わったら、ダイアログで知らせる
    }

    [Fact]
    public async Task TheDemoDoesNotFetchFromVrchat()
    {
        var calls = 0;
        var vm = Create(new FakeRunner(), offline: true, worldFetch: (_, _, _) => { calls++; return Task.CompletedTask; });
        _ws.Dialogs.Answer = true;

        Assert.False(vm.FetchWorldsCommand.CanExecute(null));
        Assert.Contains("デモ", vm.WorldsText);
        Assert.Equal(0, calls);
        await Task.CompletedTask;
    }
}
