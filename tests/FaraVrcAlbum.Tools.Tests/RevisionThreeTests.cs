using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;
using System.Text.Json.Nodes;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>
/// PR #13 の独立レビューへの回帰テスト: 同じ名前の観測日時の前進、厳密な比較、ドライランの owner 衝突、
/// 名前が使えない場合の owner 設定の理由、表示名の共通の規則（制御文字・長さ）、ワールド名の旧名の扱い。
/// R2 には繋がない（MemoryObjectStore）。名前・ID は試験用の値だけ。
/// </summary>
public sealed class RevisionThreeTests : IDisposable
{
    private const string Prod = RepoContext.ProductionBucket;
    private static readonly DateTimeOffset T1 = DateTimeOffset.Parse("2026-10-01T10:00:00+09:00");
    private static readonly DateTimeOffset T2 = DateTimeOffset.Parse("2026-10-02T10:00:00+09:00");
    private static readonly DateTimeOffset T3 = DateTimeOffset.Parse("2026-10-03T10:00:00+09:00");

    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void TheSharedDisplayNameRuleRemovesControlAndFormatCharactersAndCountsRunes()
    {
        Assert.Equal("新しい名前", DisplayName.Clean("​新しい‮名前 "));
        Assert.Equal("A B", DisplayName.Clean("  A \t B  "));
        Assert.Null(DisplayName.Clean(string.Concat(Enumerable.Repeat("\U00020000", 65))));   // 65 文字（サロゲートペア）は上限超え
        Assert.NotNull(DisplayName.Problem("a​b"));
        Assert.Null(DisplayName.Problem(string.Concat(Enumerable.Repeat("\U00020000", 64))));
    }

    // ---- ワールド名 ----

    private void Master(string? nameAt, string name, params string[] aliases)
    {
        var master = new JsonObject { ["name"] = name, ["aliases"] = new JsonArray(aliases.Select(a => (JsonNode)a).ToArray()) };
        if (nameAt is not null) master["nameAt"] = nameAt;
        RepoFixture.Write(_fixture.Worlds, new JsonObject { [RepoFixture.WorldOld] = master }.ToJsonString());
    }

    private PhotoLedger Row(string xmpWorldName, string takenAt = "2026-10-24T09:00:00+09:00")
    {
        var key = takenAt == "2026-10-24T09:00:00+09:00" ? "2026-10/20261024-090000-000_11223344" : RepoFixture.Key1;
        var ledger = _fixture.Ledger((key, "1111", LedgerStates.Uploaded, takenAt));
        ledger.BySha["1111"].World = new LedgerWorld { Id = RepoFixture.WorldOld, Name = xmpWorldName };
        return ledger;
    }

    private DraftResult Draft(PhotoLedger ledger, bool apply = true) =>
        DraftService.Run(_fixture.Editor, ledger, new DraftRequest { Slug = "rev3-post", Title = "改訂", Keys = [ledger.BySha["1111"].Key] }, DateTimeOffset.Parse("2026-10-25T00:00:00+09:00"), apply);

    [Fact]
    public void ASameWorldNameSeenLaterAdvancesNameAtWithoutChangingTheName()
    {
        Master("2026-10-01T00:00:00+09:00", "同じ名前");
        var ledger = Row("同じ名前", "2026-10-24T09:00:00+09:00");

        var result = Draft(ledger);

        Assert.Contains(result.Messages, m => m.Contains("観測日時を進めました"));
        var master = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("同じ名前", master["name"]!.GetValue<string>());
        Assert.Equal(DateTimeOffset.Parse("2026-10-24T09:00:00+09:00"), DateTimeOffset.Parse(master["nameAt"]!.GetValue<string>()));
    }

    [Fact]
    public void ANewNameThatIsAlreadyAnAliasIsRemovedFromAliasesWhenItBecomesTheName()
    {
        Master("2026-10-01T00:00:00+09:00", "旧", "新");   // 「新」は既に旧名（aliases）にある
        var ledger = Row("新", "2026-10-24T09:00:00+09:00");

        Draft(ledger);

        var master = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("新", master["name"]!.GetValue<string>());
        var aliases = master["aliases"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.DoesNotContain("新", aliases);     // name と aliases に同じ名前を重ねない
        Assert.Contains("旧", aliases);           // 旧名は残す
        _fixture.Editor.ValidateOrThrow();
    }

    [Fact]
    public void AControlCharacterInTheWorldNameIsRemovedBeforeItIsWritten()
    {
        Master(null, "旧名のワールド");
        var ledger = Row("​新しい‮名前 ", "2026-10-24T09:00:00+09:00");

        Draft(ledger);

        var master = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("新しい名前", master["name"]!.GetValue<string>());
        _fixture.Editor.ValidateOrThrow();
    }

    [Fact]
    public void AWorldNameWithControlCharactersInTheMasterIsRejectedByTheLoader()
    {
        // 公開データ（worlds.json）の name と aliases も、制御・書式文字と長さを検査する
        RepoFixture.Write(_fixture.Worlds, "{ \"" + RepoFixture.WorldOld + "\": { \"name\": \"悪​い\", \"aliases\": [\"" + new string('a', 65) + "\"] } }");

        var error = Assert.Throws<ContentException>(() => ContentLoader.Load(_fixture.ContentDir));
        Assert.Contains(error.Problems, p => p.Contains("name") && p.Contains("制御文字"));
        Assert.Contains(error.Problems, p => p.Contains("aliases") && p.Contains("64 文字まで"));
    }
}
