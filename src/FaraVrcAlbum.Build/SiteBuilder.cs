using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Build;

/// <summary>
/// Build の入力。Out は dotnet publish の wwwroot のコピー（Template とは別のフォルダ）。
/// Now は「今」（本番で予約公開の記事を出すかどうかの基準。省略は実行時の時刻。テストは固定の時刻を渡す）。
/// </summary>
public sealed record BuildOptions(string Repo, string Template, string Out, string Env, string KitPath, string Node = "node", DateTimeOffset? Now = null, string? ContentDir = null);

/// <summary>
/// 公開用の静的ファイル一式を作る。
///   data/*.json … アプリ（WebAssembly）が起動時・ページごとに読む（site・posts-index・archive・posts/{slug}・tags・worlds・worlds/{uuid}）
///   本文入りの静的 HTML（一覧・記事・アーカイブ・タグ・ワールド・privacy・report）・404.html・robots.txt・sitemap.xml（本番のみ）・_headers（CSP。確認用は X-Robots-Tag: noindex）・_redirects … kit の build/build-pages.mjs が書く
///   feed.xml … RSS 2.0（最新 20 件）
///   _headers の末尾に /data/* の X-Robots-Tag: noindex を足す（本番でも noindex にするため。確認用は全体が noindex）
/// 本番用（--env production）と確認用（--env develop）で内容が違う（sitemap・noindex・canonical・予約公開の記事）。取り違えは verify-site.mjs が止める。
/// 静的 HTML・data/ には人の名前を入れない（撮影者・写っている人は持たない。ワールド名は出す）。
/// </summary>
public static class SiteBuilder
{
    public static readonly string[] Environments = ["production", "develop"];

    public static void Run(BuildOptions options)
    {
        if (!Environments.Contains(options.Env)) throw new ArgumentException($"--env は production か develop です（{options.Env}）");
        if (!File.Exists(options.Template)) throw new FileNotFoundException($"テンプレートがありません: {options.Template}（dotnet publish の wwwroot/index.html）");
        var kitScript = Path.Combine(options.KitPath, "build", "build-pages.mjs");
        if (!File.Exists(kitScript)) throw new FileNotFoundException($"fara-blazor-component がありません: {kitScript}（git submodule update --init）");
        if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(options.Template)!), Path.GetFullPath(options.Out), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--out はテンプレート（dotnet publish の wwwroot）とは別のフォルダにしてください（コピーを作ってから実行）");

        var settings = LoadSettings(Path.Combine(options.Repo, "appsettings.json"), options.Env);
        var now = options.Now ?? DateTimeOffset.UtcNow;
        var contentDir = options.ContentDir ?? Path.Combine(options.Repo, "content");
        // 本物の content/ は Git に入れない。無いまま空のサイトを作って公開してしまわないよう、ここで止める
        if (!Directory.Exists(contentDir)) throw new DirectoryNotFoundException($"記事の中身がありません: {contentDir}（本物の content/ は Git に入れていません。手元のフォルダを置くか、--content で指定してください）");
        var content = ContentLoader.Load(contentDir).ForEnvironment(options.Env, now);
        var model = new SiteModel(settings, content);
        var siteConfig = SiteConfigFile.Read(Path.Combine(options.Repo, "tools", "site.json"));

        Directory.CreateDirectory(options.Out);
        var dataDir = Path.Combine(options.Out, "data");
        if (Directory.Exists(dataDir)) Directory.Delete(dataDir, recursive: true);
        DataWriter.Write(dataDir, model);
        var work = Path.Combine(options.Repo, "obj", "build-pages", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var pagesFile = Path.Combine(work, "pages.json");
            using (var renderer = new PageRenderer())
            {
                File.WriteAllText(pagesFile, PageCatalog.BuildPagesJson(model, renderer), new UTF8Encoding(false));
            }
            var siteFile = Path.Combine(work, "site.json");
            File.WriteAllText(siteFile, KitSiteConfig.Build(siteConfig, settings), new UTF8Encoding(false));

            RunKit(options, kitScript, siteFile, pagesFile);
            HeadersFile.AppendNoindexRules(Path.Combine(options.Out, "_headers"));
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* 作業フォルダが消せなくても、出力には影響しない */ }
        }
        FeedWriter.Write(Path.Combine(options.Out, "feed.xml"), model, options.Env == "production" ? siteConfig.Origin : siteConfig.DevOrigin);

        Console.WriteLine($"{options.Env}: 記事 {model.Posts.Count} 件、一覧 {SiteRules.PageCount(model.Posts.Count, settings.PostsPerPage)} ページ、月 {model.Months.Count}、タグ {model.PostsByTag.Count}、ワールド {model.PhotosByWorld.Count} を書き出しました（{options.Out}）");
    }

    /// <summary>appsettings.json を読む。画像は確認用と本番で同じ R2 を見る（確認用だけ別の場所は使わない）。</summary>
    public static SiteSettings LoadSettings(string path, string env = "production")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        string? Optional(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : null;
        string Text(string name) => Optional(name) ?? throw new InvalidDataException($"appsettings.json に {name}（文字列）がありません");
        var perPage = root.TryGetProperty("PostsPerPage", out var p) && p.TryGetInt32(out var n) ? n : 12;
        // 範囲外はビルドを止める（入力の誤りを公開の前に気づかせる）。Web（AlbumStore）は、読んだ data/site.json の範囲外を既定の 12 に戻して画面を開く（意図の違い）
        if (perPage < 1 || perPage > 100) throw new InvalidDataException($"PostsPerPage は 1〜100 です（{perPage}）");
        // 画像は確認用も本番も R2（ImageBaseUrl）。確認用は本番と同じ写真を参照する（確認用の合成画像は作らない）
        var publicImageBase = SiteRules.NormalizeImageBaseUrl(Text("ImageBaseUrl"));
        return new SiteSettings
        {
            SiteName = Text("SiteName"),
            Author = Text("Author"),
            ImageBaseUrl = publicImageBase,
            PublicImageBaseUrl = publicImageBase,
            PostsPerPage = perPage,
        };
    }

    private static void RunKit(BuildOptions options, string script, string site, string pages)
    {
        var info = new ProcessStartInfo(options.Node)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { script, "--site", site, "--pages", pages, "--template", options.Template, "--out", options.Out, "--env", options.Env })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{options.Node} を起動できません");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"fara-blazor-component の build-pages.mjs が失敗しました（終了コード {process.ExitCode}）:\n{stderr.Result}{stdout.Result}");
        Console.WriteLine(stdout.Result.Trim());
    }
}
