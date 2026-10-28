using FaraVrcAlbum.Content;
using FaraVrcAlbum.Desktop.Demo;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>確認ダイアログの代わり。答えを固定し、聞かれた題を記録する。</summary>
internal sealed class FakeDialogs : IDialogService
{
    public bool Answer { get; set; }

    public List<string> Asked { get; } = [];

    /// <summary>終わったことを知らせたダイアログ（題: 本文）。</summary>
    public List<string> Informed { get; } = [];

    public Task InformAsync(string title, string message)
    {
        Informed.Add(title + ": " + message);
        return Task.CompletedTask;
    }

    /// <summary>フォルダを選ぶダイアログの答え（null は、選ばない）。</summary>
    public string? Picked { get; set; }

    public string? PickFolder(string title, string? initial) => Picked;

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        Asked.Add(title);
        return Task.FromResult(Answer);
    }
}

/// <summary>
/// 合成のデモ（一時フォルダ・R2 はメモリ）。実リポジトリ・実バケット・F:\ には触れない。
/// </summary>
internal sealed class Workspace : IDisposable
{
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));

    public Workspace(bool offline = true)
    {
        Layout = DemoWorkspace.Create(_root);
        Session = new AlbumSession(RepoContext.Discover(Layout.RepoRoot), Layout.UploadRoot, offline, Layout.SourceRoot);
        App = new AppState(Session);
        Dialogs = new FakeDialogs();
    }

    public DemoLayout Layout { get; }

    public AlbumSession Session { get; }

    public AppState App { get; }

    public FakeDialogs Dialogs { get; }

    public string ProductionLedgerPath => Session.LedgerPath(Session.ProductionBucket);


    /// <summary>デモの本番 R2（メモリ）に写真を入れる。</summary>
    public async Task SeedProductionAsync()
    {
        await DemoWorkspace.SeedStore(Session.DemoStore(Session.ProductionBucket), PhotoLedger.Load(ProductionLedgerPath));
    }


    /// <summary>アップロード用フォルダのファイル（相対パス・サイズ・更新時刻）。何も書いていないことの確認に使う。</summary>
    public IReadOnlyList<string> UploadSnapshot()
    {
        return Directory.EnumerateFiles(Layout.UploadRoot, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => $"{Path.GetRelativePath(Layout.UploadRoot, p)}|{new FileInfo(p).Length}|{new FileInfo(p).LastWriteTimeUtc.Ticks}")
            .ToList();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
