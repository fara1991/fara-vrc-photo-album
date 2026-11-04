using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>公開先。確認用（dev。develop 環境）と本番。</summary>
public enum PublishTarget { Develop, Production }

/// <summary>公開先ごとの、公開済みかどうか（今の記事の中身と、最後に公開した内容の比較）。</summary>
public enum PublishState
{
    /// <summary>まだ公開していない（このアプリからは）。</summary>
    Never,

    /// <summary>最後に公開した内容と、今の記事の中身が同じ。</summary>
    UpToDate,

    /// <summary>公開したあとに、記事の中身が変わっている。</summary>
    Changed,
}

/// <summary>最後に公開した内容の記録。ContentHash は記事の中身（JSON 一式）の SHA-256。</summary>
public sealed record PublishRecord(string ContentHash, DateTimeOffset At, string? Commit);

/// <summary>
/// 公開の記録（local/publish-state.json。Git 管理外）。公開先ごとに、最後に公開した記事の中身の内容と日時を持つ。
/// 読めなければ「まだ公開していない」として扱う（壊れたファイルで画面を止めない）。
/// </summary>
public sealed class PublishLog(string path)
{
    public string Path { get; } = path;

    public PublishRecord? Get(PublishTarget target)
    {
        try
        {
            if (!File.Exists(Path) || JsonNode.Parse(File.ReadAllText(Path)) is not JsonObject root) return null;
            if (root[Key(target)] is not JsonObject node) return null;
            var hash = node["contentHash"]?.GetValue<string>();
            var at = node["at"]?.GetValue<string>();
            if (string.IsNullOrEmpty(hash) || !DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, out var when)) return null;
            return new PublishRecord(hash, when, node["commit"]?.GetValue<string>());
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or IOException)
        {
            return null;
        }
    }

    public void Set(PublishTarget target, PublishRecord record)
    {
        JsonObject root;
        try
        {
            root = File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path)) as JsonObject ?? new JsonObject() : new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }
        root[Key(target)] = new JsonObject
        {
            ["contentHash"] = record.ContentHash,
            ["at"] = record.At.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            ["commit"] = record.Commit,
        };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        File.WriteAllText(Path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string Key(PublishTarget target) => target == PublishTarget.Production ? "production" : "develop";
}

/// <summary>外のプログラム（node・dotnet・git）を動かす口。テストでは偽物に差し替える。出力は 1 行ずつ onLine に渡す。</summary>
public interface IProcessRunner
{
    Task<int> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, IReadOnlyDictionary<string, string> environment, Action<string> onLine, CancellationToken ct);
}

/// <summary>実際にプログラムを動かす。見つからないプログラムは、名前を示す文にする。</summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>
    /// 子プロセスの環境を決める。親の環境変数は継承されるので、ALBUM_ALLOW_SAMPLE（CI 用のサンプル記事の関門を外す印）は、必ず外す
    /// （アプリの公開で、サンプルの記事が本番に出ないように）。
    /// </summary>
    public static void ApplyEnvironment(ProcessStartInfo info, IReadOnlyDictionary<string, string> environment)
    {
        foreach (var (name, value) in environment) info.Environment[name] = value;
        info.Environment.Remove("ALBUM_ALLOW_SAMPLE");
    }

    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> args, string workingDirectory, IReadOnlyDictionary<string, string> environment, Action<string> onLine, CancellationToken ct)
    {
        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        ApplyEnvironment(info, environment);

        using var process = new Process { StartInfo = info };
        var gate = new object();
        void Line(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (gate) onLine(e.Data);
        }
        process.OutputDataReceived += Line;
        process.ErrorDataReceived += Line;
        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"「{fileName}」を実行できません（インストールされているか、PATH に入っているか確認してください）");
        }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        process.WaitForExit();                                                         // 出力の読み取りの完了を待つ
        return process.ExitCode;
    }
}

/// <summary>
/// アプリからのサイトの公開（ビルドと Cloudflare Workers への公開）。公開の手順は <c>node tools/site.mjs deploy &lt;環境&gt;</c>（CI の Deploy と同じビルド・検査）。
/// 記事の中身は、設定のフォルダ（既定はリポジトリの content）から読む（環境変数 ALBUM_CONTENT_DIR）。
/// <list type="bullet">
/// <item>本番は、確認用（dev）に<b>今の記事の中身</b>を公開済みのときだけ。違う内容を、確認せずに本番へ出さないため。</item>
/// <item>本番の公開の前に、Worker の変数 MAINTENANCE_MODE が notice か true かを確かめる（CI の本番の公開と同じ決まり。require-maintenance.mjs）。</item>
/// <item>公開に成功したときだけ、公開の記録（PublishLog）に、そのときの記事の中身の内容を残す。</item>
/// <item>Cloudflare のトークンは、プログラムの環境変数でだけ渡す（引数・ログには出さない。出力に混ざったら ●●● にする）。</item>
/// </list>
/// </summary>
public static class SitePublishService
{
    public const string TokenVariable = "CLOUDFLARE_API_TOKEN";
    /// <summary>アカウント ID は R2 の設定（R2_ACCOUNT_ID）と共通。1 か所で管理する。wrangler には CLOUDFLARE_ACCOUNT_ID として渡す。</summary>
    public const string AccountVariable = "R2_ACCOUNT_ID";

    /// <summary>本番の Worker の名前（wrangler.jsonc の name。MAINTENANCE_MODE の確認に使う）。</summary>
    public const string ProductionWorkerName = "fara-vrc-album";

    public static string EnvName(PublishTarget target) => target == PublishTarget.Production ? "production" : "develop";

    public static string Label(PublishTarget target) => target == PublishTarget.Production ? "本番" : "確認用（dev）";

    /// <summary>記事の中身（JSON 一式）の内容から決まる指紋。ファイル名と内容が同じなら同じ値（作成日時などは見ない）。</summary>
    public static string ContentHash(string contentDir)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in ContentBackupService.ContentFiles(contentDir))
        {
            var data = File.ReadAllBytes(Path.Combine(contentDir, path.Replace('/', Path.DirectorySeparatorChar)));
            sha.AppendData(Encoding.UTF8.GetBytes(path + "\0" + Convert.ToHexString(SHA256.HashData(data)) + "\n"));
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    public static PublishState StateOf(PublishRecord? record, string currentHash) =>
        record is null ? PublishState.Never
        : string.Equals(record.ContentHash, currentHash, StringComparison.Ordinal) ? PublishState.UpToDate
        : PublishState.Changed;

    /// <summary>画面に出す文（例: 公開済み（2026-10-27 22:30）／公開後に変更あり（最後の公開 …）／まだ公開していません）。</summary>
    public static string Describe(PublishRecord? record, string currentHash)
    {
        var when = record is null ? "" : record.At.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return StateOf(record, currentHash) switch
        {
            PublishState.UpToDate => $"公開済み（{when}）",
            PublishState.Changed => $"公開したあとに変更があります（最後の公開 {when}）",
            _ => "まだ公開していません",
        };
    }

    /// <summary>本番へ公開してよいか。確認用（dev）に今の記事の中身を公開済みでなければ、理由つきで拒否する。</summary>
    public static void RequireReadyForProduction(PublishLog log, string currentHash)
    {
        var state = StateOf(log.Get(PublishTarget.Develop), currentHash);
        if (state == PublishState.UpToDate) return;
        throw new InvalidOperationException(state == PublishState.Never
            ? "本番へ公開する前に、確認用（dev）に公開して、サイトを確かめてください（まだ確認用に公開していません）"
            : "本番へ公開する前に、今の内容を確認用（dev）に公開して、サイトを確かめてください（確認用に公開したあとに、記事の中身が変わっています）");
    }

    /// <summary>公開に要る Cloudflare の設定（トークンとアカウント ID）。足りなければ、足りない項目の名前だけを示して拒否する。</summary>
    public static (string Token, string AccountId) RequireCloudflare(RepoContext repo)
    {
        var token = repo.Env(TokenVariable);
        var account = repo.Env(AccountVariable);
        var missing = new List<string>();
        if (string.IsNullOrEmpty(token)) missing.Add("Cloudflare の API トークン（公開用）");
        if (string.IsNullOrEmpty(account)) missing.Add("Cloudflare R2 のアカウント ID");
        if (missing.Count > 0)
            throw new InvalidOperationException($"公開に要る設定が足りません: {string.Join("、", missing)}（メニューの「設定」で入力してください）");
        return (token!, account!);
    }

    /// <summary>
    /// サイトをビルドして公開する。成功したときだけ公開の記録を更新する。失敗は <see cref="InvalidOperationException"/>（原因の出力の最後の数行つき）。
    /// </summary>
    public static async Task<PublishRecord> PublishAsync(
        RepoContext repo, PublishTarget target, IProcessRunner runner, PublishLog log, DateTimeOffset now, Action<string> onLine, CancellationToken ct = default)
    {
        var contentDir = repo.ContentDir;
        if (ContentBackupService.ContentFiles(contentDir).Count == 0)
            throw new InvalidOperationException("公開する記事の中身がありません（content のフォルダが空か、見つかりません）");
        var (token, accountId) = RequireCloudflare(repo);
        var hash = ContentHash(contentDir);
        if (target == PublishTarget.Production) RequireReadyForProduction(log, hash);

        var secrets = new[] { token, accountId };
        var tail = new Queue<string>();
        void Output(string line)
        {
            var safe = Redactor.Redact(line, secrets);
            tail.Enqueue(safe);
            while (tail.Count > 12) tail.Dequeue();
            onLine(safe);
        }

        var env = new Dictionary<string, string>
        {
            [TokenVariable] = token,
            ["CLOUDFLARE_ACCOUNT_ID"] = accountId,
            ["ALBUM_CONTENT_DIR"] = contentDir,
        };

        if (target == PublishTarget.Production)
        {
            onLine("本番の公開の前に、メンテナンスの表示（MAINTENANCE_MODE）を確かめます");
            var gate = await runner.RunAsync("node", [".github/scripts/require-maintenance.mjs", ProductionWorkerName], repo.Root, env, Output, ct);
            if (gate != 0) throw Failure("本番の公開を止めました（メンテナンスの表示の確認）", tail);
            tail.Clear();
        }

        onLine($"{Label(target)}へ公開します（ビルドから。数分かかります）");
        var code = await runner.RunAsync("node", ["tools/site.mjs", "deploy", EnvName(target)], repo.Root, env, Output, ct);
        if (code != 0) throw Failure($"{Label(target)}への公開に失敗しました", tail);

        var record = new PublishRecord(hash, now, await CommitAsync(repo, runner, ct));
        log.Set(target, record);
        return record;
    }

    private static InvalidOperationException Failure(string headline, IEnumerable<string> tail)
    {
        var lines = tail.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(6).ToList();
        return new InvalidOperationException(lines.Count == 0 ? headline : headline + "。出力の最後: " + string.Join(" / ", lines));
    }

    /// <summary>公開したサイトのコードの版（Git のコミット。取れなければ null）。</summary>
    private static async Task<string?> CommitAsync(RepoContext repo, IProcessRunner runner, CancellationToken ct)
    {
        string? commit = null;
        try
        {
            var code = await runner.RunAsync("git", ["rev-parse", "--short", "HEAD"], repo.Root, new Dictionary<string, string>(), l => commit ??= l.Trim(), ct);
            return code == 0 && !string.IsNullOrEmpty(commit) ? commit : null;
        }
        catch (InvalidOperationException)
        {
            return null;                                                               // git が無くても公開は成功している
        }
    }
}
