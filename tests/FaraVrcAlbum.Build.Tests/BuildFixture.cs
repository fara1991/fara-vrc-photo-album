using System.Text;
using FaraVrcAlbum.Build;

namespace FaraVrcAlbum.Build.Tests;

/// <summary>リポジトリのルート（FaraVrcAlbum.sln があるフォルダ）。</summary>
internal static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FaraVrcAlbum.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FaraVrcAlbum.sln が見つかりません");
    }
}

/// <summary>
/// 使い捨てのリポジトリ（appsettings.json・tools/site.json・content/）を作って、Build を実際に実行する（node と external/fara-site-kit が要る）。
/// テンプレートは、本物の src/FaraVrcAlbum.Web/wwwroot/index.html（SEO の区切りが空のもの。dotnet publish の出力と同じ区切り）。
/// 「今」は固定（2026-10-25 JST）。人物は持たない（撮影者・写っている人の項目は無い）。
/// </summary>
public sealed class BuildFixture : IDisposable
{
    public const string Key1 = "2026-10/20261023-213312-345_a1b2c3d4";
    public const string Key2 = "2026-10/20261023-214001-120_0f1e2d3c";

    /// <summary>ワールドのマスタに入っている ID（content/worlds.json）。UUID は wrld_ を除いた部分が URL になる。</summary>
    public const string World1 = "wrld_00000000-0000-4000-8000-000000000001";
    public const string World2 = "wrld_00000000-0000-4000-8000-000000000002";
    public const string World1Name = "マスタのワールド名 1";

    /// <summary>World1 の画像（R2 のキー）。World2 には画像を付けない（撮った写真で代わりに見せる）。</summary>
    public const string World1ImageKey = "worlds/00000000-0000-4000-8000-000000000001/file_00000000-0000-4000-8000-0000000000aa_3";

    public static readonly DateTimeOffset Now = new(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "album-build-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _repoRoot = RepoRoot.Find();

    public string Repo { get; }

    public BuildFixture(string imageBaseUrl = "https://vrc-photos.fara-labs.com/", int perPage = 12)
    {
        Repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(Repo, "content", "posts"));
        Directory.CreateDirectory(Path.Combine(Repo, "tools"));
        File.Copy(Path.Combine(_repoRoot, "tools", "site.json"), Path.Combine(Repo, "tools", "site.json"));
        File.WriteAllText(Path.Combine(Repo, "appsettings.json"),
            "{ \"SiteName\": \"Fara Photo Album\", \"Author\": \"Fara\", \"ImageBaseUrl\": \"" + imageBaseUrl + "\", \"PostsPerPage\": " + perPage
            + " }");
        File.WriteAllText(Path.Combine(Repo, "content", "worlds.json"),
            "{ \"" + World1 + "\": { \"name\": \"" + World1Name + "\", \"aliases\": [\"旧名\"], \"image\": { \"key\": \"" + World1ImageKey + "\", \"w\": 1024, \"h\": 768 } }, \"" + World2 + "\": { \"name\": \"マスタのワールド名 2\" } }");
        File.WriteAllText(Path.Combine(Repo, "content", "world-stats.json"),
            "{ \"fetchedAt\": \"2026-10-26T12:00:00+09:00\", \"worlds\": { \"" + World1 + "\": { \"authorName\": \"作者の名前\", \"visits\": 123456, \"favorites\": 4321, \"capacity\": 80, \"recommendedCapacity\": 40 } } }");
        File.WriteAllText(Path.Combine(Repo, "content", "tags.json"),
            "{ \"sample\": { \"name\": \"サンプル\" }, \"night\": { \"name\": \"夜景\" }, \"unused\": { \"name\": \"使われないタグ\" } }");
        File.WriteAllText(Path.Combine(Repo, "content", "outfits.json"),
            "{ \"coat-red\": { \"name\": \"赤いコート\", \"shop\": \"テスト店\", \"url\": \"https://booth.pm/ja/items/1\", \"thumbnail\": \"https://booth.pximg.net/x/i/1/thumb.jpg\", \"description\": \"あたたかいコートです。\\n\\n対応アバター\\n■ Alpha\\nhttps://example.booth.pm/items/9\\n■ Beta\\nhttps://example.booth.pm/items/8\\n\\n■利用規約\\n再配布禁止\\nhttps://example.com/terms\", \"tags\": [\"コート\", \"冬服\"], \"publishedAt\": \"2025-11-20T10:00:00+09:00\", \"updatedAt\": \"2025-12-01T10:00:00+09:00\", \"fetchedAt\": \"2026-10-26T12:00:00+09:00\" }, \"boots\": { \"name\": \"靴\" } }");
        File.WriteAllText(Path.Combine(Repo, "content", "avatars.json"),
            "{ \"avatar-a\": { \"name\": \"Avatar A\", \"aliases\": [], \"outfitIds\": [\"coat-red\", \"boots\"] } }");
    }

    public string TemplatePath => Path.Combine(_repoRoot, "src", "FaraVrcAlbum.Web", "wwwroot", "index.html");

    /// <summary>記事を足す。photos 枚の写真（key は番号から作る）を持ち、全部に world を付けられる。撮影日時は書かない。</summary>
    public void AddPost(string slug, string title, DateTimeOffset published, string body = "本文です。", bool draft = false, bool noindex = false, string? summary = null,
        int photos = 1, string[]? tags = null, string? world = null, string worldSnapshotName = "撮影時の名前", int photoOffset = 0, string? avatar = null, string[]? outfits = null)
    {
        var photoJson = new List<string>();
        for (var i = 0; i < photos; i++)
        {
            var n = i + photoOffset;
            var key = $"2026-10/20261023-2133{n % 60:00}-{n % 1000:000}_{(n + 1):x8}";
            var extra = "";
            if (avatar is not null) extra += ", \"avatar\": \"" + avatar + "\"";
            if (outfits is not null) extra += ", \"outfits\": [" + string.Join(", ", outfits.Select(o => "\"" + o + "\"")) + "]";
            if (world is not null) extra += ", \"world\": { \"id\": \"" + world + "\", \"name\": \"" + worldSnapshotName + "\" }";
            photoJson.Add("{ \"key\": \"" + key + "\", \"w\": 2048, \"h\": 1152, \"color\": \"1b2a3c\", \"caption\": \"写真 " + n + "\"" + extra + " }");
        }
        var cover = photos > 0 ? ", \"cover\": \"2026-10/20261023-" + $"2133{photoOffset % 60:00}-{photoOffset % 1000:000}_{(photoOffset + 1):x8}" + "\"" : "";
        var tagJson = string.Join(", ", (tags ?? ["sample"]).Select(t => "\"" + t + "\""));
        var json = "{ \"schema\": 1, \"slug\": \"" + slug + "\", \"title\": \"" + title + "\", \"publishedAt\": \"" + published.ToString("yyyy-MM-dd'T'HH:mm:sszzz") + "\""
            + ", \"draft\": " + (draft ? "true" : "false") + ", \"noindex\": " + (noindex ? "true" : "false")
            + (summary is null ? "" : ", \"summary\": \"" + summary + "\"") + cover
            + ", \"tags\": [" + tagJson + "], \"body\": " + System.Text.Json.JsonSerializer.Serialize(body)
            + ", \"photos\": [" + string.Join(", ", photoJson) + "] }";
        File.WriteAllText(Path.Combine(Repo, "content", "posts", slug + ".json"), json, new UTF8Encoding(false));
    }

    /// <summary>Build を実行して、出力フォルダを返す（テンプレートは別フォルダにあるので、出力先は空のフォルダ）。now を省略すると <see cref="Now"/>。</summary>
    public string Run(string env, DateTimeOffset? now = null, string? contentDir = null)
    {
        var template = Path.Combine(_root, "template", "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(template)!);
        File.Copy(TemplatePath, template, overwrite: true);
        var output = Path.Combine(_root, "out-" + env);
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        // dotnet publish の出力の代わり: 起動に要る静的ファイルのうち、このテストが見るもの（data/ は Build が書く）
        SiteBuilder.Run(new BuildOptions(Repo, template, output, env, Path.Combine(_repoRoot, "external", "fara-site-kit"), Now: now ?? Now, ContentDir: contentDir));
        return output;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
