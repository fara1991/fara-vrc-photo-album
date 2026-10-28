using Microsoft.Data.Sqlite;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>管理（写真の点検・削除できない写真）・BOOTH のカテゴリでの服の絞り込み・バケット名の設定。</summary>
public sealed class ManageBoothSettingsTests : IDisposable
{
    private readonly Workspace _ws = new();

    public void Dispose()
    {
        RepoContext.ConfigureProductionBucket(null);
        _ws.Dispose();
    }

    private string TempDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "work", "booth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>BOOTH Library Manager の data.db と同じ形の、小さなデータベースを作る。</summary>
    private static string BoothDatabase(string dir)
    {
        var path = Path.Combine(dir, "data.db");
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE parent_categories (id INTEGER PRIMARY KEY, name TEXT);
                CREATE TABLE sub_categories (id INTEGER PRIMARY KEY, name TEXT, parent_category_id INTEGER);
                CREATE TABLE shops (subdomain TEXT PRIMARY KEY, name TEXT);
                CREATE TABLE booth_items (id INTEGER PRIMARY KEY, name TEXT, shop_subdomain TEXT, sub_category INTEGER);
                CREATE TABLE overwritten_booth_items (booth_item_id INTEGER PRIMARY KEY, name TEXT);
                INSERT INTO parent_categories VALUES (1, '3Dモデル'), (2, '音楽');
                INSERT INTO sub_categories VALUES (209, '3D衣装', 1), (208, '3Dキャラクター', 1), (33, '音楽一般', 2);
                INSERT INTO shops VALUES ('shop-a', 'ショップA');
                INSERT INTO booth_items VALUES (111, '白いドレス', 'shop-a', 209), (222, 'キャラクター', 'shop-a', 208), (333, '曲', 'shop-a', 33), (444, 'ああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああ', 'shop-a', 209);
                INSERT INTO overwritten_booth_items VALUES (111, '白いドレス（書き換え）');
                """;
            cmd.ExecuteNonQuery();
        }
        return path;
    }

    [Fact]
    public void OnlyThe3DOutfitCategoryOfBoothItemsIsOfferedWithNameShopAndUrl()
    {
        var dir = TempDir();
        var db = BoothDatabase(dir);
        var library = Path.Combine(dir, "library");
        foreach (var id in new[] { "b111", "b222", "b333", "b444" }) Directory.CreateDirectory(Path.Combine(library, id));

        var scan = FolderScan.OutfitCandidates(library, db);

        Assert.Equal(2, scan.Entries.Count);                                             // 3D 衣装だけ（キャラクター・音楽は出さない）
        var dress = scan.Entries.Single(e => e.Name.StartsWith("白いドレス", StringComparison.Ordinal));
        Assert.Equal("白いドレス（書き換え）", dress.Name);                                // 利用者が書き換えた名前を使う
        Assert.Equal("ショップA", dress.Shop);
        Assert.Equal("https://booth.pm/ja/items/111", dress.Url);
        var longName = scan.Entries.Single(e => e.Name.StartsWith("あ", StringComparison.Ordinal));
        Assert.True(longName.Name.EnumerateRunes().Count() <= 64, "表示名の上限（64 文字）に収める");
        Assert.Null(DisplayName.Problem(longName.Name));
    }

    [Fact]
    public void WithoutBoothDataTheFolderNamesAreShownWithANote()
    {
        var dir = TempDir();
        var library = Path.Combine(dir, "library");
        Directory.CreateDirectory(Path.Combine(library, "b111"));

        var scan = FolderScan.OutfitCandidates(library, Path.Combine(dir, "no-such.db"));

        Assert.Equal(["b111"], scan.Entries.Select(e => e.Name));
        Assert.Contains("読めない", scan.Note);
    }

    [Fact]
    public async Task BoothOutfitsAreImportedWithTheirShopAndUrlAndCheckboxesCanBeChecked()
    {
        var dir = TempDir();
        var db = BoothDatabase(dir);
        var library = Path.Combine(dir, "library");
        foreach (var id in new[] { "b111", "b444" }) Directory.CreateDirectory(Path.Combine(library, id));
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        _ws.Session.Preferences.BoothDatabase = db;
        vm.OutfitImport.Folder = library;
        var real = vm.OutfitImport;
        real.Scan();
        var candidate = real.Candidates.First(c => c.Name.StartsWith("白い", StringComparison.Ordinal));
        candidate.IsChecked = true;
        Assert.True(candidate.IsChecked);                                                // チェックできる
        Assert.Equal(1, real.CheckedCount);
        await real.ImportCommand.RunAsync();

        var outfit = Assert.Single(vm.Outfits, o => o.Name == "白いドレス（書き換え）");
        Assert.Equal("ショップA", outfit.Shop);
        Assert.Equal("https://booth.pm/ja/items/111", outfit.Url);
        Assert.True(real.Candidates.First(c => c.Name.StartsWith("白い", StringComparison.Ordinal)).Registered);
    }

    [Fact]
    public async Task PhotoAuditShowsEveryUploadedPhotoAndJudgesWhetherItIsUsed()
    {
        await _ws.SeedProductionAsync();
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);
        manage.LoadAuditRows();
        Assert.Equal(4, manage.AuditPhotos.Count);
        Assert.All(manage.AuditPhotos, p => Assert.Equal("未点検", p.Judgement));

        await manage.AuditCommand.RunAsync();

        Assert.All(manage.AuditPhotos, p => Assert.NotEqual("未点検", p.Judgement));
        Assert.Contains(manage.AuditPhotos, p => p.Judgement.StartsWith("使用中", StringComparison.Ordinal));
        // 記事に入っていない写真だけが「使われていません」になる
        var inPosts = PostService.PhotoKeys(_ws.Session.Editor);
        Assert.All(manage.AuditPhotos, p => Assert.Equal(!inPosts.Contains(p.Key), p.IsUnused));
    }

    [Fact]
    public async Task APhotoUsedAsACoverCannotBeSelectedForRemovalAndTheReasonIsShown()
    {
        await _ws.SeedProductionAsync();
        File.Delete(Path.Combine(_ws.Layout.RepoRoot, "content", "posts", "demo-draft.json"));                  // 非公開の記事を消して、その写真を「どの記事にも入っていない」にする
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);
        await manage.LoadRemovableAsync();
        var coverKey = PostService.CoverKeys(_ws.Session.Editor).First();
        var cover = manage.Removable.Single(r => r.Key == coverKey);
        Assert.False(cover.CanRemove);
        Assert.Contains("表紙", cover.BlockReason);

        manage.SelectedRemovable = cover;

        Assert.Null(manage.SelectedRemovable);                                           // 選べない
        Assert.Contains("選べません", manage.Status);
        Assert.False(manage.RemovePlanCommand.CanExecute(null));
        var other = manage.Removable.First(r => r.CanRemove);
        manage.SelectedRemovable = other;
        Assert.Same(other, manage.SelectedRemovable);
    }

    [Fact]
    public async Task APhotoUsedInAnArticleCannotBeSelectedForRemovalEvenIfItIsNotTheCover()
    {
        await _ws.SeedProductionAsync();
        var manage = new ManageViewModel(_ws.App, _ws.Dialogs);
        await manage.LoadRemovableAsync();
        var covers = PostService.CoverKeys(_ws.Session.Editor);
        var usedNotCover = PostService.PhotoKeys(_ws.Session.Editor).FirstOrDefault(k => !covers.Contains(k));
        Assert.NotNull(usedNotCover);                                                    // デモの記事には、表紙でない写真もある
        var row = manage.Removable.Single(r => r.Key == usedNotCover);
        Assert.False(row.CanRemove);
        Assert.Contains("記事で使われています", row.BlockReason);

        manage.SelectedRemovable = row;

        Assert.Null(manage.SelectedRemovable);
        Assert.Contains("選べません", manage.Status);
    }

    [Fact]
    public async Task TheBucketNameCanBeEditedAndInvalidNamesAreRefused()
    {
        _ws.Dialogs.Answer = true;
        var settings = new SettingsViewModel(_ws.App, _ws.Dialogs);
        var row = settings.Rows.Single(r => r.Key == "bucket");
        Assert.Equal(RepoContext.ProductionBucket, settings.BucketText);

        row.EditText = "Bad Name";
        await settings.SaveCommand.RunAsync();
        Assert.Contains("バケット名は", settings.Status);
        Assert.Equal(RepoContext.ProductionBucket, _ws.Session.ProductionBucket);

        row.EditText = "my-own-photos";
        await settings.SaveCommand.RunAsync();
        Assert.Equal("my-own-photos", _ws.Session.ProductionBucket);
        Assert.Equal("my-own-photos", settings.BucketText);
        Assert.True(RepoContext.IsProductionBucket("my-own-photos"));
    }

    [Fact]
    public async Task SeveralCheckedBoothOutfitsAreAllSaved()
    {
        var dir = TempDir();
        var db = BoothDatabase(dir);
        // 3D 衣装を 3 件に増やす
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString()))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO booth_items VALUES (555, '青いワンピース', 'shop-a', 209), (666, '赤いコート', 'shop-a', 209);";
            cmd.ExecuteNonQuery();
        }
        var library = Path.Combine(dir, "library");
        foreach (var id in new[] { "b111", "b444", "b555", "b666" }) Directory.CreateDirectory(Path.Combine(library, id));
        _ws.Dialogs.Answer = true;
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs);
        vm.Refresh();
        _ws.Session.Preferences.BoothDatabase = db;
        vm.OutfitImport.Folder = library;
        vm.OutfitImport.Scan();
        Assert.Equal(4, vm.OutfitImport.Candidates.Count);

        vm.OutfitImport.AllChecked = true;                                              // 見出しのチェックで全部を選ぶ
        Assert.Equal(4, vm.OutfitImport.CheckedCount);
        await vm.OutfitImport.ImportCommand.RunAsync();

        Assert.Equal(4, vm.Outfits.Count);                                               // 複数選んでも、全部が保存される
        Assert.Equal(4, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(_ws.Layout.RepoRoot, "content", "outfits.json")))!.AsObject().Count);
        Assert.All(vm.OutfitImport.Candidates, c => Assert.True(c.Registered));
    }
}
