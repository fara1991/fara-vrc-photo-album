using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>設定画面の編集: 秘密は押している間だけ見える・保存は確認の後だけ・ほかの行やコメントは保つ。</summary>
public sealed class SettingsTests : IDisposable
{
    private const string Secret = "FakeSecretValueXYZ0123456789abcdefABCDEF";
    private readonly Workspace _ws = new();

    public SettingsTests()
    {
        File.WriteAllText(EnvPath, $"# R2 の設定\nR2_ACCOUNT_ID=acct-fake-0123456789\nR2_ACCESS_KEY_ID=AKIAFAKEFAKEFAKE0000\nR2_SECRET_ACCESS_KEY={Secret}\nOTHER=keep\n");
    }

    public void Dispose() => _ws.Dispose();

    private string EnvPath => Path.Combine(_ws.Layout.RepoRoot, ".env.local");

    private SettingsViewModel Settings()
    {
        var session = new AlbumSession(RepoContext.Discover(_ws.Layout.RepoRoot), _ws.Layout.UploadRoot, offline: false, sourceRoot: _ws.Layout.SourceRoot);
        return new SettingsViewModel(new AppState(session), _ws.Dialogs);
    }

    private static SettingRowViewModel Row(SettingsViewModel s, string key) => s.Rows.Single(r => r.Key == key);

    [Fact]
    public void ASecretIsMaskedAndShownOnlyWhileRevealed()
    {
        var row = Row(Settings(), "R2_SECRET_ACCESS_KEY");
        Assert.True(row.IsSecret);
        Assert.Equal(SettingRowViewModel.Mask, row.DisplayText);
        Assert.Equal("", row.EditText);                                                // 入力欄にも値を入れない

        row.IsRevealed = true;
        Assert.Equal(Secret, row.DisplayText);
        row.IsRevealed = false;
        Assert.Equal(SettingRowViewModel.Mask, row.DisplayText);
    }

    [Fact]
    public void NonSecretRowsShowTheirValueAndCannotBeRevealed()
    {
        File.Delete(Path.Combine(_ws.Layout.RepoRoot, "local", "album.local.json"));     // フォルダを設定していない状態
        var row = Row(Settings(), "contentDir");
        Assert.False(row.IsSecret);
        Assert.EndsWith("（既定）", row.DisplayText);                                     // 空なら、実際に使う既定のフォルダを出す
        row.IsRevealed = true;
        Assert.False(row.IsRevealed);
    }

    [Fact]
    public void TheLabelsAreUserFacingAndTheBucketNameIsEditable()
    {
        var settings = Settings();
        Assert.Equal(["Cloudflare R2 のアカウント ID", "Cloudflare R2 のアクセスキー ID", "Cloudflare R2 のシークレットアクセスキー", "Cloudflare の API トークン（サイトの公開用）", "Cloudflare R2 のバケット名", "アバターのサムネイル画像のフォルダ", "記事の中身（content）のフォルダ", "記事の中身の控え用のバケット名"],
            settings.Rows.Select(r => r.Label));
        Assert.Equal(RepoContext.ProductionBucket, settings.BucketText);
        Assert.DoesNotContain(settings.Rows, r => r.Key == "R2_BUCKET");
        // 説明にファイル名やリポジトリを出さない
        foreach (var text in settings.Rows.Select(r => r.Note).Append(settings.ConnectionText))
            foreach (var word in new[] { ".env.local", "album.local.json", "リポジトリ" })
                Assert.DoesNotContain(word, text);
        // 「ピクチャ」フォルダの名前は PC の設定による（Pictures とは限らない）ので、その PC のピクチャの下にあることを確かめる
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), RepoContext.DefaultUploadRoot);
        Assert.EndsWith("VRChatAlbum", RepoContext.DefaultUploadRoot);
        Assert.EndsWith("VRChat", RepoContext.DefaultSourceRoot);
    }

    [Fact]
    public async Task SavingWritesOnlyAfterConfirmationKeepsOtherLinesAndNeverPutsTheSecretInTheDialog()
    {
        var settings = Settings();
        Assert.False(settings.SaveCommand.CanExecute(null));
        Row(settings, "R2_SECRET_ACCESS_KEY").SetNewValue("NewSecretValue000");
        Row(settings, "avatarThumbnailDir").EditText = @"D:\album-thumbs";
        Assert.True(settings.SaveCommand.CanExecute(null));

        _ws.Dialogs.Answer = false;
        await settings.SaveCommand.RunAsync();
        Assert.Contains(Secret, File.ReadAllText(EnvPath));                            // 断ったら書かない

        _ws.Dialogs.Answer = true;
        await settings.SaveCommand.RunAsync();
        var env = File.ReadAllText(EnvPath);
        Assert.Contains("R2_SECRET_ACCESS_KEY=NewSecretValue000\n", env);
        Assert.DoesNotContain(Secret, env);
        Assert.Contains("# R2 の設定\n", env);                                          // コメントとほかの行は保つ
        Assert.Contains("OTHER=keep\n", env);
        Assert.Contains(@"D:\\album-thumbs", File.ReadAllText(Path.Combine(_ws.Layout.RepoRoot, "local", "album.local.json")));
        Assert.DoesNotContain(settings.Rows, r => r.IsChanged);                        // 読み直した
        Row(settings, "R2_SECRET_ACCESS_KEY").IsRevealed = true;
        Assert.Equal("NewSecretValue000", Row(settings, "R2_SECRET_ACCESS_KEY").DisplayText);   // 保存後に読み直した値
        Assert.DoesNotContain("NewSecretValue000", settings.Status);
    }

    [Fact]
    public async Task ClearingASecretRemovesTheLine()
    {
        var settings = Settings();
        Row(settings, "R2_ACCOUNT_ID").Clear();
        _ws.Dialogs.Answer = true;
        await settings.SaveCommand.RunAsync();

        Assert.DoesNotContain("R2_ACCOUNT_ID", File.ReadAllText(EnvPath));
        Assert.Equal("未設定", Row(settings, "R2_ACCOUNT_ID").DisplayText);
    }

    [Fact]
    public void SharedMessagesAreReworded()
    {
        Assert.Equal("保存先にあるため、登録済み", UserText.Plain("R2 に在るため、登録済み"));
        Assert.Equal("写真の記録で削除済み（k）", UserText.Plain("写真の記録で削除済み（k）"));
        Assert.Equal("写真の記録で削除済み（k）", UserText.Plain("台帳で削除済み（k）。再登録するには --force を付ける"));
        Assert.DoesNotContain("R2", UserText.Plain("R2 と台帳には登録済みですが、uploaded フォルダへ移せません"));
    }
}
