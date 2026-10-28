using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>ローカルと R2 の 1 ファイルの状態。</summary>
public enum SyncStatus
{
    /// <summary>どちらにもあり、中身が同じ。</summary>
    Same,

    /// <summary>ローカルにだけある。</summary>
    LocalOnly,

    /// <summary>R2 にだけある。</summary>
    RemoteOnly,

    /// <summary>どちらにもあるが、中身が違う。</summary>
    Different,
}

/// <summary>1 ファイルについての操作。</summary>
public enum SyncAction
{
    None,

    /// <summary>ローカルの内容を R2 へ置く（R2 にあれば上書き）。</summary>
    Upload,

    /// <summary>R2 の内容をローカルへ取り込む（ローカルにあれば上書き。上書き前の内容は残す）。</summary>
    Download,

    /// <summary>ローカルのファイルを消す（R2 に無いファイルだけ。消す前の内容は残す）。</summary>
    DeleteLocal,

    /// <summary>R2 のファイルを消す（ローカルに無いファイルだけ。消す前の内容は、ローカルの安全の控えに残す）。</summary>
    DeleteRemote,
}

public sealed record SyncItem(string Path, SyncStatus Status, long? LocalBytes, long? RemoteBytes);

public sealed record SyncResult(int Uploaded, int Downloaded, int DeletedLocal = 0, int DeletedRemote = 0);

/// <summary>
/// 記事の中身（content/ の JSON）の、ローカルと R2 の同期。R2 は、控え用のバケット（<see cref="ContentBackupService.RequireBackupBucket"/>。公開しない）の
/// <c>sync/{相対パス}</c> に、「いまの内容」を 1 ファイルずつ置く（履歴用のスナップショット <c>snapshots/</c> とは別。スナップショットはそのまま残る）。
/// どちらかにだけあるファイル・中身の違うファイルを一覧にして、ファイルごとに「送る・取り込む・消す・何もしない」を選べる。
/// <list type="bullet">
/// <item>消せるのは、<b>相手側に無いファイルだけ</b>（ローカルにだけある → ローカルから消す、R2 にだけある → R2 から消す）。中身の違うファイルは消せない（上書きで直す）。</item>
/// <item>ローカルを変える操作（取り込み・消す）は、変える前の内容を safetyCopyDir に残し、そのあとの content が記事として読めなければ全部元に戻す（R2 へは何もしない）。</item>
/// <item>R2 から消すときは、消す前の内容を safetyCopyDir/r2/ に残す（R2 には履歴が無いため）。</item>
/// </list>
/// </summary>
public static class ContentSyncService
{
    public const string Prefix = "sync/";
    private const string PrivateCache = "private, no-store";

    /// <summary>R2 から消したファイルの、ローカルの安全の控えの中の置き場所（safetyCopyDir の下）。</summary>
    public const string RemoteBackupFolder = "r2";

    private static string StoreKey(string path) => Prefix + path;

    private static string LocalPath(string contentDir, string path) => Path.Combine(contentDir, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>ローカルと R2 を比べる（R2 の中身は、同じかどうかを確かめるため、ファイルごとに読む。JSON は小さい）。並びはパス順。</summary>
    public static async Task<IReadOnlyList<SyncItem>> CompareAsync(string contentDir, IObjectStore store, CancellationToken ct = default)
    {
        var local = ContentBackupService.ContentFiles(contentDir).ToHashSet(StringComparer.Ordinal);
        var remote = new Dictionary<string, long>(StringComparer.Ordinal);
        await foreach (var (key, size) in store.ListAsync(Prefix, ct))
        {
            var path = key[Prefix.Length..];
            if (ContentPath.IsSafe(path)) remote[path] = size;                                    // 形の合わないキー（控えの目録など）は見ない
        }

        var items = new List<SyncItem>();
        foreach (var path in local.Union(remote.Keys).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var inLocal = local.Contains(path);
            var inRemote = remote.ContainsKey(path);
            if (inLocal && !inRemote)
            {
                items.Add(new SyncItem(path, SyncStatus.LocalOnly, new FileInfo(LocalPath(contentDir, path)).Length, null));
                continue;
            }
            if (!inLocal)
            {
                items.Add(new SyncItem(path, SyncStatus.RemoteOnly, null, remote[path]));
                continue;
            }
            var localData = await File.ReadAllBytesAsync(LocalPath(contentDir, path), ct);
            var remoteData = await store.GetAsync(StoreKey(path), ct);
            var status = remoteData is not null && localData.AsSpan().SequenceEqual(remoteData) ? SyncStatus.Same : SyncStatus.Different;
            items.Add(new SyncItem(path, status, localData.Length, remoteData?.Length ?? remote[path]));
        }
        return items;
    }

    /// <summary>
    /// 選んだ操作を行う。先にローカルを変える操作（取り込み・ローカルから消す）をして、content が記事として読めるか確かめ、読めなければ元に戻して例外にする
    /// （R2 へは何もしない）。そのあとで、R2 を変える操作（送る・R2 から消す）をする。
    /// </summary>
    public static async Task<SyncResult> ApplyAsync(string contentDir, IObjectStore store, IReadOnlyList<(string Path, SyncAction Action)> actions, string safetyCopyDir, CancellationToken ct = default)
    {
        foreach (var (path, _) in actions)
            if (!ContentPath.IsSafe(path)) throw new InvalidOperationException($"ファイル名が不正です: {path}");

        // ---- 先に読む（ここまでは、何も変えない）
        var downloads = new List<(string Path, byte[] Data, byte[]? Before)>();
        foreach (var (path, _) in actions.Where(a => a.Action == SyncAction.Download))
        {
            var data = await store.GetAsync(StoreKey(path), ct) ?? throw new InvalidOperationException($"R2 にファイルがありません: {path}");
            downloads.Add((path, data, await ReadLocalOrNullAsync(contentDir, path, ct)));
        }
        var localDeletes = new List<(string Path, byte[] Before)>();
        foreach (var (path, _) in actions.Where(a => a.Action == SyncAction.DeleteLocal))
        {
            var before = await ReadLocalOrNullAsync(contentDir, path, ct) ?? throw new FileNotFoundException($"ローカルにファイルがありません: {path}");
            if (await store.ExistsAsync(StoreKey(path), ct)) throw new InvalidOperationException($"R2 にもあるファイルは、ローカルから消せません（R2 に無いファイルだけ消せます）: {path}");
            localDeletes.Add((path, before));
        }
        var remoteDeletes = new List<(string Path, byte[] Before)>();
        foreach (var (path, _) in actions.Where(a => a.Action == SyncAction.DeleteRemote))
        {
            var before = await store.GetAsync(StoreKey(path), ct) ?? throw new InvalidOperationException($"R2 にファイルがありません: {path}");
            if (File.Exists(LocalPath(contentDir, path))) throw new InvalidOperationException($"ローカルにもあるファイルは、R2 から消せません（ローカルに無いファイルだけ消せます）: {path}");
            remoteDeletes.Add((path, before));
        }

        // ---- 消す前・上書きする前の内容を残す
        foreach (var (path, _, before) in downloads.Where(d => d.Before is not null))
            await SaveCopyAsync(safetyCopyDir, path, before!, ct);
        foreach (var (path, before) in localDeletes) await SaveCopyAsync(safetyCopyDir, path, before, ct);
        foreach (var (path, before) in remoteDeletes) await SaveCopyAsync(Path.Combine(safetyCopyDir, RemoteBackupFolder), path, before, ct);

        // ---- ローカルを変える（取り込み・消す）。そのあとの content が読めなければ、全部元に戻す
        if (downloads.Count > 0 || localDeletes.Count > 0)
        {
            Directory.CreateDirectory(contentDir);
            var changed = new List<(string Path, byte[]? Before)>();
            try
            {
                foreach (var (path, data, before) in downloads)
                {
                    JsonFiles.WriteAtomic(LocalPath(contentDir, path), data);
                    changed.Add((path, before));
                }
                foreach (var (path, before) in localDeletes)
                {
                    File.Delete(LocalPath(contentDir, path));
                    changed.Add((path, before));
                }
                ContentLoader.Load(contentDir);                                            // 変えたあとの content が、記事として読めるか（落ちたら例外）
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var failed = ContentPath.RollBack(contentDir, changed);
                if (failed.Count > 0)
                    throw new IOException("ローカルを元に戻せなかったファイルがあります（R2 は変えていません。変える前の内容は local/sync-backups にあります）:\n  " + string.Join("\n  ", failed), e);
                if (e is ContentException or InvalidDataException)
                    throw new InvalidDataException($"この内容にすると、記事の中身として読めなくなるので、ローカルは変えませんでした（元に戻しました。R2 も変えていません）: {e.Message}", e);
                throw;                                                                     // 書けなかった（容量・権限など）も、元に戻したうえで、そのままの例外
            }
        }

        // ---- R2 を変える（送る・消す）
        var uploaded = 0;
        foreach (var (path, _) in actions.Where(a => a.Action == SyncAction.Upload))
        {
            ct.ThrowIfCancellationRequested();
            var local = LocalPath(contentDir, path);
            if (!File.Exists(local)) throw new FileNotFoundException($"ローカルにファイルがありません: {path}");
            await store.PutAsync(StoreKey(path), await File.ReadAllBytesAsync(local, ct), "application/json", PrivateCache, ct);
            uploaded++;
        }
        foreach (var (path, _) in remoteDeletes)
        {
            ct.ThrowIfCancellationRequested();
            await store.DeleteAsync(StoreKey(path), ct);
        }
        return new SyncResult(uploaded, downloads.Count, localDeletes.Count, remoteDeletes.Count);
    }

    private static async Task<byte[]?> ReadLocalOrNullAsync(string contentDir, string path, CancellationToken ct)
    {
        var local = LocalPath(contentDir, path);
        return File.Exists(local) ? await File.ReadAllBytesAsync(local, ct) : null;
    }

    private static async Task SaveCopyAsync(string baseDir, string path, byte[] data, CancellationToken ct)
    {
        var copy = LocalPath(baseDir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        await File.WriteAllBytesAsync(copy, data, ct);
    }
}
