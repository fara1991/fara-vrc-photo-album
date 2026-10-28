using System.Globalization;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools.Services;

public sealed record UploadOptions
{
    /// <summary>false は計画のみ（R2 にも台帳にもファイルにも書かない）。</summary>
    public bool Apply { get; init; }

    public bool Force { get; init; }

    /// <summary>true で uploaded/ へ移さない（--keep）。</summary>
    public bool Keep { get; init; }

    /// <summary>試験用バケット: 台帳は別ファイル、ファイルは移さない（本番の台帳を汚さない）。</summary>
    public bool TestMode { get; init; }

    /// <summary>書き先のバケット（既定は本番）。</summary>
    public string Bucket { get; init; } = RepoContext.CurrentProductionBucket;

    /// <summary>エラーの文面から消す値（R2 の認証情報）。Secrets.Known の結果を渡す。</summary>
    public IReadOnlyList<string> Secrets { get; init; } = [];
}

/// <summary>1 ファイルの結果。Result が null なら Error（例外）か Skipped（書き込み中）。</summary>
public sealed record UploadItem(string SourceName, IngestResult? Result, string? Error, bool Skipped, string? MovedTo);

public sealed record UploadReport(IReadOnlyList<UploadItem> Items)
{
    public int Count(IngestState state) => Items.Count(i => i.Result?.State == state);

    public int Failed => Items.Count(i => i.Error is not null);

    public int Skipped => Items.Count(i => i.Skipped);
}

/// <summary>
/// inbox/**/*.png を取り込む（計画書 §5.5.3）。ファイルごとに: 取り込み → 台帳を保存（変わったとき）→ 成功したら uploaded/ へ移動。
/// 失敗したファイルは台帳に入らず、次の実行で同じ手順が再試行される（HEAD で既存分は飛ばす）。
/// 書き込み中と見られる（5 秒以内に更新）ファイルは飛ばす。
/// </summary>
public static class UploadService
{
    public static async Task<UploadReport> RunAsync(
        string inboxRoot,
        string uploadedRoot,
        UploadOptions options,
        PhotoLedger ledger,
        string ledgerPath,
        IObjectStore? store,
        DateTimeOffset now,
        Action<UploadItem>? onItem = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(inboxRoot)) return new UploadReport([]);
        var files = Directory.EnumerateFiles(inboxRoot, "*.png", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        return await RunFilesAsync(files, inboxRoot, uploadedRoot, options, ledger, ledgerPath, store, now, onItem, ct);
    }

    /// <summary>
    /// 選んだファイルだけを取り込む（デスクトップ版は画面でチェックしたファイルを渡す）。
    /// 入力は inboxRoot の中の PNG に限る（それ以外は例外。元写真・uploaded を入力に取らない）。
    /// </summary>
    public static Task<UploadReport> RunFilesAsync(
        IReadOnlyList<string> files,
        string inboxRoot,
        string uploadedRoot,
        UploadOptions options,
        PhotoLedger ledger,
        string ledgerPath,
        IObjectStore? store,
        DateTimeOffset now,
        Action<UploadItem>? onItem = null,
        CancellationToken ct = default) =>
        RunCoreAsync(files, inboxRoot, uploadedRoot, options, ledger, ledgerPath, store, now, onItem, ct);

    /// <summary>
    /// VRChat の写真格納フォルダ（sourceRoot\yyyy-mm）の PNG を、コピーせずに直接取り込む（デスクトップ版）。
    /// 元の写真は読むだけで、移動も削除もしない（Keep を強制する）。入力は sourceRoot の中の PNG に限る。
    /// </summary>
    public static Task<UploadReport> RunSourceFilesAsync(
        IReadOnlyList<string> files,
        string sourceRoot,
        UploadOptions options,
        PhotoLedger ledger,
        string ledgerPath,
        IObjectStore? store,
        DateTimeOffset now,
        Action<UploadItem>? onItem = null,
        CancellationToken ct = default) =>
        RunCoreAsync(files, sourceRoot, sourceRoot, options with { Keep = true }, ledger, ledgerPath, store, now, onItem, ct);

    private static async Task<UploadReport> RunCoreAsync(
        IReadOnlyList<string> files,
        string inboxRoot,
        string uploadedRoot,
        UploadOptions options,
        PhotoLedger ledger,
        string ledgerPath,
        IObjectStore? store,
        DateTimeOffset now,
        Action<UploadItem>? onItem,
        CancellationToken ct)
    {
        var items = new List<UploadItem>();
        var root = Path.GetFullPath(inboxRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var file in files)
        {
            if (!Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase) || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("取り込みの入力は、決められたフォルダの中の PNG だけです");
            }
        }

        var ingestor = new PhotoIngestor(ledger, store);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            UploadItem item;
            if (now.UtcDateTime - File.GetLastWriteTimeUtc(file) < TimeSpan.FromSeconds(5))
            {
                item = new UploadItem(Path.GetFileName(file), null, null, true, null);
            }
            else
            {
                item = await ProcessAsync(file, inboxRoot, uploadedRoot, options, ledger, ledgerPath, ingestor, ct);
            }
            items.Add(item);
            onItem?.Invoke(item);
        }
        return new UploadReport(items);
    }

    private static async Task<UploadItem> ProcessAsync(
        string file,
        string inboxRoot,
        string uploadedRoot,
        UploadOptions options,
        PhotoLedger ledger,
        string ledgerPath,
        PhotoIngestor ingestor,
        CancellationToken ct)
    {
        var name = Path.GetFileName(file);
        var firstFolder = Path.GetRelativePath(inboxRoot, file).Split(Path.DirectorySeparatorChar)[0];
        var folderMonth = System.Text.RegularExpressions.Regex.IsMatch(firstFolder, @"^\d{4}-\d{2}$") ? firstFolder : null;

        IngestResult result;
        try
        {
            result = await ingestor.IngestAsync(file, new IngestOptions { Apply = options.Apply, Force = options.Force, FolderMonth = folderMonth }, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new UploadItem(name, null, Redactor.Redact(e.Message, options.Secrets), false, null);
        }

        if (options.Apply && result.LedgerChanged)
        {
            // 台帳を保存できなければ、このファイルだけ失敗にする（移動しない・他のファイルは続ける）。R2 には書かれているので、再実行は HEAD で補われる
            try
            {
                ledger.Save(ledgerPath);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new UploadItem(name, null, Redactor.Redact($"台帳を保存できませんでした（R2 には書かれています。原因を直してから同じコマンドを再実行）: {e.Message}", options.Secrets), false, null);
            }
        }

        string? movedTo = null;
        var finished = result.State is IngestState.Uploaded or IngestState.AlreadyUploaded;
        if (options.Apply && finished && !options.Keep && !options.TestMode && result.Key is not null)
        {
            // 移動だけの失敗も、このファイルだけの失敗にする（R2 と台帳は登録済み。手で移すか、台帳の状態を見て直す）
            try
            {
                movedTo = MoveToUploaded(file, uploadedRoot, PhotoKeys.MonthOf(result.Key));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new UploadItem(name, result, Redactor.Redact($"R2 と台帳には登録済みですが、uploaded フォルダへ移せませんでした（手で移してください）: {e.Message}", options.Secrets), false, null);
            }
        }
        return new UploadItem(name, result, null, false, movedTo);
    }

    /// <summary>uploaded/yyyy-mm/ へ移す。同じ名前があれば (2) などを付ける。</summary>
    private static string MoveToUploaded(string file, string uploadedRoot, string month)
    {
        var directory = Path.Combine(uploadedRoot, month);
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(file);
        var target = Path.Combine(directory, Path.GetFileName(file));
        for (var n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(directory, $"{stem} ({n.ToString(CultureInfo.InvariantCulture)}).png");
        }
        File.Move(file, target);
        return target;
    }
}

/// <summary>inbox/yyyy-mm のフォルダを作る（空のフォルダだけ。計画書 §5.5.2 の prepare）。</summary>
public static class PrepareService
{
    public static IReadOnlyList<string> Run(string uploadRoot, string from, string to)
    {
        var start = ParseMonth(from);
        var end = ParseMonth(to);
        if (start > end) throw new ArgumentException("--from が --to より後です");

        var created = new List<string>();
        foreach (var dir in new[] { Path.Combine(uploadRoot, "uploaded"), Path.Combine(uploadRoot, "_work") })
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                created.Add(dir);
            }
        }
        for (var month = start; month <= end; month = month.AddMonths(1))
        {
            var dir = Path.Combine(uploadRoot, "inbox", month.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            if (Directory.Exists(dir)) continue;
            Directory.CreateDirectory(dir);
            created.Add(dir);
        }
        return created;
    }

    public static DateTime ParseMonth(string text)
    {
        if (!DateTime.TryParseExact(text, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
        {
            throw new ArgumentException($"月は yyyy-mm の形式で指定してください: {text}");
        }
        return month;
    }
}
