using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Services;
using TestImages = FaraVrcAlbum.Images.Tests.TestImages;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>
/// 表示名の規則: ZWJ（U+200D）と ZWNJ（U+200C）は残す（絵文字の結合・ペルシア語の表記を壊さない）。
/// それ以外の制御・書式文字（双方向の制御・ゼロ幅スペース・BOM など）は除く。連続する結合の制御は 1 つにまとめる。
/// ワールドの同時刻の観測は名前を変えない。XMP のワールド名が「無い」と「使えない」は、注意の文言を分けて伝える。
/// </summary>
public sealed class DisplayNameZwjTests : IDisposable
{
    private const string Family = "\U0001F468‍\U0001F469‍\U0001F467";   // 👨‍👩‍👧（ZWJ で結合した絵文字）
    private const string Zwj = "‍";
    private const string Zwnj = "‌";
    private readonly RepoFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ZwjEmojiSequencesAreKeptAndAreValid()
    {
        Assert.Equal(Family, DisplayName.Clean(Family));
        Assert.Null(DisplayName.Problem(Family));
        Assert.Equal("家族 " + Family, DisplayName.Clean("家族  " + Family + " "));
    }

    [Fact]
    public void ZwnjInPersianTextIsKeptAndAreValid()
    {
        var persian = "کتاب" + Zwnj + "ها";   // 「کتاب‌ها」（ZWNJ で分ける表記）
        Assert.Equal(persian, DisplayName.Clean(persian));
        Assert.Null(DisplayName.Problem(persian));
    }

    [Fact]
    public void BidiControlsZeroWidthSpaceAndBomAreStillRemoved()
    {
        // 双方向の制御（U+202E・U+2066・U+2069）・ゼロ幅スペース（U+200B）・BOM（U+FEFF）は除く
        var raw = "﻿A‮b⁦c⁩​";
        Assert.Equal("Abc", DisplayName.Clean(raw));
        Assert.NotNull(DisplayName.Problem(raw));
        Assert.NotNull(DisplayName.Problem("a‪b"));
    }

    [Fact]
    public void ConsecutiveJoinersAreCollapsedToOneAndTheRawValueIsFlagged()
    {
        var raw = "a" + Zwj + Zwnj + Zwj + "b";
        Assert.Equal("a" + Zwj + "b", DisplayName.Clean(raw));
        Assert.NotNull(DisplayName.Problem(raw));    // 整えた値と違うので、登録簿・worlds.json の検査は指摘する
        Assert.Null(DisplayName.Problem("a" + Zwj + "b"));
    }

    [Fact]
    public void JoinersAtTheEdgesAreTrimmedAndOnlyJoinersLeavesNothing()
    {
        Assert.Equal("a", DisplayName.Clean(Zwj + "a" + Zwnj));
        Assert.NotNull(DisplayName.Problem(Zwj + "a"));
        Assert.Null(DisplayName.Clean(Zwj + Zwnj + " "));
        Assert.NotNull(DisplayName.Problem(Zwj + Zwnj + " "));
    }

    [Fact]
    public async Task ContentLoaderAcceptsAZwjNameButRefusesAZeroWidthSpace()
    {
        RepoFixture.Write(_fixture.Worlds, "{ \"" + RepoFixture.WorldOld + "\": { \"name\": \"" + Family + " 家\", \"aliases\": [\"کتاب" + Zwnj + "ها\"] } }");
        ContentLoader.Load(_fixture.ContentDir);   // 例外にならない（絵文字の結合は正しい名前）

        RepoFixture.Write(_fixture.Worlds, "{ \"" + RepoFixture.WorldOld + "\": { \"name\": \"悪​い\", \"aliases\": [] } }");
        var error = Assert.Throws<ContentException>(() => ContentLoader.Load(_fixture.ContentDir));
        Assert.Contains(error.Problems, p => p.Contains("name") && p.Contains("制御文字"));
        await Task.CompletedTask;
    }

    [Fact]
    public void AWorldObservedAtTheSameInstantDoesNotChangeTheNameOrTheObservedTime()
    {
        // マスタの名前を観測した時刻と同じ時刻の写真が、別の名前を持っていても、name も nameAt も変えない（厳密に新しいときだけ変える）
        const string observed = "2026-10-24T09:00:00+09:00";
        var master = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = "マスタの名前",
            ["aliases"] = new System.Text.Json.Nodes.JsonArray(),
            ["nameAt"] = observed,
        };
        RepoFixture.Write(_fixture.Worlds, new System.Text.Json.Nodes.JsonObject { [RepoFixture.WorldOld] = master }.ToJsonString());
        var ledger = _fixture.Ledger(("2026-10/20261024-090000-000_11223344", "1111", LedgerStates.Uploaded, observed));
        ledger.BySha["1111"].World = new LedgerWorld { Id = RepoFixture.WorldOld, Name = "同時刻の別の名前" };

        var result = DraftService.Run(_fixture.Editor, ledger, new DraftRequest { Slug = "same-instant", Title = "同時刻", Keys = [ledger.BySha["1111"].Key] }, DateTimeOffset.Parse("2026-10-25T00:00:00+09:00"));

        var saved = ContentEditor.ReadObject(_fixture.Worlds)[RepoFixture.WorldOld]!.AsObject();
        Assert.Equal("マスタの名前", saved["name"]!.GetValue<string>());
        Assert.Equal(DateTimeOffset.Parse(observed), DateTimeOffset.Parse(saved["nameAt"]!.GetValue<string>()));
        Assert.Contains(result.Messages, m => m.Contains("name は変えません"));   // 別の名前は aliases に残す
    }

    [Fact]
    public async Task AnUnusableXmpWorldNameAndAMissingOneGetDifferentWarnings()
    {
        var unusable = await DryRunWorldAsync(TestImages.XmpAttributes(worldName: "​‮"), "unusable.png");
        Assert.Contains(unusable.Warnings, w => w.Contains("使えない") && w.Contains("制御文字"));
        Assert.DoesNotContain(unusable.Warnings, w => w.Contains("XMP に無い"));

        var missing = await DryRunWorldAsync(TestImages.XmpAttributes(worldName: ""), "missing.png");
        Assert.Contains(missing.Warnings, w => w.Contains("XMP に無い"));
        Assert.DoesNotContain(missing.Warnings, w => w.Contains("使えない"));

        var tooLong = await DryRunWorldAsync(TestImages.XmpAttributes(worldName: new string('x', 65)), "long.png");
        Assert.Contains(tooLong.Warnings, w => w.Contains("使えない"));
    }

    private async Task<IngestResult> DryRunWorldAsync(string xmp, string fileName)
    {
        var path = Path.Combine(_fixture.Root, fileName);
        File.WriteAllBytes(path, TestImages.Png(640, 360, xmp));
        var ledger = PhotoLedger.Empty();
        return await new PhotoIngestor(ledger, null).IngestAsync(path, new IngestOptions { Apply = false });
    }
}
