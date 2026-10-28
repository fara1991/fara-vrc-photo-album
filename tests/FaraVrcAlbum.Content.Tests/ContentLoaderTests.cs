using Xunit;

namespace FaraVrcAlbum.Content.Tests;

public sealed class ContentLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "album-content-" + Guid.NewGuid().ToString("N")[..8]);

    public ContentLoaderTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "posts"));
        File.WriteAllText(Path.Combine(_dir, "worlds.json"), "{ \"" + W1 + "\": { \"name\": \"ワールド 1\", \"aliases\": [\"旧名\"] } }");
        File.WriteAllText(Path.Combine(_dir, "tags.json"), "{ \"sample\": { \"name\": \"サンプル\" }, \"night\": { \"name\": \"夜景\", \"description\": \"夜\" } }");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string Key = "2026-10/20261023-213312-345_a1b2c3d4";
    private const string W1 = "wrld_00000000-0000-4000-8000-000000000001";
    private const string W2 = "wrld_00000000-0000-4000-8000-000000000002";
    private const string Photographer = "p_00000000000000aa";      // 撮影者は人物 ID（p_）。usr_ は Git に書けない
    private const string Usr = "usr_00000000-0000-4000-8000-0000000000aa";

    private void Write(string name, string json) => File.WriteAllText(Path.Combine(_dir, "posts", name), json);

    private void WriteRoot(string name, string json) => File.WriteAllText(Path.Combine(_dir, name), json);

    private static string Post(string slug, string published = "2026-10-24T20:00:00+09:00", string extra = "", string photos = "[]") =>
        "{ \"schema\": 1, \"slug\": \"" + slug + "\", \"title\": \"題 " + slug + "\", \"publishedAt\": \"" + published + "\", \"body\": \"本文\"" + extra + ", \"photos\": " + photos + " }";

    private static string Photo(string key, int w = 2048, int h = 1152, string extra = "") =>
        "{ \"key\": \"" + key + "\", \"w\": " + w + ", \"h\": " + h + extra + " }";

    private ContentException Fails() => Assert.Throws<ContentException>(() => ContentLoader.Load(_dir));

    private static string World(string id = W1, string name = "撮影時の名前") => "{ \"id\": \"" + id + "\", \"name\": \"" + name + "\" }";

    [Fact]
    public void LoadsPostsNewestFirstKeepsUnpublishedAndFuturePostsAndFiltersPerEnvironment()
    {
        Write("old.json", Post("old", "2026-10-01T00:00:00+09:00"));
        Write("new.json", Post("new", "2026-10-24T00:00:00+09:00"));
        Write("hidden.json", Post("hidden", extra: ", \"draft\": true"));
        Write("future.json", Post("future", "2099-01-01T00:00:00+09:00"));

        var content = ContentLoader.Load(_dir);

        var at = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(9));
        Assert.Equal(["future", "hidden", "new", "old"], content.Posts.Select(p => p.Slug).Order(StringComparer.Ordinal));   // 下書きも読む
        Assert.Equal(["new", "old"], content.ForEnvironment("production", at).Posts.Select(p => p.Slug));                  // 本番: 非公開も予約公開も出さない
        var develop = content.ForEnvironment("develop", at).Posts;
        Assert.Equal(["future", "new", "old"], develop.Select(p => p.Slug).Order(StringComparer.Ordinal));                // 確認用: 非公開は出さない（予約公開は出す）
    }

    [Fact]
    public void ProductionHidesAPostOnlyWhilePublishedAtIsAfterNow()
    {
        var publishedAt = new DateTimeOffset(2026, 11, 1, 0, 30, 0, TimeSpan.FromHours(9));
        Write("p.json", Post("p", "2026-11-01T00:30:00+09:00"));
        var content = ContentLoader.Load(_dir);

        Assert.Empty(content.ForEnvironment("production", publishedAt.AddTicks(-1)).Posts);
        Assert.Single(content.ForEnvironment("production", publishedAt).Posts);                  // 同時刻は出す
        Assert.Single(content.ForEnvironment("production", publishedAt.AddDays(1)).Posts);
        Assert.Single(content.ForEnvironment("develop", publishedAt.AddDays(-30)).Posts);       // 確認用は予約公開も出す
    }

    [Fact]
    public void MissingPostsFolderAndMastersAreEmpty()
    {
        Directory.Delete(Path.Combine(_dir, "posts"));
        File.Delete(Path.Combine(_dir, "worlds.json"));
        File.Delete(Path.Combine(_dir, "tags.json"));
        var content = ContentLoader.Load(_dir);
        Assert.Empty(content.Posts);
        Assert.Empty(content.Worlds);
        Assert.Empty(content.Tags);
    }

    [Fact]
    public void ReportsAllProblemsAtOnce()
    {
        Write("Bad_Slug.json", Post("Bad_Slug"));
        Write("mismatch.json", Post("other"));
        Write("badkey.json", Post("badkey", photos: "[" + Photo("x/y", 10, 10) + "]"));
        Write("nocover.json", Post("nocover", extra: ", \"cover\": \"" + Key + "\""));
        Write("broken.json", "{ not json");

        var e = Fails();

        Assert.Contains(e.Problems, p => p.StartsWith("Bad_Slug.json") && p.Contains("slug が不正"));
        Assert.Contains(e.Problems, p => p.StartsWith("mismatch.json") && p.Contains("一致しません"));
        Assert.Contains(e.Problems, p => p.StartsWith("badkey.json") && p.Contains("key の形式"));
        Assert.Contains(e.Problems, p => p.StartsWith("nocover.json") && p.Contains("cover"));
        Assert.Contains(e.Problems, p => p.StartsWith("broken.json") && p.Contains("JSON を読めません"));
    }

    [Fact]
    public void RejectsUnknownFieldsDuplicateKeysBadTagsAndMissingDate()
    {
        Write("typo.json", Post("typo", extra: ", \"titel\": \"x\""));
        Write("dupkey.json", Post("dupkey", photos: "[" + Photo(Key) + ", " + Photo(Key) + "]"));
        Write("tag.json", Post("tag", extra: ", \"tags\": [\"夜景\"]"));
        Write("nodate.json", "{ \"schema\": 1, \"slug\": \"nodate\", \"title\": \"t\", \"body\": \"\" }");

        var e = Fails();

        Assert.Contains(e.Problems, p => p.StartsWith("typo.json"));
        Assert.Contains(e.Problems, p => p.StartsWith("dupkey.json") && p.Contains("重複"));
        Assert.Contains(e.Problems, p => p.StartsWith("tag.json") && p.Contains("タグが不正"));
        Assert.Contains(e.Problems, p => p.StartsWith("nodate.json") && p.Contains("publishedAt"));
    }

    [Fact]
    public void UnknownKeysAreRejectedAtEveryLevel()
    {
        Write("a.json", Post("a", photos: "[" + Photo(Key, extra: ", \"world\": { \"id\": \"" + W1 + "\", \"nmae\": \"x\" }") + "]"));
        Write("b.json", Post("b", photos: "[" + Photo(Key, extra: ", \"author\": { \"name\": \"Fara\", \"nick\": \"x\" }") + "]"));
        Write("c.json", Post("c", photos: "[" + Photo(Key, extra: ", \"instance\": \"12345~private\"") + "]"));      // インスタンス ID を置く場所は無い
        Write("d.json", Post("d", photos: "[" + Photo(Key, extra: ", \"sourceName\": \"VRChat_2026-10-23_21-33-12.345_3840x2160.png\"") + "]"));
        WriteRoot("worlds.json", "{ \"" + W1 + "\": { \"name\": \"A\", \"aliasses\": [] } }");
        WriteRoot("tags.json", "{ \"sample\": { \"name\": \"A\", \"colour\": \"red\" } }");

        var e = Fails();

        foreach (var file in new[] { "a.json", "b.json", "c.json", "d.json", "worlds.json", "tags.json" })
            Assert.Contains(e.Problems, p => p.StartsWith(file + ": JSON を読めません"));
    }

    [Fact]
    public void AWorldNameAtMustBeADate()
    {
        // ワールドの名前を観測した日時（nameAt）は日時の形式だけ（壊れた値で名前の更新の判断をしない）
        WriteRoot("worlds.json", "{ \"" + W1 + "\": { \"name\": \"A\", \"nameAt\": \"きのう\" } }");
        var e = Fails();
        Assert.Contains(e.Problems, p => p.Contains("nameAt"));
    }

    [Theory]
    [InlineData("https://evil.example/x.png")]
    [InlineData("worlds/00000000-0000-4000-8000-000000000099/file_00000000-0000-4000-8000-0000000000aa_3")]
    [InlineData("worlds/../file_00000000-0000-4000-8000-0000000000aa_3")]
    public void AWorldImageKeyMustBeUnderThatWorld(string key)
    {
        // R2 のワールドの画像は worlds/{そのワールドの uuid}/file_…_{版} だけ
        WriteRoot("worlds.json", "{ \"" + W1 + "\": { \"name\": \"A\", \"image\": { \"key\": \"" + key + "\", \"w\": 1024, \"h\": 768 } } }");
        var e = Fails();
        Assert.Contains(e.Problems, p => p.Contains("image"));
    }

    [Fact]
    public void WorldStatsMustReferToKnownWorldsAndHaveNoUserIds()
    {
        WriteRoot("worlds.json", "{ \"" + W1 + "\": { \"name\": \"A\" } }");
        WriteRoot("world-stats.json", "{ \"fetchedAt\": \"2026-10-26T12:00:00+09:00\", \"worlds\": { \"" + W2 + "\": { \"visits\": 1 }, \"" + W1 + "\": { \"description\": \"by usr_00000000-0000\" } } }");
        var e = Fails();
        Assert.Contains(e.Problems, p => p.Contains(W2) && p.Contains("worlds.json にありません"));
        Assert.Contains(e.Problems, p => p.Contains("usr_"));
    }

    [Fact]
    public void DuplicateKeysAreRejectedNestedAndInTheMasters()
    {
        Write("a.json", Post("a", photos: "[" + Photo(Key, extra: ", \"world\": { \"id\": \"" + W1 + "\", \"id\": \"" + W2 + "\" }") + "]"));
        WriteRoot("worlds.json", "{ \"" + W1 + "\": { \"name\": \"A\" }, \"" + W1 + "\": { \"name\": \"B\" } }");
        WriteRoot("tags.json", "{ \"sample\": { \"name\": \"A\" }, \"sample\": { \"name\": \"B\" } }");

        var e = Fails();

        Assert.Contains(e.Problems, p => p.StartsWith("a.json: JSON を読めません"));
        Assert.Contains(e.Problems, p => p.StartsWith("worlds.json: JSON を読めません"));      // 辞書のキーの重複も後勝ちにしない
        Assert.Contains(e.Problems, p => p.StartsWith("tags.json: JSON を読めません"));
    }

    [Fact]
    public void NullsAndMissingSchemaAreReportedAsReadableProblemsNotCrashes()
    {
        Write("tagsnull.json", Post("tagsnull", extra: ", \"tags\": null"));
        Write("photosnull.json", "{ \"schema\": 1, \"slug\": \"photosnull\", \"title\": \"t\", \"publishedAt\": \"2026-10-24T20:00:00+09:00\", \"photos\": null }");
        Write("photoelem.json", Post("photoelem", photos: "[null]"));
        Write("tagelem.json", Post("tagelem", extra: ", \"tags\": [null]"));
        Write("titlenull.json", "{ \"schema\": 1, \"slug\": \"titlenull\", \"title\": null, \"publishedAt\": \"2026-10-24T20:00:00+09:00\" }");
        Write("worldnull.json", Post("worldnull", photos: "[" + Photo(Key, extra: ", \"world\": null") + "]"));
        Write("authornull.json", Post("authornull", photos: "[" + Photo(Key, extra: ", \"author\": null") + "]"));
        Write("peoplenull.json", Post("peoplenull", photos: "[" + Photo(Key, extra: ", \"people\": null") + "]"));
        Write("personnull.json", Post("personnull", photos: "[" + Photo(Key, extra: ", \"people\": [\"p_0123456789ab\", null]") + "]"));
        Write("noschema.json", "{ \"slug\": \"noschema\", \"title\": \"t\", \"publishedAt\": \"2026-10-24T20:00:00+09:00\" }");
        Write("wrongtype.json", "{ \"schema\": 1, \"slug\": \"wrongtype\", \"title\": 5, \"publishedAt\": \"2026-10-24T20:00:00+09:00\" }");
        Write("nullfile.json", "null");
        WriteRoot("worlds.json", "{ \"" + W1 + "\": null }");

        var e = Fails();

        Assert.Contains(e.Problems, p => p.StartsWith("tagsnull.json: $.tags が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("photosnull.json: $.photos が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("photoelem.json: $.photos[0] が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("tagelem.json: $.tags[0] が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("titlenull.json: $.title が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("worldnull.json: $.photos[0].world が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("authornull.json: $.photos[0].author が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("peoplenull.json: $.photos[0].people が null です"));
        Assert.Contains(e.Problems, p => p.StartsWith("personnull.json: $.photos[0].people[1] が null です"));
        Assert.Contains(e.Problems, p => p == "noschema.json: schema がありません（1 を書いてください）");
        Assert.Contains(e.Problems, p => p.StartsWith("wrongtype.json: JSON を読めません"));
        Assert.Contains(e.Problems, p => p == "nullfile.json: 中身が空です");
        Assert.Contains(e.Problems, p => p.StartsWith("worlds.json: $." + W1 + " が null です"));
    }

    [Fact]
    public void ADuplicatedPropertyIsAnErrorNotLastWins()
    {
        Write("dup.json", "{ \"schema\": 1, \"slug\": \"dup\", \"title\": \"a\", \"title\": \"b\", \"publishedAt\": \"2026-10-24T20:00:00+09:00\" }");

        Assert.Contains(Fails().Problems, p => p.StartsWith("dup.json: JSON を読めません"));
    }

    [Theory]
    [InlineData("wrld_00000000-0000-4000-8000-0000000000ff")]      // マスタに無い
    public void AWorldMissingFromTheMasterIsABuildError(string worldId)
    {
        Write("w.json", Post("w", photos: "[" + Photo(Key, extra: ", \"world\": " + World(worldId)) + "]"));
        Assert.Contains(Fails().Problems, p => p.StartsWith("w.json") && p.Contains("content/worlds.json にありません") && p.Contains(worldId));
    }

    [Theory]
    [InlineData("wrld_00000000-0000-4000-8000-00000000000G")]
    [InlineData("WRLD_00000000-0000-4000-8000-000000000001")]
    [InlineData("wrld_00000000-0000-4000-8000-000000000001:12345~private(usr_x)")]       // インスタンス ID つき
    [InlineData("wrld_0000")]
    [InlineData("")]
    public void AMalformedWorldIdIsRejected(string worldId)
    {
        Write("w.json", Post("w", photos: "[" + Photo(Key, extra: ", \"world\": " + World(worldId)) + "]"));
        Assert.Contains(Fails().Problems, p => p.StartsWith("w.json") && p.Contains("world.id の形式が不正"));
    }

    [Fact]
    public void TheWorldMasterIsValidated()
    {
        WriteRoot("worlds.json", "{ \"" + W1 + "\": { \"name\": \" \" }, \"wrld_nope\": { \"name\": \"x\" }, \"" + W2 + "\": { \"name\": \"ok\", \"aliases\": [\"\"] } }");

        var problems = Fails().Problems;

        Assert.Contains(problems, p => p == "worlds.json: " + W1 + " の name が空です");
        Assert.Contains(problems, p => p.StartsWith("worlds.json: ワールド ID の形式が不正です") && p.Contains("wrld_nope"));
        Assert.Contains(problems, p => p == "worlds.json: " + W2 + " の aliases に空の名前があります");
    }

    [Fact]
    public void ATagMissingFromTheMasterOrAMalformedMasterSlugIsAnError()
    {
        WriteRoot("tags.json", "{ \"Night\": { \"name\": \"夜景\" }, \"sample\": { \"name\": \"\" } }");
        Write("t.json", Post("t", extra: ", \"tags\": [\"night\", \"sample\", \"sample\"]"));

        var problems = Fails().Problems;

        Assert.Contains(problems, p => p.StartsWith("tags.json: タグの slug が不正") && p.Contains("Night"));
        Assert.Contains(problems, p => p == "tags.json: sample の name が空です");
        Assert.Contains(problems, p => p.StartsWith("t.json") && p.Contains("\"night\" が content/tags.json にありません"));
        Assert.Contains(problems, p => p == "t.json: タグが重複しています: sample");
    }

    [Theory]
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", "2026-10-23T21:33:12+09:00", true)]
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", "2026-10-23T12:33:12Z", true)]                   // UTC でも JST の同じ時刻
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", "2026-10-23T21:33:12.345+09:00", true)]         // ミリ秒は無視
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", "2026-10-23T21:33:13+09:00", false)]            // 1 秒違い
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", "2026-10-23T21:33:12", false)]                  // オフセットなし
    [InlineData("2026-10/20261023-213312-345_a1b2c3d4", "2026-10-23", false)]
    public void TakenAtMustAgreeWithTheKeyTimeAndHaveAnOffset(string key, string takenAt, bool valid)
    {
        Write("t.json", Post("t", photos: "[" + Photo(key, extra: ", \"takenAt\": \"" + takenAt + "\"") + "]"));
        if (valid) Assert.Single(ContentLoader.Load(_dir).Posts);
        else Assert.Contains(Fails().Problems, p => p.StartsWith("t.json") && (p.Contains("takenAt") || p.Contains("食い違")));
    }

    [Fact]
    public void TheKeyMonthMustMatchTheFileNameDate()
    {
        Write("m.json", Post("m", photos: "[" + Photo("2026-09/20261023-213312-345_a1b2c3d4") + "]"));
        Assert.Contains(Fails().Problems, p => p.StartsWith("m.json") && p.Contains("月（yyyy-mm/）とファイル名の日付が食い違"));
    }

    [Theory]
    [InlineData("2025-10/20251031-235959-999_a1b2c3d4", false)]     // 2025 年 11 月より前
    [InlineData("2025-11/20251101-000000-000_a1b2c3d4", true)]      // ちょうど 11 月 1 日 0 時 (JST)
    [InlineData("2024-12/20241231-120000-000_a1b2c3d4", false)]
    public void PhotosBeforeNovember2025AreRejected(string key, bool valid)
    {
        Write("e.json", Post("e", photos: "[" + Photo(key) + "]"));
        if (valid) Assert.Single(ContentLoader.Load(_dir).Posts);
        else Assert.Contains(Fails().Problems, p => p.StartsWith("e.json") && p.Contains("2025 年 11 月より前"));
    }

    [Theory]
    [InlineData("2026-10-24T20:00:00", false)]                  // オフセットなしは PC のタイムゾーンで解釈が変わるので不可
    [InlineData("2026-10-24", false)]
    [InlineData("2026-10-24T20:00:00+09:00", true)]
    [InlineData("2026-10-24T11:00:00Z", true)]
    public void PublishedAtNeedsAnOffset(string publishedAt, bool valid)
    {
        Write("d.json", Post("d", publishedAt));
        if (valid) Assert.Single(ContentLoader.Load(_dir).Posts);
        else Assert.Contains(Fails().Problems, p => p.StartsWith("d.json: JSON を読めません") && p.Contains("オフセット付き"));
    }

    [Fact]
    public void AuthorAndPeopleAreNotInTheSchemaAndAreRefused()
    {
        // 撮影者・写っている人は記事に持たない（項目を置くとエラー）。名前も ID も Git に入れない
        Write("a.json", Post("a", photos: "[" + Photo(Key, extra: ", \"author\": { \"id\": \"p_00000000000000aa\" }") + "]"));
        Write("b.json", Post("b", photos: "[" + Photo(Key, extra: ", \"people\": [\"p_00000000000000aa\"]") + "]"));

        var problems = Fails().Problems;

        Assert.Contains(problems, p => p.StartsWith("a.json"));
        Assert.Contains(problems, p => p.StartsWith("b.json"));
    }

    [Fact]
    public void TheRepositoryContentIsValid()
    {
        // 手元は本物の content/、CI は samples/content が、検証に通ること（記事の JSON の間違いを止める）
        var content = ContentLoader.Load(RepoRoot.ContentDir());
        Assert.NotEmpty(content.Posts);
    }
}
