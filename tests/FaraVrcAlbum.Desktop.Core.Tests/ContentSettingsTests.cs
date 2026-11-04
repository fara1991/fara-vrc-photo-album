using System.Text.Json.Nodes;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>設定の「記事の中身のフォルダ」「控え用のバケット名」と、コピー・控え・戻す。</summary>
public sealed class ContentSettingsTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose() => _ws.Dispose();

    private string Content => Path.Combine(_ws.Layout.RepoRoot, "content");

    private string WorkDir(string name)
    {
        var dir = Path.Combine(_ws.Layout.RepoRoot, "..", "work-" + Guid.NewGuid().ToString("N")[..6], name);
        return Path.GetFullPath(dir);
    }

    private static SettingRowViewModel Row(SettingsViewModel s, string key) => s.Rows.Single(r => r.Key == key);

    private async Task SaveAsync(SettingsViewModel settings)
    {
        _ws.Dialogs.Answer = true;
        await settings.SaveCommand.RunAsync();
    }

    [Fact]
    public async Task TheBackupBucketCannotBeThePhotoBucketAndNothingIsSavedThen()
    {
        var settings = new SettingsViewModel(_ws.App, _ws.Dialogs);
        Row(settings, "backupBucket").EditText = _ws.Session.ProductionBucket;

        await SaveAsync(settings);

        Assert.Contains("同じにはできません", settings.Status);
        Assert.Equal("", new FaraVrcAlbum.Desktop.Services.AlbumSession(RepoContext.Discover(_ws.Layout.RepoRoot), _ws.Layout.UploadRoot, true).BackupBucket);
    }

    [Fact]
    public async Task TheContentFolderMustExistAndBeReadableAsContent()
    {
        var settings = new SettingsViewModel(_ws.App, _ws.Dialogs);
        Row(settings, "contentDir").EditText = WorkDir("nothing");
        await SaveAsync(settings);
        Assert.Contains("見つかりません", settings.Status);

        var broken = WorkDir("broken");
        Directory.CreateDirectory(Path.Combine(broken, "posts"));
        File.WriteAllText(Path.Combine(broken, "posts", "x.json"), "{ \"schema\": 1, \"slug\": \"x\" }");
        Row(settings, "contentDir").EditText = broken;
        await SaveAsync(settings);
        Assert.Contains("読めません", settings.Status);
        Assert.Null(_ws.Session.Repo.ConfiguredContentDir);
    }

    [Fact]
    public async Task CopyOutCopiesTheContentAndDoesNotChangeTheFolderInUse()
    {
        var dest = WorkDir("outside");
        _ws.Dialogs.Picked = dest;
        _ws.Dialogs.Answer = true;
        var settings = new SettingsViewModel(_ws.App, _ws.Dialogs);

        await settings.CopyOutCommand.RunAsync();

        Assert.True(File.Exists(Path.Combine(dest, "worlds.json")));
        Assert.Null(_ws.Session.Repo.ConfiguredContentDir);                              // 使うフォルダは変わらない
        Assert.Contains("使うフォルダは変えていません", settings.Status);
        Assert.Equal(["記事の中身をコピーします"], _ws.Dialogs.Asked);

        // 選ばなければ何もしない
        _ws.Dialogs.Picked = null;
        _ws.Dialogs.Asked.Clear();
        await settings.CopyOutCommand.RunAsync();
        Assert.Empty(_ws.Dialogs.Asked);
    }

    [Fact]
    public async Task BackupNeedsABucketAndTheConfirmationThenRestoreBringsTheContentBack()
    {
        var settings = new SettingsViewModel(_ws.App, _ws.Dialogs);
        _ws.Dialogs.Answer = true;
        await settings.BackupCommand.RunAsync();
        Assert.Contains("設定されていません", settings.Status);                          // バケット名が未設定のあいだは、控えを作れない

        Row(settings, "backupBucket").EditText = "demo-content-backup";
        await SaveAsync(settings);
        await settings.BackupCommand.RunAsync();
        Assert.Contains("控えを作りました", settings.Status);
        Assert.Contains("最後の控え", settings.BackupStatusText);
        var store = _ws.Session.DemoStore("demo-content-backup");
        Assert.Contains(store.Objects.Keys, k => k.StartsWith("snapshots/", StringComparison.Ordinal));
        Assert.DoesNotContain(_ws.Session.DemoStore(_ws.Session.ProductionBucket).Objects.Keys, k => k.StartsWith("snapshots/", StringComparison.Ordinal));   // 写真のバケットには置かない

        // 記事を直してから、控えから戻す
        var post = Directory.EnumerateFiles(Path.Combine(Content, "posts"), "*.json").First();
        var original = File.ReadAllText(post);
        File.WriteAllText(post, original.Replace("\"title\": \"", "\"title\": \"壊した"));
        await settings.RestoreCommand.RunAsync();

        Assert.Equal(original, File.ReadAllText(post));
        Assert.Contains("戻しました", settings.Status);
        Assert.True(Directory.Exists(Path.Combine(_ws.Layout.RepoRoot, "local", "restore-backups")));    // 戻す前の内容の安全の控え
    }
}
