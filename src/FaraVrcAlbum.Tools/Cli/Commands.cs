using System.Globalization;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Tools.Cli;

/// <summary>コマンドの振り分けと表示（処理の中身は Services）。</summary>
public static class Commands
{
    public static async Task<int> RunAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        return args.Command switch
        {
            "prepare" => Prepare(args, repo),
            "scan" => Scan(args, repo),
            "upload" => await UploadAsync(args, repo, ct),
            "draft" => Draft(args, repo),
            "verify" => await VerifyAsync(args, repo, ct),
            "audit" => await AuditAsync(args, repo, ct),
            "remove" => await RemoveAsync(args, repo, ct),
            "cover" => await CoverAsync(args, repo, ct),
            "avatar" or "outfit" or "photo-avatar" => OutfitCommands.Run(args, repo),
            "worlds" => await WorldsAsync(args, repo, ct),
            "world-stats" => await WorldStatsAsync(args, repo, ct),
            _ => throw new UsageException($"不明なコマンドです: {args.Command}"),
        };
    }

    private static int Prepare(Args args, RepoContext repo)
    {
        var from = args.Value("from") ?? SiteRules.EarliestPhotoDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var to = args.Value("to") ?? DateTimeOffset.UtcNow.ToOffset(PhotoKeys.Jst).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var created = PrepareService.Run(repo.UploadRoot(args), from, to);
        Console.WriteLine($"作成: {created.Count} フォルダ（{repo.UploadRoot(args)} 配下）");
        foreach (var dir in created) Console.WriteLine($"  {Path.GetRelativePath(repo.UploadRoot(args), dir)}");
        Console.WriteLine("掲載してよい PNG を、対応する inbox\\yyyy-mm にコピーしてください（コピーが掲載確認の記録）。");
        return 0;
    }

    private static int Scan(Args args, RepoContext repo)
    {
        var folder = args.Positional.FirstOrDefault() ?? repo.SourceRoot(args);
        if (!Directory.Exists(folder)) throw new UsageException($"フォルダがありません: {folder}");
        var ledger = PhotoLedger.Load(repo.ProductionLedgerPath);
        var cache = HashCache.Load(Path.Combine(repo.LocalDir, "cache", "hashes.json"));
        var rows = ScanService.Run(folder, ledger, cache);
        cache.Save();

        Console.WriteLine("月        件数  XMP有  取込済  削除済  未登録  2025/11前  読めず");
        foreach (var r in rows)
        {
            Console.WriteLine($"{r.Month,-9} {r.Total,5} {r.WithXmp,6} {r.Uploaded,7} {r.Removed,7} {r.Unregistered,7} {r.BeforeWindow,10} {r.Unreadable,7}");
        }
        Console.WriteLine($"合計 {rows.Sum(r => r.Total)} 件（読み取りのみ。ファイル名・ワールド名は表示しません）");
        return 0;
    }

    private static async Task<int> UploadAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var bucket = repo.Bucket(args);
        var apply = args.Has("apply");
        var testMode = !RepoContext.IsProductionBucket(bucket);
        var ledgerPath = repo.LedgerPathFor(bucket);
        var ledger = PhotoLedger.Load(ledgerPath);

        var credentials = repo.Credentials(out var missing);
        if (apply && credentials is null)
        {
            throw new UsageException($"--apply には R2 の接続が必要です。.env.local（または環境変数）に {string.Join(", ", missing)} が足りません。値は表示しません");
        }
        using var store = credentials is null ? null : new S3ObjectStore(credentials, bucket);

        var uploadRoot = repo.UploadRoot(args);
        var inbox = Path.Combine(uploadRoot, "inbox");
        var uploaded = Path.Combine(uploadRoot, "uploaded");
        Console.WriteLine($"バケット: {bucket}{(testMode ? "（試験用: 台帳は local/ に分け、ファイルは移動しません）" : "")}");
        Console.WriteLine(apply ? "実行: --apply（R2 に書き、台帳に登録し、uploaded/ へ移動します）" : "ドライラン（何も書きません）");
        if (credentials is null) Console.WriteLine("R2 は未接続（認証情報なし）。変換と検査だけを行います");

        using var lockFile = apply ? LockFile.Acquire(Path.Combine(repo.LocalDir, "album.lock")) : null;
        var now = DateTimeOffset.UtcNow;
        var report = await UploadService.RunAsync(
            inbox,
            uploaded,
            new UploadOptions { Apply = apply, Force = args.Has("force"), Keep = args.Has("keep"), TestMode = testMode, Bucket = bucket, Secrets = Secrets.Known(repo) },
            ledger,
            ledgerPath,
            store,
            now,
            item => PrintUploadItem(item, repo),
            ct);

        Console.WriteLine();
        Console.WriteLine($"完了: 新規 {report.Count(IngestState.Uploaded)} / 登録済み {report.Count(IngestState.AlreadyUploaded)} / 計画 {report.Count(IngestState.Planned)} / 拒否 {report.Count(IngestState.Refused)} / 失敗 {report.Failed} / 書き込み中で保留 {report.Skipped}");
        if (!apply) Console.WriteLine("ドライランです。実行するには同じコマンドに --apply を付けてください。");
        return report.Failed > 0 ? 1 : 0;
    }

    private static void PrintUploadItem(UploadItem item, RepoContext repo)
    {
        if (item.Skipped)
        {
            Console.WriteLine($"[保留] {item.SourceName}: 更新直後のため後で（5 秒以内）");
            return;
        }
        if (item.Error is not null)
        {
            // ファイル単位のエラーも、表示の直前に秘密を消す（UploadService でも消しているが二重に）
            Console.WriteLine($"[失敗] {item.SourceName}: {Secrets.Redact(item.Error, repo)}");
            return;
        }
        var result = item.Result!;
        Console.WriteLine($"[{StateLabel(result.State)}] {item.SourceName} → {result.Key ?? "-"}: {result.Message}");
        foreach (var warning in result.Warnings) Console.WriteLine($"    注意: {warning}");
        if (item.MovedTo is not null) Console.WriteLine("    移動: uploaded フォルダへ移しました");
    }

    private static string StateLabel(IngestState state) => state switch
    {
        IngestState.Planned => "計画",
        IngestState.Uploaded => "新規",
        IngestState.AlreadyUploaded => "登録済み",
        IngestState.Refused => "拒否",
        _ => state.ToString(),
    };

    private static int Draft(Args args, RepoContext repo)
    {
        var slug = args.Require("post");
        var ledger = PhotoLedger.Load(repo.ProductionLedgerPath);
        var editor = new ContentEditor(repo.ContentDir);
        var request = new DraftRequest
        {
            Slug = slug,
            Title = args.Value("title"),
            PublishedAt = args.Value("published"),
            Since = args.Value("since") is { } since ? ParseSince(since) : null,
            Keys = args.Value("keys")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Append = args.Has("append"),
            Avatar = args.Value("avatar"),
            Outfits = args.Value("outfits") is { } outfits ? outfits.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() : null,
        };
        var apply = args.Has("apply");
        var result = DraftService.Run(editor, ledger, request, DateTimeOffset.UtcNow, apply);
        var relative = Path.GetRelativePath(repo.Root, result.PostPath);
        if (!apply)
        {
            Console.WriteLine($"プレビュー（書いていません）: {relative} に写真 {result.Added} 件を追加（{(result.Created ? "新規" : "既存の記事に追記")}・draft: true）");
            foreach (var message in result.Messages) Console.WriteLine($"  {message}");
            Console.WriteLine("問題がなければ同じコマンドに --apply を付けて書いてください（書いたあと content/ の検査を行い、失敗したら元に戻します）。");
            return 0;
        }
        Console.WriteLine($"{(result.Created ? "作成" : "更新")}: {relative}（写真 {result.Added} 件を追加。draft: true）");
        foreach (var message in result.Messages) Console.WriteLine($"  {message}");
        return 0;
    }

    private static async Task<int> VerifyAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var content = new ContentEditor(repo.ContentDir).LoadContent();
        // --ledger: 記事に無い台帳（uploaded）の写真も検査する（読み取りのみ）
        var ledger = args.Has("ledger") ? PhotoLedger.Load(repo.ProductionLedgerPath) : null;
        var scope = VerifyService.Plan(content, ledger);
        Console.WriteLine($"確認: {scope.Photos.Count} 件（記事の写真 {scope.PostPhotoCount}・台帳のみ {scope.LedgerOnlyCount}）（{repo.ImageBaseUrl}）");
        foreach (var note in scope.Notes) Console.WriteLine($"  注: {note}");
        if (scope.Photos.Count == 0)
        {
            Console.WriteLine("検査する写真がないため、公開 URL は見ていません。");
            return 0;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var problems = await VerifyService.RunAsync(scope.Photos, repo.ImageBaseUrl, http, 8, ct);
        foreach (var problem in problems.Take(50)) Console.WriteLine($"  問題: {problem}");
        if (problems.Count > 50) Console.WriteLine($"  …ほか {problems.Count - 50} 件");
        Console.WriteLine(problems.Count == 0 ? "すべて合っています。" : $"問題 {problems.Count} 件。");
        return problems.Count == 0 ? 0 : 1;
    }

    private static async Task<int> AuditAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var bucket = repo.Bucket(args);
        using var store = repo.RequireStore(bucket);
        var ledger = PhotoLedger.Load(repo.LedgerPathFor(bucket));
        var editor = new ContentEditor(repo.ContentDir);
        // 記事（下書きを含む全件）の写真と台帳の uploaded で突き合わせる（欠け・孤児の両方の検査の元）
        var (referenced, covers, postKeys) = AuditService.References(editor, ledger);

        var report = await AuditService.RunAsync(store, referenced, covers, postKeys, ct);
        Console.WriteLine($"バケット {bucket}: {report.ObjectCount} オブジェクト / {report.TotalBytes / 1024.0 / 1024.0:0.0} MB");
        foreach (var (month, count) in report.CountByMonth) Console.WriteLine($"  {month}: {count} オブジェクト");
        Console.WriteLine($"孤児（どこからも参照されない）: {report.Orphans.Count} 件");
        foreach (var orphan in report.Orphans.Take(50)) Console.WriteLine($"  {orphan}");
        Console.WriteLine($"欠け（記事にあるのに _2048.webp が無い）: {report.Missing.Count} 件");
        foreach (var missing in report.Missing.Take(50)) Console.WriteLine($"  {missing}");
        return report.Missing.Count > 0 ? 1 : 0;
    }

    private static async Task<int> RemoveAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var key = args.Positional.FirstOrDefault() ?? throw new UsageException("写真のキーを指定してください（album remove <key>）");
        var bucket = repo.Bucket(args);
        RepoContext.RequireProductionBucket(bucket, "削除");
        var ledger = PhotoLedger.Load(repo.ProductionLedgerPath);
        var editor = new ContentEditor(repo.ContentDir);
        var plan = RemoveService.Plan(key, ledger, editor, repo.ImageBaseUrl);

        foreach (var warning in WorkTreeCheck.Warnings(repo.Root)) Console.WriteLine($"注意: {warning}");
        Console.WriteLine($"削除の計画: {key}");
        Console.WriteLine("  R2 のオブジェクト:");
        foreach (var r2Key in plan.R2Keys) Console.WriteLine($"    {r2Key}");
        Console.WriteLine($"  台帳: uploaded → removed（ハッシュは残す。再登録は拒否）");
        if (!args.Has("apply"))
        {
            Console.WriteLine("ドライランです。実行するには --apply。実行後、次の URL のキャッシュを Cloudflare で削除してください（URL 指定の削除）:");
            foreach (var url in plan.PurgeUrls) Console.WriteLine($"  {url}");
            return 0;
        }

        using var store = repo.RequireStore(bucket);
        await RemoveService.ApplyAsync(plan, bucket, ledger, repo.ProductionLedgerPath, store, DateTimeOffset.UtcNow, ct);
        Console.WriteLine("削除しました。次の URL のキャッシュを Cloudflare で削除してください（URL 指定の削除）:");
        foreach (var url in plan.PurgeUrls) Console.WriteLine($"  {url}");
        Console.WriteLine("次は PR（develop 向け）→ 確認用で確認 → 再デプロイ。");
        return 0;
    }

    private static async Task<int> CoverAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var key = args.Positional.FirstOrDefault() ?? throw new UsageException("写真のキーを指定してください（album cover <key> [--apply]）");
        var bucket = repo.Bucket(args);
        RepoContext.RequireProductionBucket(bucket, "表紙の OGP の作成");
        using var store = repo.RequireStore(bucket);
        var ledger = PhotoLedger.Load(repo.ProductionLedgerPath);
        var plan = await CoverService.PlanAsync(key, ledger, repo.UploadRoot(args), store, ct);
        if (!args.Has("apply"))
        {
            Console.WriteLine($"計画: {key} の _og.jpg（1200×630）を作って R2 に置く。{(plan.AlreadyInR2 ? "R2 には既にあるため、台帳のバイト数だけ更新" : "R2 には無い")}");
            Console.WriteLine("ドライランです。実行するには --apply。");
            return 0;
        }
        await CoverService.ApplyAsync(plan, bucket, ledger, repo.ProductionLedgerPath, store, ct);
        Console.WriteLine($"表紙の OGP を作りました: {key}_og.jpg（台帳を更新）。記事の cover にこの写真を指定すると、og:image に使われます。");
        return 0;
    }

    private static async Task<int> WorldsAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var editor = new ContentEditor(repo.ContentDir);
        var apply = args.Has("apply");
        var bucket = repo.Bucket(args);
        if (apply) RepoContext.RequireProductionBucket(bucket, "ワールドの画像の配置");
        using var http = WorldImageService.CreateClient();
        Console.WriteLine($"VRChat の API を、約 {WorldImageService.Interval.TotalSeconds:0} 秒おきに読みます（画像の入ったワールドは読みません）。");
        var plan = await WorldImageService.PlanImagesAsync(editor, http, args.Has("refresh"), ct);
        foreach (var item in plan.Items) Console.WriteLine($"  {item.Name}: {item.Key}（_512.webp・_1024.webp）");
        foreach (var problem in plan.Problems) Console.WriteLine($"  問題: {problem}");
        Console.WriteLine($"ワールドの画像: 置く {plan.Items.Count} 件・問題 {plan.Problems.Count} 件・そのまま {plan.Unchanged} 件");
        if (plan.Items.Count == 0) return plan.Problems.Count == 0 ? 0 : 1;
        if (!apply)
        {
            Console.WriteLine("ドライランです。R2 に置いて content/worlds.json に書くには --apply。");
            return 0;
        }
        using var store = repo.RequireStore(bucket);
        var (done, problems) = await WorldImageService.ApplyImagesAsync(editor, http, store, plan, ct);
        foreach (var problem in problems) Console.WriteLine($"  問題: {problem}");
        Console.WriteLine($"{done} 件の画像を R2 に置き、content/worlds.json を更新しました。次は PR（develop 向け）→ 確認用で確認。");
        return plan.Problems.Count + problems.Count == 0 ? 0 : 1;
    }

    private static async Task<int> WorldStatsAsync(Args args, RepoContext repo, CancellationToken ct)
    {
        var editor = new ContentEditor(repo.ContentDir);
        using var http = WorldImageService.CreateClient();
        Console.WriteLine($"VRChat の API を、約 {WorldImageService.Interval.TotalSeconds:0} 秒おきに読みます（全ワールド）。");
        var result = await WorldImageService.UpdateStatsAsync(editor, http, DateTimeOffset.UtcNow, args.Has("apply"), Console.WriteLine, ct);
        foreach (var problem in result.Problems) Console.WriteLine($"  問題: {problem}");
        Console.WriteLine(args.Has("apply")
            ? $"content/world-stats.json を更新しました（{result.Updated} 件）。"
            : $"ドライランです（{result.Updated} 件を取得）。書くには --apply。");
        // 1 件も取れなかったときだけ失敗にする（一部の失敗・429 は、取れた分を書いて成功）
        return result.Updated > 0 || result.Problems.Count == 0 ? 0 : 1;
    }

    /// <summary>--since: yyyy-mm-dd（JST の 0 時）または ISO 8601 の日時。</summary>
    public static DateTimeOffset ParseSince(string text)
    {
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new DateTimeOffset(date, PhotoKeys.Jst);
        }
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed;
        throw new UsageException($"--since の形式が不正です（yyyy-mm-dd か ISO 8601）: {text}");
    }
}

/// <summary>同時実行の防止（local/album.lock）。残っていれば、前回が異常終了したかを確かめてから消す。</summary>
public sealed class LockFile : IDisposable
{
    private readonly string _path;

    private LockFile(string path) => _path = path;

    public static LockFile Acquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            using (File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
        }
        catch (IOException)
        {
            throw new UsageException($"別の album が動いているようです（{path}）。確認してから消してください");
        }
        return new LockFile(path);
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
