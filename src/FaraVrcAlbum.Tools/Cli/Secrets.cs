namespace FaraVrcAlbum.Tools.Cli;

/// <summary>
/// 表示から消す値の集まり（R2 の認証情報）。環境変数と、--repo で指したリポジトリの .env.local の両方を対象にする。
/// 値そのものは表示しない。エラーの表示・ファイル単位の失敗の表示は、必ずここを通す。
/// </summary>
public static class Secrets
{
    public static IReadOnlyList<string> Known(RepoContext? repo)
    {
        var values = new List<string>();
        foreach (var name in RepoContext.R2Variables.Concat(["CLOUDFLARE_API_TOKEN"]))
        {
            var fromEnv = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(fromEnv)) values.Add(fromEnv);
            if (repo?.Env(name) is { } fromFile) values.Add(fromFile);
        }
        return values;
    }

    public static string Redact(string text, RepoContext? repo) => Redactor.Redact(text, Known(repo));
}
