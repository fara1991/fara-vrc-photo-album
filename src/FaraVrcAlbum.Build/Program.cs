using FaraVrcAlbum.Build;

// 使い方:
//   dotnet run --project src/FaraVrcAlbum.Build -c Release --no-launch-profile -- \
//     --template <dotnet publish の wwwroot/index.html> --out <dotnet publish の wwwroot のコピー> --env production|develop \
//     [--repo .] [--kit external/fara-blazor-component] [--node node] [--content <記事の中身のフォルダ。省略はリポジトリの content>]
// --out はテンプレートと別のフォルダにする（kit の build-pages が、同じ場所への上書きを拒否する）。詳しくは SiteBuilder.cs。
try
{
    var values = ParseArgs(args);
    string Get(string key, string? fallback = null) =>
        values.TryGetValue(key, out var v) ? v : fallback ?? throw new ArgumentException($"--{key} を指定してください");

    var repo = Path.GetFullPath(Get("repo", "."));
    var kit = values.TryGetValue("kit", out var k) ? Path.GetFullPath(k) : Path.Combine(repo, "external", "fara-blazor-component");
    SiteBuilder.Run(new BuildOptions(
        Repo: repo,
        Template: Path.GetFullPath(Get("template")),
        Out: Path.GetFullPath(Get("out")),
        Env: Get("env"),
        KitPath: kit,
        Node: Get("node", "node"),
        ContentDir: values.TryGetValue("content", out var content) ? Path.GetFullPath(content) : null));       // ビルドは、設定ファイルを暗黙には読まない（CI の結果を変えないため）
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or DirectoryNotFoundException or InvalidOperationException or InvalidDataException or FaraVrcAlbum.Content.ContentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static Dictionary<string, string> ParseArgs(string[] args)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            throw new ArgumentException($"引数が不正です: {args[i]}");
        values[args[i][2..]] = args[i + 1];
    }
    return values;
}
