using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;
using Xunit;

namespace FaraVrcAlbum.Build.Tests;

/// <summary>Build の出力（本番用・確認用）を、実際に kit の build-pages.mjs まで通して確かめる。node と submodule が要る。</summary>
public class SiteBuilderTests
{
    private const string ImageHost = "https://vrc-photos.fara-labs.com";
    private const string Uuid1 = "00000000-0000-4000-8000-000000000001";
    private const string Uuid2 = "00000000-0000-4000-8000-000000000002";

    [Fact]
    public void TheContentFolderCanBeGivenExplicitlyAndTheBuildUsesThatFolderInsteadOfTheRepositoryContent()
    {
        var (f, production, _) = Build();
        Assert.True(Exists(production, "data/posts/second-post.json"));

        // 別の場所のコピーから 1 件の記事を除いて、そちらを指定して作る
        var alt = Path.Combine(Path.GetTempPath(), "album-alt-content-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            CopyDirectory(Path.Combine(f.Repo, "content"), alt);
            File.Delete(Path.Combine(alt, "posts", "second-post.json"));
            var output = f.Run("production", contentDir: alt);

            Assert.True(Exists(output, "data/posts/first-post.json"));
            Assert.False(Exists(output, "data/posts/second-post.json"));
        }
        finally
        {
            if (Directory.Exists(alt)) Directory.Delete(alt, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static (BuildFixture Fixture, string Production, string Develop) Build(Action<BuildFixture>? setup = null, string imageBaseUrl = ImageHost + "/", int perPage = 12, DateTimeOffset? now = null, bool defaults = true)
    {
        var fixture = new BuildFixture(imageBaseUrl, perPage);
        if (defaults)
        {
            fixture.AddPost("first-post", "最初の記事", new DateTimeOffset(2026, 10, 24, 20, 0, 0, TimeSpan.FromHours(9)), "これは **強調** の本文です。", photos: 2,
                tags: ["sample", "night"], world: BuildFixture.World1);
            fixture.AddPost("second-post", "二番目の記事", new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.FromHours(9)), "二番目。", noindex: true,
                world: BuildFixture.World2, worldSnapshotName: "撮影時の別名", photoOffset: 10);
            fixture.AddPost("draft-post", "下書き", new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(9)), draft: true, photoOffset: 20);
        }
        setup?.Invoke(fixture);
        return (fixture, fixture.Run("production", now), fixture.Run("develop", now));
    }

    private static string Read(string dir, string name) => File.ReadAllText(Path.Combine(dir, name));

    private static bool Exists(string dir, string name) => File.Exists(Path.Combine(dir, name));

    private static string Head(string html) => Regex.Match(html, "<head>(.*?)</head>", RegexOptions.Singleline).Groups[1].Value;

    private static string Body(string html) => Regex.Match(html, "<main class=\"entry-static\">.*?</main>", RegexOptions.Singleline).Value;

    private static bool IsNoindex(string html) => Head(html).Contains("<meta name=\"robots\" content=\"noindex\" />");

    private static IEnumerable<(string Name, string Text)> AllFiles(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => (Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllText(f)));

    /// <summary>ルールが無い（null）ときは失敗にする（DoesNotContain が null で誤って通らないように）。</summary>
    private static string MustRule(string headers, string path) => Rule(headers, path) ?? throw new Xunit.Sdk.XunitException("_headers に " + path + " のルールがありません");

    private static string? Rule(string headers, string path)
    {
        var lines = headers.Split('\n');
        var inside = false;
        var found = false;
        var block = new List<string>();
        foreach (var line in lines)
        {
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith('#')) { inside = line.Trim() == path; found |= inside; continue; }
            if (inside) block.Add(line);
        }
        return found ? string.Join("\n", block) : null;
    }

    [Fact]
    public void WritesAPageForEveryRouteAndNotTheDraft()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        foreach (var name in new[]
        {
            "index.html", "posts/first-post.html", "posts/second-post.html", "archive/2026/10.html", "tags.html", "tags/sample.html", "tags/night.html",
            "worlds.html", $"worlds/{Uuid1}.html", $"worlds/{Uuid2}.html", "privacy.html", "report.html", "404.html", "robots.txt", "sitemap.xml", "_headers", "_redirects", "feed.xml",
            "data/site.json", "data/posts-index.json", "data/archive.json", "data/tags.json", "data/worlds.json", $"data/worlds/{Uuid1}.json", "data/posts/first-post.json",
        })
            Assert.True(Exists(prod, name), name + " がありません");
        Assert.False(Exists(prod, "posts/draft-post.html"));
        Assert.False(Exists(prod, "data/posts/draft-post.json"));
        Assert.False(Exists(prod, "tags/unused.html"));                    // 記事が 0 件のタグのページは作らない
        Assert.DoesNotContain("下書き", Read(prod, "data/posts-index.json"));
    }

    [Fact]
    public void EveryPageHasTheHeadItemsAndTheStaticBody()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var notFound = Read(prod, "404.html");
        Assert.Contains("<meta name=\"robots\" content=\"noindex\" />", Head(notFound));
        foreach (var name in new[] { "index.html", "posts/first-post.html", "archive/2026/10.html", "tags.html", "tags/night.html", "worlds.html", $"worlds/{Uuid1}.html", "privacy.html", "report.html" })
        {
            var html = Read(prod, name);
            var head = Head(html);
            Assert.Matches("<title>[^<]+</title>", head);
            Assert.Contains("<meta name=\"description\"", head);
            Assert.Contains("<link rel=\"canonical\" href=\"https://vrc-album.fara-labs.com", head);
            Assert.Contains("<meta property=\"og:site_name\" content=\"Fara Photo Album\" />", head);
            Assert.Contains("<meta name=\"twitter:card\"", head);
            Assert.Single(Regex.Matches(html, "<main class=\"entry-static\">"));
            Assert.StartsWith("<main class=\"entry-static\">", Body(html));
            Assert.DoesNotContain("album-banner", Body(html));          // バナー画像はサイドバーのプロフィールカード（起動の後）に移した。静的 HTML には入れない
            Assert.DoesNotContain("images/header-", Body(html));
            Assert.Contains("rel=\"alternate\" type=\"application/rss+xml\"", html);       // RSS の発見用
        }
    }

    [Fact]
    public void EveryStaticPageHasTheSiteFooterWithTheFourSocialLinksWithoutJavaScript()
    {
        var (f, prod, develop) = Build();
        using var _ = f;
        // 本番と確認用の、出力のすべての HTML（404 を含む）。JavaScript が無くても、Web で起動したあとと同じフッターと SNS 4 つが読める
        foreach (var (dir, author) in new[] { (prod, "https://fara-labs.com/"), (develop, "https://dev.fara-labs.com/") })
        {
            var names = Directory.EnumerateFiles(dir, "*.html", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/')).ToList();
            Assert.Contains("404.html", names);
            foreach (var name in names)
            {
                var html = Read(dir, name);
                var footers = Regex.Matches(html, "<footer class=\"fbc-footer entry-static\">.*?</footer>", RegexOptions.Singleline);
                Assert.True(footers.Count == 1, name + ": フッターが 1 つではありません");
                Assert.Contains("</main><footer class=\"fbc-footer entry-static\">", html);      // 本文（main）の後ろ・#app の直下（読み込み表示の inert の対象）
                var footer = footers[0].Value;
                Assert.Equal(4, Regex.Matches(footer, "<a class=\"fbc-footer-social fbc-service-").Count);
                Assert.Contains("href=\"https://github.com/fara1991\"", footer);
                Assert.Contains("href=\"https://twitter.com/game_fara\"", footer);
                Assert.Contains("href=\"https://twitch.tv/game_fara\"", footer);
                Assert.Contains("href=\"https://discord.com/invite/B38eCSYyk5\"", footer);
                Assert.Contains("href=\"privacy\"", footer);
                Assert.Contains("© 2026 ", footer);
                Assert.Contains($"href=\"{author}\"", footer);                                    // 作者のホーム（確認用は dev のホストへ）
            }
        }

        // 問い合わせの ?from= は、ページの URL（Web と同じ）。404 には付けない
        Assert.Contains("href=\"report?from=%2Fposts%2Ffirst-post\"", Footer(prod, "posts/first-post.html"));
        Assert.Contains("href=\"report?from=%2F\"", Footer(prod, "index.html"));
        Assert.Contains("href=\"report\"", Footer(prod, "404.html"));
        Assert.Contains("<h1>ページが見つかりません</h1>", Read(prod, "404.html"));
    }

    [Fact]
    public void TheContactPageAndThePrivacyPageUseTheSameNamesEverywhere()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var report = Read(prod, "report.html");
        Assert.Contains("<title>問い合わせ | Fara Photo Album</title>", Head(report));
        Assert.Contains("問い合わせ", Body(report));
        foreach (var name in new[] { "report.html", "privacy.html", "index.html", "404.html" })
            Assert.DoesNotContain("作者へ報告", Read(prod, name));
        Assert.Contains("<title>プライバシーポリシー | Fara Photo Album</title>", Head(Read(prod, "privacy.html")));
        Assert.Contains("<a href=\"/report\">問い合わせ</a>", Body(Read(prod, "privacy.html")));
    }

    [Fact]
    public void ThePrivacyPageIsStaticAndDescribesTheServicesWithoutPlaceholderNotes()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var body = Body(Read(prod, "privacy.html"));
        Assert.DoesNotContain("雛形", body);
        Assert.DoesNotContain("Twitter（", body);
        Assert.DoesNotContain("&#x", body);
        Assert.Contains("<table", body);
        Assert.Contains("BOOTH（pixiv Inc.）の画像サーバー", body);
        Assert.Contains("Twitter", body);
        Assert.Contains($"制定: {PrivacyDate} / 最終改定: {PrivacyDate}", body);
        Assert.Contains("7 日以内", body);
    }

    // プライバシーポリシーの制定日・最終改定日（PrivacyView.razor の Enacted / Revised）。改定したら両方そろえて直す
    private const string PrivacyDate = "2026年10月29日";

    [Fact]
    public void ThePrivacyPageHasTheSameSectionsAsTheOtherSitesAndDescribesTheImplementation()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var body = Body(Read(prod, "privacy.html"));
        foreach (var heading in new[] { "運営者", "ブラウザに保存する情報", "服の掲載について", "広告", "変更について" })
            Assert.Contains($"<h2>{heading}</h2>", body);
        Assert.DoesNotContain("Cookie と端末への保存", body);
        Assert.DoesNotContain("迷惑な送信", body);
        Assert.DoesNotContain("匿名", body);
        Assert.Contains("EU 圏内からのアクセスは計測の対象外", body);
        Assert.Contains("商品説明のうち対応アバターと利用規約の抜粋", body);
        Assert.Contains("広告は、いま使っていません。", body);
        Assert.DoesNotContain("掃除", body);
        // 閲覧者に要らない技術の詳細（ホスト名・内部の仕組みの名前・保存のキー名・置き換え記号）は出さない
        foreach (var jargon in new[] { "fara-labs.com", "pximg.net", "googleapis", "R2", "Durable", "HMAC", "キャッシュ", "localStorage", "fara-vrc-album.", "@ImageHost", "秘密の鍵" })
            Assert.DoesNotContain(jargon, body);
        Assert.True(body.IndexOf("<h2>運営者</h2>", StringComparison.Ordinal) < body.IndexOf("<h2>利用目的</h2>", StringComparison.Ordinal));
    }

    private static string Footer(string dir, string name) =>
        Regex.Match(Read(dir, name), "<footer class=\"fbc-footer entry-static\">.*?</footer>", RegexOptions.Singleline).Value;

    [Fact]
    public void TheHomePageHasStaticTextForSearchEnginesAndNoJavaScript()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var body = Body(Read(prod, "index.html"));
        Assert.Contains("最初の記事", body);
        Assert.Contains("二番目の記事", body);
        Assert.Contains(" 枚", body);           // 日本語が文字参照（&#x…;）になっていない
        Assert.DoesNotContain("&#x", body);
        Assert.Contains("<h1 class=\"visually-hidden\">Fara Photo Album</h1>", body);       // 視覚的に隠した h1（ヘッダーは画像の『Fara』）
        Assert.Contains("Faraです。2021/7からVRChat始めてますがインバイトで良く引きこもってます。<br", body);
        Assert.Contains("たまにVRC潜って撮った写真や、フレンドに撮ってもらった写真をこのサイトに載せてます。", body);
        Assert.Contains("href=\"posts/first-post\"", body);  // 相対リンク（base href=/ の下で解決）
        Assert.Matches("<img[^>]+width=\"2048\"[^>]+height=\"1152\"", body);
        Assert.DoesNotContain("下書き", body);
    }

    [Fact]
    public void TextBeforePhotosAndTheAfterwordAreRenderedFromMarkdownAndTheSourceTextIsNotPublished()
    {
        var (f, prod, _) = Build(setup: fx =>
        {
            fx.AddPost("story", "文章のある記事", new DateTimeOffset(2026, 10, 22, 20, 0, 0, TimeSpan.FromHours(9)), "はじめ", photos: 3, photoOffset: 30);
            var path = Path.Combine(fx.Repo, "content", "posts", "story.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json["photos"]![1]!["text"] = "ここから **夜** の写真";
            json["afterword"] = "おわりの <b>文章</b>";
            File.WriteAllText(path, json.ToJsonString());
        });
        using var _ = f;
        var body = Body(Read(prod, "posts/story.html"));
        Assert.Contains("<strong>夜</strong>", body);
        Assert.Contains("&lt;b&gt;文章&lt;/b&gt;", body);                              // 生の HTML は文字として出す（本文と同じ規則）
        Assert.Equal(2, Regex.Matches(body, "<ul class=\"album-gallery").Count);
        var data = Read(prod, "data/posts/story.json");
        Assert.Contains("\"textHtml\"", data);
        Assert.Contains("\"afterwordHtml\"", data);
        Assert.DoesNotContain("\"text\":", data);                                     // 元の Markdown は公開データに入れない
        Assert.DoesNotContain("\"afterword\":", data);
    }

    [Fact]
    public void ThePostPageHasTheBodyGalleryWorldLinkMasterNameAndStructuredData()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var html = Read(prod, "posts/first-post.html");
        var body = Body(html);
        Assert.Contains("<strong>強調</strong>", body);
        Assert.Equal(2, Regex.Matches(body, "<li class=\"album-gallery-item\"").Count);
        // ワールド名はマスタの名前（記事の JSON の撮影時の名前ではない）。クリックで、サイト内のワールドのページ（そこで名前を押すと VRChat のワールドページへ）
        Assert.Matches(@"<a class=""album-world-link"" href=""worlds/" + Uuid1 + @""">" + Regex.Escape(BuildFixture.World1Name) + "</a>", body);
        Assert.DoesNotContain("vrchat.com/home/world", body);
        Assert.DoesNotContain("撮影時の名前", html);
        Assert.Contains("href=\"archive/2026/10\"", body);
        Assert.Contains("href=\"tags/night\"", body);
        Assert.Contains(">夜景</a>", body);
        Assert.Contains("<meta property=\"og:type\" content=\"article\" />", html);
        Assert.Contains("<meta property=\"og:image\" content=\"" + ImageHost + "/2026-10/", html);
        Assert.Contains("_og.jpg", html);
        var ld = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var json = JsonDocument.Parse(ld);
        Assert.Equal("BlogPosting", json.RootElement.GetProperty("@type").GetString());
        Assert.Equal("最初の記事", json.RootElement.GetProperty("headline").GetString());
        Assert.Equal("サンプル, 夜景", json.RootElement.GetProperty("keywords").GetString());
    }

    [Fact]
    public void ThePagesAreSplitByPostsPerPageWithARedirectForPageOne()
    {
        var (f, prod, _) = Build(fx => { for (var i = 0; i < 5; i++) fx.AddPost("post-" + i, "記事 " + i, new DateTimeOffset(2026, 9, 1 + i, 0, 0, 0, TimeSpan.FromHours(9)), photoOffset: 30 + i); }, perPage: 3);
        using var _ = f;
        // 公開 7 件（first・second と 5 件。draft 除く）を 3 件ずつ = 3 ページ
        Assert.True(Exists(prod, "page/2.html"));
        Assert.True(Exists(prod, "page/3.html"));
        Assert.False(Exists(prod, "page/4.html"));
        Assert.False(Exists(prod, "page/1.html"));
        Assert.Contains("/page/1 / 301", Read(prod, "_redirects"));
        Assert.Contains("rel=\"next\" href=\"page/2\"", Body(Read(prod, "index.html")));
        Assert.Contains("href=\"https://vrc-album.fara-labs.com/page/2\"", Head(Read(prod, "page/2.html")));
        Assert.Equal(3, Regex.Matches(Body(Read(prod, "index.html")), "class=\"album-card( album-card--feature)?\"").Count);       // 先頭の 1 件は「最新の記事」の大きなカード（album-card--feature）、残りが 3 列のグリッド
        Assert.Single(Regex.Matches(Body(Read(prod, "index.html")), "album-card--feature\"").ToList());
        Assert.Equal(2, Regex.Matches(Body(Read(prod, "index.html")), "<h2 class=\"album-section-heading\">").Count);                       // 最新の記事・これまでの記事
        Assert.DoesNotContain("album-card--feature", Body(Read(prod, "page/2.html")));                                                       // 2 ページ目以降は通常のグリッド
    }

    [Fact]
    public void TheDataJsonIsReadableByTheAppsSerializerContext()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var index = JsonSerializer.Deserialize(Read(prod, "data/posts-index.json"), SiteJsonContext.Default.ListPostSummary)!;
        Assert.Equal(["first-post", "second-post"], index.Select(p => p.Slug));
        Assert.Equal(2, index[0].PhotoCount);
        Assert.Equal(new[] { "sample", "night" }, index[0].Tags);
        var site = JsonSerializer.Deserialize(Read(prod, "data/site.json"), SiteJsonContext.Default.SiteSettings)!;
        Assert.Equal(ImageHost + "/", site.ImageBaseUrl);
        Assert.Equal(12, site.PostsPerPage);
        var post = JsonSerializer.Deserialize(Read(prod, "data/posts/first-post.json"), SiteJsonContext.Default.PostPage)!;
        Assert.Contains("<strong>強調</strong>", post.BodyHtml);
        Assert.Equal(new[] { "sample", "night" }, post.Tags.Select(t => t.Slug));
        Assert.Equal(new[] { "サンプル", "夜景" }, post.Tags.Select(t => t.Name));
        Assert.Equal(BuildFixture.World1Name, post.Photos[0].World!.Name);           // 公開データの world.name はマスタの名前
        Assert.Equal(BuildFixture.World1, post.Photos[0].World!.Id);
        Assert.Contains("本文", Read(prod, "data/posts/first-post.json"));   // 日本語が \u エスケープになっていない
        var archive = JsonSerializer.Deserialize(Read(prod, "data/archive.json"), SiteJsonContext.Default.DictionaryStringDictionaryStringInt32)!;
        Assert.Equal(2, archive["2026"]["10"]);
        var tags = JsonSerializer.Deserialize(Read(prod, "data/tags.json"), SiteJsonContext.Default.ListTagSummary)!;
        Assert.Equal([("sample", 2), ("night", 1)], tags.Select(t => (t.Slug, t.PostCount)));       // 使われないタグ（unused）は載せない
        var worlds = JsonSerializer.Deserialize(Read(prod, "data/worlds.json"), SiteJsonContext.Default.ListWorldSummary)!;
        Assert.Equal([(BuildFixture.World1, BuildFixture.World1Name, 2), (BuildFixture.World2, "マスタのワールド名 2", 1)], worlds.Select(w => (w.Id, w.Name, w.PhotoCount)));
    }

    [Fact]
    public void AFuturePostIsHiddenInProductionEverywhereAndShownInDevelop()
    {
        var future = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var (f, prod, dev) = Build(fx => fx.AddPost("scheduled-post", "予約公開の記事", future, tags: ["night"], world: BuildFixture.World2, photoOffset: 40));
        using var _ = f;

        // 本番: HTML・データ・sitemap・フィード・集計のどこにも無い
        Assert.False(Exists(prod, "posts/scheduled-post.html"));
        Assert.False(Exists(prod, "data/posts/scheduled-post.json"));
        Assert.False(Exists(prod, "archive/2099/01.html"));
        foreach (var (name, text) in AllFiles(prod))
        {
            if (name.EndsWith(".png") || name.EndsWith(".webp")) continue;
            Assert.False(text.Contains("予約公開の記事") || text.Contains("scheduled-post"), $"本番の {name} に予約公開の記事があります");
        }
        using var prodArchive = JsonDocument.Parse(Read(prod, "data/archive.json"));
        Assert.False(prodArchive.RootElement.TryGetProperty("2099", out var year2099));
        Assert.Contains("（1）", Body(Read(prod, "tags.html")));                      // night は first-post の 1 件だけ

        // 確認用: 出る（確認できるように）
        Assert.True(Exists(dev, "posts/scheduled-post.html"));
        Assert.True(Exists(dev, "archive/2099/01.html"));
        Assert.Contains("予約公開の記事", Read(dev, "data/posts-index.json"));
        Assert.Contains("（2）", Body(Read(dev, "tags.html")));
    }

    [Fact]
    public void AScheduledPostAppearsInProductionOnceNowReachesItsPublishedAt()
    {
        var publishedAt = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(9));
        var before = Build(fx => fx.AddPost("scheduled-post", "予約公開の記事", publishedAt, photoOffset: 40), now: publishedAt.AddSeconds(-1));
        using (before.Fixture) Assert.False(Exists(before.Production, "posts/scheduled-post.html"));
        var after = Build(fx => fx.AddPost("scheduled-post", "予約公開の記事", publishedAt, photoOffset: 40), now: publishedAt);
        using (after.Fixture) Assert.True(Exists(after.Production, "posts/scheduled-post.html"));
    }

    [Fact]
    public void TheArchiveIsByPublishedMonthInJstWithPagesAndCounts()
    {
        var now = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var (f, prod, _) = Build(fx =>
        {
            fx.AddPost("jst-november", "JST では 11 月", new DateTimeOffset(2026, 10, 31, 15, 30, 0, TimeSpan.Zero), photoOffset: 50);          // JST 2026-11-01 00:30
            fx.AddPost("jst-october", "JST では 10 月の最後", new DateTimeOffset(2026, 10, 31, 23, 59, 59, TimeSpan.FromHours(9)), photoOffset: 51);
            fx.AddPost("past-december", "過去日の公開", new DateTimeOffset(2025, 12, 15, 21, 0, 0, TimeSpan.FromHours(9)), photoOffset: 52);       // 過去日でもアーカイブはその月
        }, perPage: 2, now: now);
        using var _ = f;

        Assert.True(Exists(prod, "archive/2026/11.html"));
        Assert.Contains("JST では 11 月", Body(Read(prod, "archive/2026/11.html")));
        Assert.DoesNotContain("JST では 10 月の最後", Body(Read(prod, "archive/2026/11.html")));
        // 10 月は first-post・second-post・jst-october の 3 件 = 2 ページ（perPage 2）
        Assert.True(Exists(prod, "archive/2026/10.html"));
        Assert.True(Exists(prod, "archive/2026/10/page/2.html"));
        Assert.False(Exists(prod, "archive/2026/10/page/3.html"));
        Assert.Contains("rel=\"next\" href=\"archive/2026/10/page/2\"", Body(Read(prod, "archive/2026/10.html")));
        Assert.Contains("href=\"https://vrc-album.fara-labs.com/archive/2026/10/page/2\"", Head(Read(prod, "archive/2026/10/page/2.html")));
        Assert.Contains("2026年10月の記事", Head(Read(prod, "archive/2026/10.html")));
        Assert.True(Exists(prod, "archive/2025/12.html"));
        var archive = JsonSerializer.Deserialize(Read(prod, "data/archive.json"), SiteJsonContext.Default.DictionaryStringDictionaryStringInt32)!;
        Assert.Equal(3, archive["2026"]["10"]);
        Assert.Equal(1, archive["2026"]["11"]);
        Assert.Equal(1, archive["2025"]["12"]);
        // 記事の日付は月のアーカイブへのリンク
        Assert.Contains("href=\"archive/2026/11\"", Body(Read(prod, "posts/jst-november.html")));
    }

    [Fact]
    public void WorldPagesUseTheUuidPathTheMasterNameAndTheVrchatLinkAndGatherPhotosAcrossPosts()
    {
        var (f, prod, _) = Build(fx => fx.AddPost("third-post", "三番目の記事", new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(9)), world: BuildFixture.World1, photos: 3, photoOffset: 60), perPage: 3);
        using var _ = f;
        // world1 の写真: first-post の 2 枚 + third-post の 3 枚 = 5 枚（perPage 3 で 2 ページ）
        var page1 = Read(prod, $"worlds/{Uuid1}.html");
        var body = Body(page1);
        Assert.Matches(@"<h1 class=""album-heading""><a class=""album-external"" href=""https://vrchat\.com/home/world/" + BuildFixture.World1 + @"/info"" [^>]*>" + Regex.Escape(BuildFixture.World1Name) + "</a></h1>", body);   // 名前を押すと VRChat のワールドページ
        Assert.Contains("rel=\"noopener noreferrer nofollow\"", body);
        Assert.Equal(3, Regex.Matches(body, "<li class=\"album-card album-card--photo\"").Count);
        Assert.Contains($"rel=\"next\" href=\"worlds/{Uuid1}/page/2\"", body);
        Assert.True(Exists(prod, $"worlds/{Uuid1}/page/2.html"));
        Assert.Equal(2, Regex.Matches(Body(Read(prod, $"worlds/{Uuid1}/page/2.html")), "<li class=\"album-card album-card--photo\"").Count);
        Assert.Contains($"{BuildFixture.World1Name} の写真", Head(page1));
        Assert.DoesNotContain("撮影時の名前", page1);
        using var world = JsonDocument.Parse(Read(prod, $"data/worlds/{Uuid1}.json"));
        Assert.Equal(BuildFixture.World1, world.RootElement.GetProperty("id").GetString());
        Assert.Equal(5, world.RootElement.GetProperty("photos").GetArrayLength());
        Assert.Equal("first-post", world.RootElement.GetProperty("photos")[0].GetProperty("slug").GetString());   // 新しい記事の順
        // 一覧: 写真の多い順、各ワールドの href は UUID
        var list = Body(Read(prod, "worlds.html"));
        Assert.True(list.IndexOf($"href=\"worlds/{Uuid1}\"", StringComparison.Ordinal) < list.IndexOf($"href=\"worlds/{Uuid2}\"", StringComparison.Ordinal));
        Assert.Contains("5 枚", list);
        Assert.DoesNotContain("wrld_", list);                          // URL に wrld_ は出ない
    }

    [Fact]
    public void WorldNamesAndVrchatLinksAreInTheStaticHtmlWithoutScript()
    {
        // JS が無くても見える: ワールド名と、そのリンク（VRChat の world ページ）・ワールドの画像が、ビルドの静的 HTML の本文（main.entry-static）の中にある
        var (_, prod, _) = Build();
        var vrchat = $"https://vrchat.com/home/world/{BuildFixture.World1}/info";

        // 記事: 写真のキャプション（figcaption）の中で、ワールド名そのものが、サイト内のワールドのページへのリンク（VRChat へは、そのページの名前から）
        var post = Body(Read(prod, "posts/first-post.html"));
        Assert.Matches(@"<figcaption>[\s\S]*?<a class=""album-world-link"" href=""worlds/" + Uuid1 + @""">" + Regex.Escape(BuildFixture.World1Name) + "</a>", post);
        Assert.DoesNotContain(">VRChat</a>", post);

        // ワールド一覧: 本文の中に、ワールド名のリンク
        var worlds = Body(Read(prod, "worlds.html"));
        Assert.Matches(@"<a [^>]*href=""[^""]*worlds/" + Uuid1 + @"[^""]*""[^>]*>[\s\S]*?" + Regex.Escape(BuildFixture.World1Name) + @"[\s\S]*?</a>", worlds);

        // ワールド一覧: 画像のあるワールドは R2 のワールドの画像（512 幅）、無いワールドは撮った写真
        Assert.Contains("src=\"" + ImageHost + "/" + BuildFixture.World1ImageKey + "_512.webp\"", worlds);
        Assert.DoesNotContain("vrchat.cloud", worlds);

        // ワールドページ: 先頭にワールドの画像（VRChat へのリンク）、その下にワールド名（h1）と情報、その下に写真
        var world = Body(Read(prod, $"worlds/{Uuid1}.html"));
        var order = new[] { "class=\"album-world-hero\"", ">" + BuildFixture.World1Name + "</a></h1>", "album-world-info", "album-grid--photos" }
            .Select(x => world.IndexOf(x, StringComparison.Ordinal)).ToArray();
        Assert.True(order.All(i => i >= 0) && order.SequenceEqual(order.Order()), string.Join(",", order));
        Assert.Contains("src=\"" + ImageHost + "/" + BuildFixture.World1ImageKey + "_1024.webp\"", world);
        Assert.Contains("123,456", world);
        Assert.Contains("作者の名前", world);
        Assert.Contains("2026年10月26日時点の", world);
        Assert.Contains(vrchat, world);
        Assert.DoesNotContain("vrchat.cloud", world);
        // 画像も情報も無いワールドのページは、どちらも出さない
        var world2 = Body(Read(prod, $"worlds/{BuildFixture.World2["wrld_".Length..]}.html"));
        Assert.DoesNotContain("album-world-hero", world2);
        Assert.DoesNotContain("album-world-info", world2);
        // データ（JSON）にも同じ情報（起動後の Blazor の画面用）
        var data = JsonSerializer.Deserialize(Read(prod, $"data/worlds/{Uuid1}.json"), SiteJsonContext.Default.WorldPage)!;
        Assert.Equal((123456L, BuildFixture.World1ImageKey), (data.Stats!.Visits, data.Image!.Key));
    }

    [Fact]
    public void TheOutfitNamesAreInTheCaptionAsLinksAndTheAvatarNameIsNeverOutput()
    {
        // 服の名前はキャプション（figcaption）の中に、URL があればリンク（https・rel・新しいタブ）。アバター名は出さない（静的 HTML にも data にも）
        var (_, prod, _) = Build(fx => fx.AddPost("outfit-post", "服の記事", new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(9)), photos: 1, photoOffset: 50, avatar: "avatar-a"));
        var post = Body(Read(prod, "posts/outfit-post.html"));
        Assert.Matches(@"<figcaption>[\s\S]*?<span class=""album-caption-outfits"">服: [\s\S]*?<a class=""album-outfit-link"" href=""outfits/coat-red"">赤いコート</a>[\s\S]*?<a class=""album-outfit-link"" href=""outfits/boots"">靴</a>", post);

        // 服のページ: 名前を押すと BOOTH など（https・新しいタブ）。その服を着た写真が並ぶ。ページ・データ・一覧ができる
        var outfit = Body(Read(prod, "outfits/coat-red.html"));
        Assert.Matches(@"<h1 class=""album-heading"">\s*<a class=""album-external"" href=""https://booth\.pm/ja/items/1"" target=""_blank"" rel=""noopener noreferrer nofollow""[^>]*>赤いコート</a>\s*</h1>", outfit);
        // BOOTH の情報（画像・お店・公開日・更新日・タグ・説明・時点）。「商品ページを見る」のリンクは無い。情報と「この服を着て撮った写真です」の間は、空ける（クラス）
        Assert.Matches(@"<a class=""album-outfit-hero"" href=""https://booth\.pm/ja/items/1"" [^>]*>\s*<img src=""https://booth\.pximg\.net/x/i/1/thumb\.jpg""[^>]*referrerpolicy=""no-referrer""", outfit);
        Assert.Contains("<dt>お店</dt><dd>テスト店</dd>", outfit);
        Assert.Contains("<dt>公開日</dt><dd>2025年11月20日</dd>", outfit);
        Assert.Contains("<dt>更新日</dt><dd>2025年12月1日</dd>", outfit);
        Assert.Contains("<dt>タグ</dt><dd>コート、冬服</dd>", outfit);
        Assert.DoesNotContain("あたたかいコートです。", outfit);                                      // 前文は出さない（対応アバターと利用規約だけ）
        Assert.Contains("【対応アバター】", outfit);
        Assert.Contains("https://example.booth.pm/items/9", outfit);
        Assert.Contains("【利用規約】", outfit);
        Assert.Contains("https://example.com/terms", outfit);
        Assert.Contains("2026年10月26日時点の BOOTH の情報です。", outfit);
        Assert.DoesNotContain("商品ページを見る</a>", outfit);
        Assert.Contains("class=\"album-lead album-lead--after-info\">この服を着て撮った写真です（1 枚）。", outfit);
        Assert.Single(Regex.Matches(outfit, "<li class=\"album-card album-card--photo\""));
        Assert.Contains("href=\"posts/outfit-post\"", outfit);
        Assert.DoesNotContain("<a", Body(Read(prod, "outfits/boots.html")).Split("<h1", 2)[1].Split("</h1>", 2)[0]);   // 商品ページの URL が無い服は、名前をリンクにしない
        var list = Body(Read(prod, "outfits.html"));
        Assert.Contains("href=\"outfits/coat-red\"", list);
        Assert.Contains("テスト店・1 枚", list);
        Assert.Contains("<img src=\"https://booth.pximg.net/x/i/1/thumb.jpg\"", list);                                   // 一覧の画像は、BOOTH の商品の画像
        Assert.Single(Regex.Matches(list, "album-card-media--outfit"));                                              // 画像の無い服（靴）は、画像を出さない（自分の撮った写真は出さない）
        Assert.DoesNotContain("_640.webp", list);
        using var data = JsonDocument.Parse(Read(prod, "data/outfits/coat-red.json"));
        Assert.Equal("赤いコート", data.RootElement.GetProperty("name").GetString());
        Assert.Equal("https://booth.pximg.net/x/i/1/thumb.jpg", data.RootElement.GetProperty("thumbnail").GetString());
        Assert.Equal("冬服", data.RootElement.GetProperty("tags")[1].GetString());
        Assert.Equal("2026-10-26T12:00:00+09:00", data.RootElement.GetProperty("fetchedAt").GetString());
        Assert.DoesNotContain("\"note\"", Read(prod, "data/outfits/coat-red.json"));
        Assert.Equal("https://booth.pm/ja/items/1", data.RootElement.GetProperty("url").GetString());
        Assert.Equal(1, data.RootElement.GetProperty("photos").GetArrayLength());
        Assert.Contains("outfits/coat-red", Read(prod, "sitemap.xml"));
        foreach (var file in new[] { "posts/outfit-post.html", "data/posts/outfit-post.json" })
        {
            Assert.DoesNotContain("Avatar A", Read(prod, file));
            Assert.DoesNotContain("avatar-a", Read(prod, file));
        }
        // 服が無い写真には何も出さない（服の記事の 2 枚目以降と、既存の記事）
        Assert.DoesNotContain("album-caption-outfits", Read(prod, "posts/first-post.html"));
    }

    [Fact]
    public void AWorldNotInTheMasterStopsTheBuild()
    {
        var fixture = new BuildFixture();
        using var _ = fixture;
        fixture.AddPost("unregistered", "未登録のワールド", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9)), world: "wrld_11111111-1111-4111-8111-111111111111");
        var e = Assert.Throws<ContentException>(() => fixture.Run("production"));
        Assert.Contains(e.Problems, p => p.Contains("content/worlds.json にありません"));
    }

    [Fact]
    public void TagPagesListTheirPostsWithTheDisplayNameAndSkipUnusedTags()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var night = Read(prod, "tags/night.html");
        Assert.Contains("タグ: 夜景", Body(night));
        Assert.Contains("最初の記事", Body(night));
        Assert.DoesNotContain("二番目の記事", Body(night));
        Assert.Contains("タグ: 夜景 | Fara Photo Album", night);
        var list = Body(Read(prod, "tags.html"));
        Assert.Contains("href=\"tags/sample\"", list);
        Assert.DoesNotContain("使われないタグ", Read(prod, "tags.html"));
    }

    [Fact]
    public void ListPagesAreNoindexWhenEveryPostInThemIsNoindexOrThereAreNone()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        // second-post だけの世界（world2）のページ・一覧の中身がすべて noindex → noindex（sitemap にも入らない）
        Assert.True(IsNoindex(Read(prod, $"worlds/{Uuid2}.html")));
        Assert.False(IsNoindex(Read(prod, $"worlds/{Uuid1}.html")));
        Assert.False(IsNoindex(Read(prod, "index.html")));
        Assert.False(IsNoindex(Read(prod, "tags/night.html")));
        var sitemap = Read(prod, "sitemap.xml");
        Assert.DoesNotContain(Uuid2, sitemap);
        Assert.Contains(Uuid1, sitemap);
        Assert.Contains("/archive/2026/10", sitemap);
        // 記事が 0 件のサイト: すべて noindex
        var (e, eProd, _) = Build(defaults: false);
        using (e)
        {
            Assert.True(IsNoindex(Read(eProd, "index.html")));
            Assert.True(IsNoindex(Read(eProd, "tags.html")));
            Assert.True(IsNoindex(Read(eProd, "worlds.html")));
        }
    }

    [Fact]
    public void TheFeedIsValidRssWithTheLatest20IndexablePostsAndTheEnvironmentOrigin()
    {
        var (f, prod, dev) = Build(fx =>
        {
            for (var i = 0; i < 25; i++) fx.AddPost("feed-" + i, "フィード " + i, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(9)).AddDays(i % 20).AddHours(i), tags: ["night"], photoOffset: 100 + i);
        });
        using var _ = f;
        var doc = XDocument.Parse(Read(prod, "feed.xml"));
        var channel = doc.Root!.Element("channel")!;
        Assert.Equal("2.0", doc.Root.Attribute("version")!.Value);
        Assert.Equal("Fara Photo Album", channel.Element("title")!.Value);
        Assert.Equal("https://vrc-album.fara-labs.com/", channel.Element("link")!.Value);
        var items = channel.Elements("item").ToList();
        Assert.Equal(20, items.Count);
        Assert.DoesNotContain(items, i => i.Element("link")!.Value.Contains("second-post"));          // noindex の記事は入れない
        Assert.Equal("https://vrc-album.fara-labs.com/posts/first-post", items[0].Element("link")!.Value);
        Assert.Equal(items[0].Element("link")!.Value, items[0].Element("guid")!.Value);
        Assert.Matches(@"^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} \+0900$", items[0].Element("pubDate")!.Value);
        Assert.Equal("Sat, 24 Oct 2026 20:00:00 +0900", items[0].Element("pubDate")!.Value);
        var enclosure = items[0].Element("enclosure")!;
        Assert.StartsWith(ImageHost + "/2026-10/", enclosure.Attribute("url")!.Value);
        Assert.EndsWith("_og.jpg", enclosure.Attribute("url")!.Value);
        Assert.Equal("image/jpeg", enclosure.Attribute("type")!.Value);
        Assert.Contains("最初の記事", Read(prod, "feed.xml"));                                          // 日本語が文字参照になっていない
        var devDoc = XDocument.Parse(Read(dev, "feed.xml"));
        Assert.StartsWith("https://vrc-album-dev.fara-labs.com/", devDoc.Root!.Element("channel")!.Element("link")!.Value);
    }

    [Fact]
    public void PrivateValuesInTheSourceNeverReachTheOutput()
    {
        // 元ファイル名・インスタンス ID などが content/ に紛れ込んでも、公開物には出さない。
        // PostSource は未知の項目を拒否する（ContentLoader）ので、ビルド自体が止まる
        var fixture = new BuildFixture();
        using var _ = fixture;
        File.WriteAllText(Path.Combine(fixture.Repo, "content", "posts", "leak.json"),
            "{ \"schema\": 1, \"slug\": \"leak\", \"title\": \"t\", \"publishedAt\": \"2026-10-24T20:00:00+09:00\", \"body\": \"\", \"sourceName\": \"VRChat_2026-10-23.png\", \"instanceId\": \"12345~private\" }");
        Assert.Throws<ContentException>(() => fixture.Run("production"));
    }

    [Fact]
    public void ForbiddenValuesAreInNoOutputFile()
    {
        var (f, prod, dev) = Build();
        using var _ = f;
        foreach (var dir in new[] { prod, dev })
            foreach (var (name, text) in AllFiles(dir))
            {
                Assert.False(Regex.IsMatch(text, @"VRChat_\d{4}-\d{2}-\d{2}"), $"{name} に元ファイル名があります");
                Assert.False(Regex.IsMatch(text, @"wrld_[0-9a-f-]{36}:\d"), $"{name} にインスタンス ID があります");
                Assert.False(Regex.IsMatch(text, @"~private|~hidden|~friends|~region\(", RegexOptions.IgnoreCase), $"{name} にインスタンスの種類があります");
                Assert.False(Regex.IsMatch(text, @"\.png\b", RegexOptions.IgnoreCase), $"{name} に PNG のファイル名があります");
                Assert.False(Regex.IsMatch(text, @"<x:xmpmeta|<rdf:RDF|vrc:WorldID", RegexOptions.IgnoreCase), $"{name} に元の XMP があります");
            }
    }

    [Fact]
    public void ADescriptionIsTheSummaryOrTheStartOfTheBody()
    {
        var (f, prod, _) = Build(fx => fx.AddPost("with-summary", "要約あり", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(9)), summary: "明示した要約です。", photoOffset: 70));
        using var _ = f;
        Assert.Contains("<meta name=\"description\" content=\"明示した要約です。\" />", Head(Read(prod, "posts/with-summary.html")));
        Assert.Contains("<meta name=\"description\" content=\"二番目。\" />", Head(Read(prod, "posts/second-post.html")));
    }

    [Fact]
    public void OgImagesOnListPagesComeFromAPostCover()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        // _og.jpg を作るのはカバーだけ。一覧・タグ・ワールド・アーカイブは、載せる記事のカバーを使う
        foreach (var name in new[] { "index.html", "tags/night.html", $"worlds/{Uuid1}.html", "archive/2026/10.html" })
        {
            var og = Regex.Match(Read(prod, name), "<meta property=\"og:image\" content=\"([^\"]+)\"").Groups[1].Value;
            Assert.Matches(@"^https://vrc-photos\.fara-labs\.com/2026-10/\d{8}-\d{6}-\d{3}_[0-9a-f]{8}_og\.jpg$", og);
        }
    }

    [Fact]
    public void EveryStaticPageHasAMatchingBlazorRoute()
    {
        // 静的 HTML を出した URL に、アプリ側のルートが無いと、起動した途端に 404 の画面に置き換わってしまう
        var templates = typeof(FaraVrcAlbum.Web.App).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), inherit: false).Cast<Microsoft.AspNetCore.Components.RouteAttribute>())
            .Select(r => RouteRegex(r.Template))
            .ToList();
        var (f, prod, _) = Build(fx => fx.AddPost("third-post", "三番目の記事", new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(9)), world: BuildFixture.World1, photos: 3, photoOffset: 60), perPage: 2);
        using var _ = f;
        var paths = Directory.EnumerateFiles(prod, "*.html", SearchOption.AllDirectories)
            .Select(file => "/" + Path.GetRelativePath(prod, file).Replace('\\', '/')[..^".html".Length])
            .Where(p => p != "/404")
            .Select(p => p == "/index" ? "/" : p)
            .ToList();
        Assert.True(paths.Count > 15);
        foreach (var path in paths)
            Assert.True(templates.Any(t => t.IsMatch(path)), $"{path} に一致する @page のルートがありません");
    }

    [Fact]
    public void EveryStaticPageHasExactlyOneH1()
    {
        // 見出しの階層（h1 は 1 つ）。視覚的に隠した h1 と、画面の題名の h1 が重なると、支援技術で二重に読まれる（報告ページで起きた）
        var (f, prod, _) = Build(fx => fx.AddPost("third-post", "三番目の記事", new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(9)), world: BuildFixture.World1, photos: 3, photoOffset: 60), perPage: 2);
        using var _ = f;
        // 404.html は kit の build-pages が作る（この test の対象外。kit 側の見出しの扱いは報告に残す）
        var files = Directory.EnumerateFiles(prod, "*.html", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != "404.html").ToList();
        Assert.True(files.Count > 15);
        foreach (var file in files)
        {
            var count = Regex.Matches(File.ReadAllText(file), @"<h1[\s>]").Count;
            Assert.True(count == 1, $"{Path.GetRelativePath(prod, file)} の h1 が {count} 個です（1 つにする）");
        }
    }

    /// <summary>@page のテンプレート（/archive/{Year:int}/{Month:int}、/tags/{Slug} など）に一致する正規表現。</summary>
    private static Regex RouteRegex(string template)
    {
        var pattern = Regex.Escape(template);                                        // { は \{ になる（} はそのまま）
        pattern = Regex.Replace(pattern, @"\\\{[A-Za-z]+:int\}", @"\d+");
        pattern = Regex.Replace(pattern, @"\\\{[A-Za-z]+\}", "[^/]+");
        return new Regex("^" + pattern + "$");
    }

    [Fact]
    public void TheOutputFolderMustNotBeTheTemplateFolder()
    {
        var f = new BuildFixture();
        using var _ = f;
        var dir = Path.GetDirectoryName(f.TemplatePath)!;
        Assert.Throws<ArgumentException>(() => SiteBuilder.Run(new BuildOptions(f.Repo, f.TemplatePath, dir, "production", Path.Combine(RepoRoot.Find(), "external", "fara-blazor-component"))));
        Assert.Throws<ArgumentException>(() => SiteBuilder.Run(new BuildOptions(f.Repo, f.TemplatePath, Path.Combine(Path.GetTempPath(), "x"), "staging", Path.Combine(RepoRoot.Find(), "external", "fara-blazor-component"))));
    }

    [Fact]
    public void BuildingWithoutTheContentFolderFailsInsteadOfMakingAnEmptySite()
    {
        // 本物の content/ は Git に入れない。無いまま空のサイトを作って公開してしまわないこと
        var f = new BuildFixture();
        using var _ = f;
        Directory.Delete(Path.Combine(f.Repo, "content"), recursive: true);
        var outDir = Path.Combine(Path.GetTempPath(), "album-build-out-" + Guid.NewGuid().ToString("N")[..8]);
        var ex = Assert.Throws<DirectoryNotFoundException>(() =>
            SiteBuilder.Run(new BuildOptions(f.Repo, f.TemplatePath, outDir, "production", Path.Combine(RepoRoot.Find(), "external", "fara-blazor-component"))));
        Assert.Contains("記事の中身がありません", ex.Message);
        Assert.False(Directory.Exists(outDir));                                                             // 何も作らない
    }

    [Fact]
    public void TheDevelopOutputUsesTheSameR2PhotosAsProduction()
    {
        var (f, prod, dev) = Build();
        using var _ = f;
        // 確認用も本番と同じ写真（R2 の画像ドメイン）を参照する。確認用の合成画像は作らず、出力にも無い
        foreach (var output in new[] { prod, dev })
        {
            using (var site = JsonDocument.Parse(Read(output, "data/site.json")))
                Assert.Equal(ImageHost + "/", site.RootElement.GetProperty("imageBaseUrl").GetString());
            Assert.Contains("src=\"" + ImageHost + "/2026-10/", Body(Read(output, "posts/first-post.html")));
            Assert.False(Directory.Exists(Path.Combine(output, "images", "dev-photos")));
            Assert.DoesNotContain(AllFiles(output), file => file.Text.Contains("dev-photos"));
        }
        // og:image・JSON-LD・フィード・CSP は、確認用でも本番と同じ画像ドメイン
        var html = Read(dev, "posts/first-post.html");
        Assert.Contains("<meta property=\"og:image\" content=\"" + ImageHost + "/2026-10/", html);
        Assert.Contains(ImageHost, MustRule(Read(dev, "_headers"), "/*"));
    }

    [Fact]
    public void ThePostDataHasNeighboursAndRelatedAndTheStaticPageShowsThem()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        using (var first = JsonDocument.Parse(Read(prod, "data/posts/first-post.json")))
        {
            Assert.Equal("second-post", first.RootElement.GetProperty("older").GetProperty("slug").GetString());       // 1 つ古い記事
            Assert.False(first.RootElement.TryGetProperty("newer", out var absent));                                            // 最新には次が無い
            Assert.Equal(["second-post"], first.RootElement.GetProperty("related").EnumerateArray().Select(r => r.GetProperty("slug").GetString()));   // 同じタグ sample
        }
        using (var second = JsonDocument.Parse(Read(prod, "data/posts/second-post.json")))
            Assert.Equal("first-post", second.RootElement.GetProperty("newer").GetProperty("slug").GetString());
        var body = Body(Read(prod, "posts/first-post.html"));
        Assert.Contains("rel=\"prev\" href=\"posts/second-post\"", body);
        Assert.DoesNotContain("rel=\"next\"", body);
        Assert.Contains("class=\"album-related\"", body);
        Assert.Contains("class=\"album-breadcrumb\"", body);
    }

    [Fact]
    public void AScheduledPostIsNeitherANeighbourNorRelatedInProductionButIsInDevelop()
    {
        var (f, prod, dev) = Build(fx => fx.AddPost("scheduled", "予約の記事", new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.FromHours(9)), photoOffset: 30, tags: ["sample"]));
        using var _ = f;

        Assert.DoesNotContain("scheduled", Read(prod, "data/posts/first-post.json"));
        Assert.DoesNotContain("予約の記事", Body(Read(prod, "posts/first-post.html")));
        using var devFirst = JsonDocument.Parse(Read(dev, "data/posts/first-post.json"));
        Assert.Equal("scheduled", devFirst.RootElement.GetProperty("newer").GetProperty("slug").GetString());           // 確認用では、予約の記事は隣の記事（非公開は、どこにも出ない）
    }

    [Fact]
    public void AnUnpublishedPostIsShownInNeitherEnvironmentNorAsANeighbour()
    {
        var (f, prod, dev) = Build();
        using var _ = f;

        foreach (var site in new[] { prod, dev })
        {
            Assert.False(File.Exists(Path.Combine(site, "posts/draft-post.html")));
            Assert.False(File.Exists(Path.Combine(site, "data/posts/draft-post.json")));
            Assert.DoesNotContain("draft-post", Read(site, "index.html"));
            Assert.DoesNotContain("draft-post", Read(site, "data/posts-index.json"));
            Assert.DoesNotContain("draft-post", Read(site, "data/posts/first-post.json"));                              // 隣の記事にも出ない
        }
    }

    [Fact]
    public void TheJsonLdListsTheCoverAndEveryPhotoAsImageObjects()
    {
        var (f, prod, _) = Build();
        using var _ = f;
        var html = Read(prod, "posts/first-post.html");
        var ld = Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        using var json = JsonDocument.Parse(ld);

        var images = json.RootElement.GetProperty("image").EnumerateArray().ToList();
        Assert.Equal(3, images.Count);                                         // カバー（_og.jpg）+ 写真 2 枚
        Assert.EndsWith("_og.jpg", images[0].GetString());
        Assert.Equal("ImageObject", images[1].GetProperty("@type").GetString());
        Assert.StartsWith(ImageHost + "/2026-10/", images[1].GetProperty("contentUrl").GetString());
        Assert.EndsWith("_2048.webp", images[1].GetProperty("contentUrl").GetString());
        Assert.EndsWith("_640.webp", images[1].GetProperty("thumbnailUrl").GetString());
        Assert.Equal(2048, images[1].GetProperty("width").GetInt32());
        Assert.Equal(1152, images[1].GetProperty("height").GetInt32());
        Assert.DoesNotContain("usr_", ld);
    }

    [Fact]
    public void RelatedPostsNeedAtLeastOneSharedTagOrWorldAndAreOrderedByHowMuchTheyShare()
    {
        // isolated: タグも世界も共有しない（スコア 0）→ 関連に出さない。tagged-only: night だけ共有（1）。both: night + 同じワールドを共有（2）→ 先に出る
        var (f, prod, _) = Build(fx =>
        {
            fx.AddPost("isolated", "孤立した記事", new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(9)), tags: ["unused"], photoOffset: 40);
            fx.AddPost("tagged-only", "タグだけ共有", new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(9)), tags: ["night"], photoOffset: 41);
            fx.AddPost("both", "タグもワールドも共有", new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.FromHours(9)), tags: ["night"], world: BuildFixture.World1, photoOffset: 42);
        }, now: new DateTimeOffset(2026, 10, 27, 0, 0, 0, TimeSpan.FromHours(9)));
        using var _ = f;

        using var first = JsonDocument.Parse(Read(prod, "data/posts/first-post.json"));
        var related = first.RootElement.GetProperty("related").EnumerateArray().Select(r => r.GetProperty("slug").GetString()).ToList();
        Assert.DoesNotContain("isolated", related);                                      // スコア 0 は出さない（> 0 の境界）
        Assert.Equal("both", related[0]);                                                 // 共有が多い（タグ + ワールド）ほうが、新しい tagged-only より先
        Assert.Contains("tagged-only", related);
        using var isolated = JsonDocument.Parse(Read(prod, "data/posts/isolated.json"));
        Assert.False(isolated.RootElement.TryGetProperty("related", out var none));      // 何も共有しない記事には、関連の項目ごと出さない
        Assert.DoesNotContain("album-related", Body(Read(prod, "posts/isolated.html")));
    }
}
