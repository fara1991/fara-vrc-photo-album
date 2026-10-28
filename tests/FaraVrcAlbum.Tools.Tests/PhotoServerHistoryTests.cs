using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>fara-vrc-photo-server の履歴ファイルの読み取りと、アバター名のマスタへの当てはめ。</summary>
public sealed class PhotoServerHistoryTests : IDisposable
{
    private readonly TempDir _work = new();

    public void Dispose() => _work.Dispose();

    private static readonly DateTimeOffset Taken = new(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(9));

    private string Write(params string[] lines)
    {
        var path = _work.File("history.jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    [Fact]
    public void TheAvatarAtAPhotoTimeIsTheLatestSwitchBeforeItAndBrokenOrOtherLinesAreSkipped()
    {
        var path = Write(
            """{"t":"2026-10-28T11:00:00+09:00","type":"avatar","avatarName":"Earlier"}""",
            """{"t":"2026-10-28T11:30:00+09:00","type":"world","worldId":"wrld_66667777-8888-4999-8aaa-bbbbbbbbbbbb","worldName":"W"}""",
            "これは壊れた行",
            """{"t":"2026-10-28T11:50:00+09:00","type":"avatar","avatarName":"Fara AlmarianDress"}""",
            """{"t":"2026-10-28T11:50:00+09:00","type":"avatar","avatarName":"Fara AlmarianDress","avatarId":"avtr_00000000-0000-4000-8000-000000000003"}""",
            """{"t":"2026-10-28T12:30:00+09:00","type":"avatar","avatarName":"Later"}""");

        var history = PhotoServerHistory.Load(path);

        Assert.Equal(4, history.Count);
        var found = history.AvatarAt(Taken)!.Value;
        Assert.Equal("Fara AlmarianDress", found.Name);
        Assert.Equal("avtr_00000000-0000-4000-8000-000000000003", found.Id);                              // 同じ時刻なら、ID があるほう
        Assert.Null(history.AvatarAt(Taken.AddHours(-30)));                                               // それ以前の切り替えが無い
        Assert.Null(history.AvatarAt(Taken.AddDays(5)));                                                  // 24 時間より前の切り替えは使わない
    }

    [Fact]
    public void AMissingOrEmptyHistoryGivesNothing()
    {
        Assert.Equal(0, PhotoServerHistory.Load(_work.File("none.jsonl")).Count);
        Assert.Equal(0, PhotoServerHistory.Load(null).Count);
        Assert.Null(PhotoServerHistory.Empty.AvatarAt(Taken));
    }

    [Fact]
    public void AvatarNamesAreMatchedIgnoringSpacesAndCaseAndNeverGuessWhenAmbiguous()
    {
        var avatars = new Dictionary<string, AvatarMasterEntry>
        {
            ["1"] = new() { Name = "FaraAlmarianDress" },
            ["2"] = new() { Name = "FaraCasualGothic", Aliases = ["Fara Casual Gothic v2"] },
            ["3"] = new() { Name = "Twin" },
            ["4"] = new() { Name = "TWIN" },
        };

        Assert.Equal("1", AvatarMatcher.Match("Fara AlmarianDress", avatars));
        Assert.Equal("1", AvatarMatcher.Match("  fara almarian  DRESS ", avatars));
        Assert.Equal("2", AvatarMatcher.Match("FaraCasualGothicV2", avatars));                            // 別名
        Assert.Null(AvatarMatcher.Match("Twin", avatars));                                                // 2 つに合うときは、選ばない
        Assert.Null(AvatarMatcher.Match("Unknown", avatars));
        Assert.Null(AvatarMatcher.Match("", avatars));
        Assert.Null(AvatarMatcher.Match(null, avatars));
    }

    [Fact]
    public void BoothInfoIsFilledIntoOutfitsFromTheirBoothUrlsAndNeverForAdultItemsOrOtherFields()
    {
        var content = Directory.CreateDirectory(_work.File("content")).FullName;
        File.WriteAllText(Path.Combine(content, "outfits.json"), """
            {
              "a": { "name": "服 A", "shop": "店 A", "url": "https://booth.pm/ja/items/111", "note": "メモ" },
              "b": { "name": "服 B", "url": "https://booth.pm/ja/items/222" },
              "c": { "name": "服 C", "url": "https://booth.pm/ja/items/333" },
              "d": { "name": "服 D", "url": "https://example.test/shop" },
              "e": { "name": "服 E", "url": "https://booth.pm/ja/items/444", "thumbnail": "https://booth.pximg.net/old.jpg" }
            }
            """);
        var editor = new FaraVrcAlbum.Tools.Services.ContentEditor(content);
        var info = new Dictionary<long, FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothInfo>
        {
            [111] = new("https://booth.pximg.net/a.jpg", "説明 A", ["タグ1", "タグ2"], "2025-11-20T10:00:00+09:00", "2025-12-01T10:00:00+09:00", false),
            [222] = new("https://booth.pximg.net/b.jpg", "R18 の説明", [], null, null, true),
            [444] = new("https://booth.pximg.net/new.jpg", "E", [], null, null, false),
        };
        var now = new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(9));

        var preview = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: false, id => info.GetValueOrDefault(id), now);
        Assert.Equal(2, preview.Updated);
        Assert.DoesNotContain("thumbnail\": \"https://booth.pximg.net/a.jpg", File.ReadAllText(Path.Combine(content, "outfits.json")));   // プレビューは書かない

        var result = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: true, id => info.GetValueOrDefault(id), now);

        Assert.Equal(new FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothFillResult(Updated: 2, AlreadyFilled: 0, NoBoothUrl: 1, NotInLibrary: 1, Adult: 1), result);   // a（全部足す）と e（画像はそのまま・説明だけ足す）
        var text = File.ReadAllText(Path.Combine(content, "outfits.json"));
        var a = System.Text.Json.Nodes.JsonNode.Parse(text)!["a"]!;
        Assert.Equal("https://booth.pximg.net/a.jpg", (string?)a["thumbnail"]);
        Assert.Equal("説明 A", (string?)a["description"]);
        Assert.Equal("タグ2", (string?)a["tags"]![1]);
        Assert.Equal("2026-10-28T12:00:00+09:00", (string?)a["fetchedAt"]);
        Assert.Equal(("服 A", "店 A", "メモ"), ((string?)a["name"], (string?)a["shop"], (string?)a["note"]));          // 名前・お店・備考は変えない
        Assert.Null(System.Text.Json.Nodes.JsonNode.Parse(text)!["b"]!["thumbnail"]);                                    // 成人向けは取り込まない
        Assert.Equal("https://booth.pximg.net/old.jpg", (string?)System.Text.Json.Nodes.JsonNode.Parse(text)!["e"]!["thumbnail"]);   // 差分では、すでにある画像は変えない
        Assert.Equal("E", (string?)System.Text.Json.Nodes.JsonNode.Parse(text)!["e"]!["description"]);                         // まだ無かった説明は足す
        Assert.Equal(111L, FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothItemIdOf("https://booth.pm/ja/items/111"));
        Assert.Null(FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothItemIdOf("https://example.test/"));
    }

    private (FaraVrcAlbum.Tools.Services.ContentEditor Editor, string File) BoothOutfits(string json)
    {
        var content = Directory.CreateDirectory(_work.File("content-" + Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(content, "outfits.json"), json);
        return (new FaraVrcAlbum.Tools.Services.ContentEditor(content), Path.Combine(content, "outfits.json"));
    }

    private static readonly FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothInfo NewInfo =
        new("https://booth.pximg.net/new.jpg", "新しい説明", ["新タグ"], "2026-01-01T00:00:00+09:00", "2026-02-02T00:00:00+09:00", false);

    private static readonly DateTimeOffset FillNow = new(2026, 10, 28, 12, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void ADifferentialFillAddsOnlyWhatIsMissingAndNeverChangesWhatIsThere()
    {
        var (editor, file) = BoothOutfits("""
            { "a": { "name": "服 A", "url": "https://booth.pm/ja/items/111", "thumbnail": "https://booth.pximg.net/old.jpg", "description": "手で直した説明", "tags": ["旧タグ"] } }
            """);

        var result = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: true, _ => NewInfo, FillNow);

        Assert.Equal(1, result.Updated);
        var a = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!["a"]!;
        Assert.Equal("https://booth.pximg.net/old.jpg", (string?)a["thumbnail"]);                               // すでにあるものは変えない
        Assert.Equal("手で直した説明", (string?)a["description"]);
        Assert.Equal("旧タグ", (string?)a["tags"]![0]);
        Assert.Equal("2026-01-01T00:00:00+09:00", (string?)a["publishedAt"]);                                   // 無かった日付は足す
        Assert.Equal("2026-02-02T00:00:00+09:00", (string?)a["updatedAt"]);

        var again = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: true, _ => NewInfo, FillNow);
        Assert.Equal((0, 1), (again.Updated, again.AlreadyFilled));                                              // もう足すものは無い
    }

    [Fact]
    public void AnOverwriteFillReimportsEverythingFromBooth()
    {
        var (editor, file) = BoothOutfits("""
            { "a": { "name": "服 A", "url": "https://booth.pm/ja/items/111", "thumbnail": "https://booth.pximg.net/old.jpg", "description": "古い説明", "tags": ["旧タグ"], "note": "メモ" } }
            """);

        var result = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: true, _ => NewInfo, FillNow, overwrite: true);

        Assert.Equal(1, result.Updated);
        var a = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!["a"]!;
        Assert.Equal("https://booth.pximg.net/new.jpg", (string?)a["thumbnail"]);
        Assert.Equal("新しい説明", (string?)a["description"]);
        Assert.Equal("新タグ", (string?)a["tags"]![0]);
        Assert.Equal("メモ", (string?)a["note"]);                                                               // 名前・お店・URL・備考は変えない
        var same = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: true, _ => NewInfo, FillNow, overwrite: true);
        Assert.Equal((0, 1), (same.Updated, same.AlreadyFilled));                                                // BOOTH の内容と同じなら、変更なし
    }

    [Fact]
    public void OnlyTheChosenOutfitsAreFilledWhenIdsAreGiven()
    {
        var (editor, file) = BoothOutfits("""
            { "a": { "name": "服 A", "url": "https://booth.pm/ja/items/111" }, "b": { "name": "服 B", "url": "https://booth.pm/ja/items/111" } }
            """);

        var result = FaraVrcAlbum.Tools.Services.AvatarOutfitService.OutfitFillFromBooth(editor, apply: true, _ => NewInfo, FillNow, onlyIds: new HashSet<string> { "b" });

        Assert.Equal(1, result.Updated);
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        Assert.Null(root["a"]!["thumbnail"]);                                                                    // 選ばなかった服は触らない
        Assert.Equal("https://booth.pximg.net/new.jpg", (string?)root["b"]!["thumbnail"]);
    }
}
