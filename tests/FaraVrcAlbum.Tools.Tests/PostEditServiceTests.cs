using System.Text.Json.Nodes;
using FaraVrcAlbum.Tools.Services;
using Xunit;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>記事の文章の編集（ほかの項目は保つ・検査に通らなければ戻す・プレビューは書かない）。</summary>
public sealed class PostEditServiceTests : IDisposable
{
    private readonly RepoFixture _repo = new();

    public PostEditServiceTests() => _repo.WriteExistingPost("old-post");

    public void Dispose() => _repo.Dispose();

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 3, 0, 0, TimeSpan.Zero);

    private PostEditChanges Changes(PostEditModel m, string? text = null, bool? draft = null, IReadOnlyList<string>? tags = null, string? publishedAt = null,
        IReadOnlyList<(string, string, string)>? photos = null) => new(
        "新しい題", publishedAt ?? m.PublishedAt, draft ?? m.Draft, tags ?? ["night"], "はじめの文章", "おわりの文章",
        photos ?? [(RepoFixture.Key1, "", ""), (RepoFixture.Key2, "二枚目の説明", text ?? "ここから **夜**")]);

    [Fact]
    public void LoadShowsTheAutomaticInfoAndTheEditableText()
    {
        var m = PostEditService.Load(_repo.Editor, "old-post");
        Assert.Equal("既存の記事", m.Title);
        Assert.Equal(2, m.Photos.Count);
        Assert.Equal("旧名のワールド", m.Photos[0].WorldName);                          // ワールドの名前はマスタから
        Assert.Null(m.Photos[1].WorldName);
        Assert.Equal("", m.Photos[1].Text);
    }

    [Fact]
    public void SaveWritesTheTextAndKeepsEverythingElse()
    {
        var m = PostEditService.Load(_repo.Editor, "old-post");
        PostEditService.Save(_repo.Editor, "old-post", Changes(m), apply: true, Now);

        var post = JsonNode.Parse(File.ReadAllText(_repo.PostPath("old-post")))!;
        Assert.Equal("新しい題", post["title"]!.GetValue<string>());
        Assert.Equal("はじめの文章", post["body"]!.GetValue<string>());
        Assert.Equal("おわりの文章", post["afterword"]!.GetValue<string>());
        Assert.Equal("ここから **夜**", post["photos"]![1]!["text"]!.GetValue<string>());
        Assert.Equal("二枚目の説明", post["photos"]![1]!["caption"]!.GetValue<string>());
        Assert.Equal(RepoFixture.Key1, post["cover"]!.GetValue<string>());              // 表紙・写真の並び・ワールドは変えない
        Assert.Equal(RepoFixture.WorldOld, post["photos"]![0]!["world"]!["id"]!.GetValue<string>());
        Assert.Equal("2026-10-26T12:00:00+09:00", post["updatedAt"]!.GetValue<string>()); // 公開済みの記事を直したので更新日時
        Assert.Equal("ここから **夜**", PostEditService.Load(_repo.Editor, "old-post").Photos[1].Text);
    }

    [Fact]
    public void APreviewWritesNothingAndAnInvalidChangeIsRolledBack()
    {
        var before = File.ReadAllText(_repo.PostPath("old-post"));
        var m = PostEditService.Load(_repo.Editor, "old-post");
        PostEditService.Save(_repo.Editor, "old-post", Changes(m), apply: false, Now);
        Assert.Equal(before, File.ReadAllText(_repo.PostPath("old-post")));

        Assert.Throws<InvalidDataException>(() => PostEditService.Save(_repo.Editor, "old-post", Changes(m, publishedAt: "あした"), apply: true, Now));
        // 写真は全部 1 回ずつ（外す・足す・2 回はできない）
        Assert.Throws<InvalidDataException>(() => PostEditService.Save(_repo.Editor, "old-post", Changes(m, photos: [(RepoFixture.Key1, "", "")]), apply: true, Now));
        Assert.Throws<InvalidDataException>(() => PostEditService.Save(_repo.Editor, "old-post",
            Changes(m, photos: [(RepoFixture.Key1, "", ""), (RepoFixture.Key1, "", "")]), apply: true, Now));
        Assert.Equal(before, File.ReadAllText(_repo.PostPath("old-post")));
    }

    [Fact]
    public void EmptyTextRemovesTheFieldAndADraftCanBePublished()
    {
        var m = PostEditService.Load(_repo.Editor, "old-post");
        PostEditService.Save(_repo.Editor, "old-post", Changes(m, text: "  ", draft: true), apply: true, Now);
        var post = JsonNode.Parse(File.ReadAllText(_repo.PostPath("old-post")))!;
        Assert.Null(post["photos"]![1]!["text"]);
        Assert.True(post["draft"]!.GetValue<bool>());

        m = PostEditService.Load(_repo.Editor, "old-post");
        PostEditService.Save(_repo.Editor, "old-post", Changes(m, draft: false), apply: true, Now);
        Assert.False(JsonNode.Parse(File.ReadAllText(_repo.PostPath("old-post")))!["draft"]!.GetValue<bool>());
    }

    [Fact]
    public void PhotosCanBeReorderedAndNewTagsAreRegisteredFromFreeText()
    {
        var m = PostEditService.Load(_repo.Editor, "old-post");
        PostEditService.Save(_repo.Editor, "old-post",
            Changes(m, tags: ["夜景", "雪景色", "winter"], photos: [(RepoFixture.Key2, "", ""), (RepoFixture.Key1, "", "")]), apply: true, Now);

        var post = JsonNode.Parse(File.ReadAllText(_repo.PostPath("old-post")))!;
        Assert.Equal([RepoFixture.Key2, RepoFixture.Key1], post["photos"]!.AsArray().Select(p => p!["key"]!.GetValue<string>()));
        Assert.Equal(RepoFixture.WorldOld, post["photos"]![1]!["world"]!["id"]!.GetValue<string>());   // 並べ替えてもほかの項目は保つ
        var tags = post["tags"]!.AsArray().Select(t => t!.GetValue<string>()).ToList();
        Assert.Equal("night", tags[0]);                                                  // 既存のタグは名前でも見つかる
        Assert.StartsWith("tag-", tags[1]);                                              // 日本語の新しいタグは tag-ハッシュ
        Assert.Equal("winter", tags[2]);
        var master = JsonNode.Parse(File.ReadAllText(_repo.Tags))!;
        Assert.Equal("雪景色", master[tags[1]]!["name"]!.GetValue<string>());
        Assert.Equal("winter", master["winter"]!["name"]!.GetValue<string>());
    }
}
