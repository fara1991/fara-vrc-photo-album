using System.Text.Json.Nodes;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>
/// 「フォルダから服を登録」ウィンドウの「インポート」: 未登録の服は登録して BOOTH の画像・情報も取り込み、
/// 登録済みの服には BOOTH の画像・情報を足す（差分）か、置き換える（再インポート）。
/// </summary>
public sealed class OutfitFolderImportTests : IDisposable
{
    private readonly Workspace _ws = new(offline: false);
    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "work", "outfit-folder-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<long, AvatarOutfitService.BoothInfo> _booth = new();

    public OutfitFolderImportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        _ws.Dispose();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string OutfitsFile => Path.Combine(_ws.Layout.RepoRoot, "content", "outfits.json");

    private AvatarOutfitViewModel Create(bool withBooth = true, Func<string, ImportScan>? scan = null)
    {
        var vm = new AvatarOutfitViewModel(_ws.App, _ws.Dialogs, () => withBooth ? _booth : new Dictionary<long, AvatarOutfitService.BoothInfo>(), scan);
        vm.Refresh();
        vm.OutfitImport.Folder = _folder;
        return vm;
    }

    private JsonNode Outfit(string id) => JsonNode.Parse(File.ReadAllText(OutfitsFile))![id]!;

    private static AvatarOutfitService.BoothInfo Info(string image = "https://booth.pximg.net/new.jpg", string description = "BOOTH の説明") =>
        new(image, description, ["タグ"], "2026-01-01T00:00:00+09:00", "2026-02-02T00:00:00+09:00", false);

    /// <summary>登録済みの服（BOOTH の URL つき）を 1 つ作り、同じ名前のフォルダを置く。</summary>
    private void RegisterOutfit(string id, string name, long boothId, string? extraJson = null)
    {
        var existing = File.Exists(OutfitsFile) ? JsonNode.Parse(File.ReadAllText(OutfitsFile))!.AsObject() : new JsonObject();
        var entry = new JsonObject { ["name"] = name, ["url"] = $"https://booth.pm/ja/items/{boothId}" };
        if (extraJson is not null) foreach (var (k, v) in JsonNode.Parse(extraJson)!.AsObject()) entry[k] = v?.DeepClone();
        existing[id] = entry;
        File.WriteAllText(OutfitsFile, existing.ToJsonString());
        Directory.CreateDirectory(Path.Combine(_folder, name));
    }

    [Fact]
    public async Task RegisteringANewOutfitAlsoImportsItsBoothImageAndInfoWhenTheBoothDataCanBeRead()
    {
        // フォルダの一覧（BOOTH Library Manager）から、商品名・お店・商品ページの URL つきで候補が出る
        _booth[222] = Info();
        var vm = Create(scan: _ => new ImportScan([new ImportEntry("新しい服", "店 A", "店 A", "https://booth.pm/ja/items/222")], ""));
        vm.OutfitImport.Scan();
        var candidate = vm.OutfitImport.Candidates.Single(c => c.Name == "新しい服");
        Assert.False(candidate.Registered);
        candidate.IsChecked = true;

        _ws.Dialogs.Answer = true;
        await vm.OutfitImport.ImportCommand.RunAsync();

        var outfits = JsonNode.Parse(File.ReadAllText(OutfitsFile))!.AsObject();
        var added = outfits.Single(o => (string?)o.Value!["name"] == "新しい服").Value!;                          // 登録された
        Assert.Equal("店 A", (string?)added["shop"]);
        Assert.Equal("https://booth.pximg.net/new.jpg", (string?)added["thumbnail"]);                            // 画像・説明・タグ・日付も、あわせて入る
        Assert.Equal("BOOTH の説明", (string?)added["description"]);
        Assert.Equal("2026-02-02T00:00:00+09:00", (string?)added["updatedAt"]);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("服をまとめて登録"));
        Assert.Contains("BOOTH の画像・情報を 1 件", vm.Status);
    }

    [Fact]
    public async Task ARegisteredOutfitCanBeCheckedAndImportAddsOnlyWhatIsMissing()
    {
        RegisterOutfit("1", "登録済みの服", 111, """{ "description": "手で直した説明" }""");
        _booth[111] = Info();
        var vm = Create();
        vm.OutfitImport.Scan();
        var candidate = vm.OutfitImport.Candidates.Single(c => c.Name == "登録済みの服");
        Assert.True(candidate.Registered);
        Assert.True(candidate.CanCheck);                                                                         // 登録済みでも選べる
        Assert.True(vm.OutfitImport.RegisteredViaImport);
        candidate.IsChecked = true;
        Assert.True(vm.OutfitImport.ImportCommand.CanExecute(null));

        _ws.Dialogs.Answer = true;
        await vm.OutfitImport.ImportCommand.RunAsync();

        var outfit = Outfit("1");
        Assert.Equal("https://booth.pximg.net/new.jpg", (string?)outfit["thumbnail"]);                          // 無かった画像は足す
        Assert.Equal("手で直した説明", (string?)outfit["description"]);                                           // すでにある説明は変えない（差分）
        Assert.Equal("タグ", (string?)outfit["tags"]![0]);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("差分インポート"));
        Assert.Contains("足し", vm.Status);
    }

    [Fact]
    public async Task TheOverwriteChoiceReimportsEverythingFromBooth()
    {
        RegisterOutfit("1", "登録済みの服", 111, """{ "thumbnail": "https://booth.pximg.net/old.jpg", "description": "古い説明" }""");
        _booth[111] = Info(description: "BOOTH の今の説明");
        var vm = Create();
        vm.OutfitImport.Scan();
        vm.OutfitImport.Candidates.Single(c => c.Name == "登録済みの服").IsChecked = true;
        vm.OutfitImport.LinkOverwrite = true;                                                                    // 「すでに画像・情報があるものも上書きする」

        _ws.Dialogs.Answer = true;
        await vm.OutfitImport.ImportCommand.RunAsync();

        var outfit = Outfit("1");
        Assert.Equal("https://booth.pximg.net/new.jpg", (string?)outfit["thumbnail"]);
        Assert.Equal("BOOTH の今の説明", (string?)outfit["description"]);
        Assert.Contains(_ws.Dialogs.Asked, a => a.Contains("再インポート"));
        Assert.Contains("上書き", vm.Status);
    }

    [Fact]
    public async Task OnlyTheCheckedRegisteredOutfitsAreTouchedAndDecliningChangesNothing()
    {
        RegisterOutfit("1", "服 A", 111);
        RegisterOutfit("2", "服 B", 111);
        _booth[111] = Info();
        var vm = Create();
        vm.OutfitImport.Scan();
        vm.OutfitImport.Candidates.Single(c => c.Name == "服 B").IsChecked = true;

        _ws.Dialogs.Answer = false;
        await vm.OutfitImport.ImportCommand.RunAsync();
        Assert.Null(Outfit("2")["thumbnail"]);                                                                   // 断ったら変えない

        _ws.Dialogs.Answer = true;
        await vm.OutfitImport.ImportCommand.RunAsync();
        Assert.Null(Outfit("1")["thumbnail"]);                                                                   // 選ばなかった服は触らない
        Assert.NotNull(Outfit("2")["thumbnail"]);
    }

    [Fact]
    public async Task WithoutReadableBoothDataTheReimportExplainsAndChangesNothing()
    {
        RegisterOutfit("1", "登録済みの服", 111);
        var vm = Create(withBooth: false);
        vm.OutfitImport.Scan();
        vm.OutfitImport.Candidates.Single().IsChecked = true;

        _ws.Dialogs.Answer = true;
        await vm.OutfitImport.ImportCommand.RunAsync();

        Assert.Null(Outfit("1")["thumbnail"]);
        Assert.Contains("BOOTH Library Manager のデータを読めませんでした", vm.OutfitImport.Notice);
        Assert.DoesNotContain(_ws.Dialogs.Asked, a => a.Contains("インポート"));                                  // 取り込めないので、確認も出さない
    }

    [Fact]
    public void TheAvatarImportStillHasItsSeparateButtonForRegisteredAvatars()
    {
        var vm = Create();

        Assert.False(vm.AvatarImport.RegisteredViaImport);                                                       // アバターは、これまでどおり別のボタン
        Assert.True(vm.AvatarImport.HasRegisteredAction);
    }
}
