using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>
/// ワールド名の更新: 同じ wrld_ の新しい写真の名前で name を更新し、旧名は aliases に残す。
/// 古い写真の名前では name を変えない（nameAt より前の撮影日時）。worlds.json は Git の公開データ（R2 は使わない）。
/// </summary>
public sealed class WorldNameTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(9));
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    /// <summary>マスタ（WorldOld）と、そのワールドの写真 1 枚（撮影日時・XMP のワールド名を指定）。</summary>
    /// <summary>写真は 2026-10-24 09:00（キーと撮影日時が合うもの）。XMP のワールド名を指定する。</summary>
    private const string PhotoKey = "2026-10/20261024-090000-000_11223344";
    private const string PhotoTaken = "2026-10-24T09:00:00+09:00";

    private PhotoLedger Row(string xmpWorldName)
    {
        var ledger = _fixture.Ledger((PhotoKey, "1111", LedgerStates.Uploaded, PhotoTaken));
        var entry = ledger.BySha["1111"];
        entry.World = new LedgerWorld { Id = RepoFixture.WorldOld, Name = xmpWorldName };
        return ledger;
    }

    private void Master(string? nameAt, params string[] aliases)
    {
        var master = new System.Text.Json.Nodes.JsonObject { ["name"] = "旧名のワールド", ["aliases"] = new System.Text.Json.Nodes.JsonArray(aliases.Select(a => (System.Text.Json.Nodes.JsonNode)a).ToArray()) };
        if (nameAt is not null) master["nameAt"] = nameAt;
        RepoFixture.Write(_fixture.Worlds, new System.Text.Json.Nodes.JsonObject { [RepoFixture.WorldOld] = master }.ToJsonString());
    }

    private DraftResult Draft(PhotoLedger ledger, bool apply = true) =>
        DraftService.Run(_fixture.Editor, ledger, new DraftRequest { Slug = "world-post", Title = "ワールド", Keys = [ledger.BySha["1111"].Key] }, Now, apply);

    [Fact]
    public void ALegacyMasterWithoutNameAtTakesTheNewerNameAndKeepsTheOldAsAlias()
    {
        Master(null);
        var ledger = Row("新しい名前");

        var result = Draft(ledger);

        Assert.Contains(result.Messages, m => m.Contains("名前を更新"));
        var master = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("新しい名前", master["name"]!.GetValue<string>());
        Assert.Equal("旧名のワールド", master["aliases"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T09:00:00+09:00").ToString("O"), master["nameAt"]!.GetValue<string>());
        // 記事の写真は新しい名前（マスタの name）。ワールドの URL（id）は変わらない
        var post = ContentEditor.ReadObject(_fixture.PostPath("world-post"));
        Assert.Equal("新しい名前", post["photos"]!.AsArray()[0]!["world"]!["name"]!.GetValue<string>());
        Assert.Equal(RepoFixture.WorldOld, post["photos"]!.AsArray()[0]!["world"]!["id"]!.GetValue<string>());
        _fixture.Editor.ValidateOrThrow();
    }

    [Fact]
    public void APhotoOlderThanTheMasterNameIsNotAllowedToReplaceIt()
    {
        Master("2026-10-25T00:00:00+09:00");   // 名前を観測したのは 10/25。10/24 の写真の名前では変えない
        var ledger = Row("古い写真の名前");

        var result = Draft(ledger);

        Assert.Contains(result.Messages, m => m.Contains("name は変えません"));
        var master = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("旧名のワールド", master["name"]!.GetValue<string>());
        Assert.Equal("2026-10-25T00:00:00+09:00", DateTimeOffset.Parse(master["nameAt"]!.GetValue<string>()).ToString("yyyy-MM-ddTHH:mm:sszzz"));
        Assert.Equal("古い写真の名前", master["aliases"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void ANewerPhotoAfterTheObservedNameUpdatesAgainAndKeepsEveryOldName()
    {
        Master("2026-10-01T00:00:00+09:00", "さらに前の名前");
        var ledger = Row("最新の名前");

        Draft(ledger);

        var master = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("最新の名前", master["name"]!.GetValue<string>());
        var aliases = master["aliases"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Contains("さらに前の名前", aliases);
        Assert.Contains("旧名のワールド", aliases);
    }

    [Fact]
    public void ThePreviewShowsTheChangeAndWritesNothing()
    {
        Master(null);
        var before = File.ReadAllText(_fixture.Worlds);
        var ledger = Row("新しい名前");

        var result = Draft(ledger, apply: false);

        Assert.Contains(result.Messages, m => m.Contains("名前を更新"));
        Assert.Equal(before, File.ReadAllText(_fixture.Worlds));
        Assert.False(File.Exists(_fixture.PostPath("world-post")));
    }
}
