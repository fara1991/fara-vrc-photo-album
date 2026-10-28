using System.Security.Cryptography;
using System.Text.Json;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>控え（スナップショット）1 つの情報。Id は作成した時刻（JST の yyyyMMdd-HHmmss）。</summary>
public sealed record SnapshotInfo(string Id, DateTimeOffset CreatedAt, int FileCount, long Bytes);

/// <summary>
/// 記事の中身（content/ の JSON 一式）の、R2 への控え（バックアップ）と、そこからの復元。
/// <b>控えは写真のバケットには置かない</b>（写真のバケットは独自ドメインで公開されており、台帳の元ファイル名などが誰にでも読めてしまうため）。
/// 公開設定のない、別の（控え用の）バケットだけに置く。スナップショットは <c>snapshots/{id}/…</c> に作り、上書きしない（古い控えが残る）。
/// 復元は、いったん一時フォルダに取り出して中身を検査（ビルドと同じ ContentLoader）してから、元の内容を安全の控えにコピーして入れ替える。
/// </summary>
public static class ContentBackupService
{
    public const string SnapshotsPrefix = "snapshots/";
    private const string ManifestName = "manifest.json";
    private const string PrivateCache = "private, no-store";

    /// <summary>控え用のバケット名を確かめる。空・名前の形が不正・写真のバケットと同じなら理由つきで拒否する。</summary>
    public static string RequireBackupBucket(string? backupBucket, string photoBucket)
    {
        var name = backupBucket?.Trim() ?? "";
        if (name.Length == 0)
            throw new InvalidOperationException("控え用のバケット名が設定されていません（メニューの「設定」で入力してください。写真のバケットとは別の、公開しないバケットにします）");
        if (!Cli.RepoContext.IsValidBucketName(name))
            throw new InvalidOperationException("控え用のバケット名は、小文字の英数字とハイフンで、3〜63 文字にしてください");
        if (string.Equals(name, photoBucket, StringComparison.Ordinal))
            throw new InvalidOperationException("控え用のバケットは、写真のバケットと同じにはできません（写真のバケットは公開されているため、控えが誰にでも読めてしまいます）");
        return name;
    }

    /// <summary>content フォルダの中の JSON（相対パス。区切りは /。並びは安定）。</summary>
    public static IReadOnlyList<string> ContentFiles(string contentDir) =>
        !Directory.Exists(contentDir)
            ? []
            : Directory.EnumerateFiles(contentDir, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(contentDir, f).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToList();

    private static string IdOf(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

    private static string Sha(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>content の JSON をスナップショットとして、控え用のバケットに置く（上書きしない）。</summary>
    public static async Task<SnapshotInfo> BackupAsync(string contentDir, IObjectStore store, DateTimeOffset now, CancellationToken ct = default)
    {
        var files = ContentFiles(contentDir);
        if (files.Count == 0) throw new InvalidOperationException($"控えるファイルがありません（content のフォルダが空か、見つかりません）");
        var id = IdOf(now);
        var prefix = SnapshotsPrefix + id + "/";
        if (await store.ExistsAsync(prefix + ManifestName, ct)) throw new InvalidOperationException($"同じ時刻の控えがすでにあります（{id}）。少し待ってからやり直してください");

        var entries = new List<object>();
        long bytes = 0;
        foreach (var path in files)
        {
            var data = await File.ReadAllBytesAsync(Path.Combine(contentDir, path.Replace('/', Path.DirectorySeparatorChar)), ct);
            await store.PutAsync(prefix + path, data, "application/json", PrivateCache, ct);
            entries.Add(new { path, bytes = data.Length, sha256 = Sha(data) });
            bytes += data.Length;
        }
        // 目録は最後に置く（目録があれば、全ファイルが置けている）
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new { createdAt = now.ToString("o"), files = entries }, new JsonSerializerOptions { WriteIndented = true });
        await store.PutAsync(prefix + ManifestName, manifest, "application/json", PrivateCache, ct);
        return new SnapshotInfo(id, now, files.Count, bytes);
    }

    /// <summary>控えの一覧（新しい順）。目録のあるものだけ（途中で止まった控えは出さない）。</summary>
    public static async Task<IReadOnlyList<SnapshotInfo>> ListAsync(IObjectStore store, CancellationToken ct = default)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        await foreach (var (key, _) in store.ListAsync(SnapshotsPrefix, ct))
        {
            if (!key.EndsWith("/" + ManifestName, StringComparison.Ordinal)) continue;
            var middle = key[SnapshotsPrefix.Length..^(ManifestName.Length + 1)];
            if (middle.Length > 0 && !middle.Contains('/')) ids.Add(middle);
        }
        var list = new List<SnapshotInfo>();
        foreach (var id in ids.Reverse())
        {
            var manifest = await ReadManifestAsync(store, id, ct);
            if (manifest is not null) list.Add(manifest.Value.Info);
        }
        return list;
    }

    private static async Task<(SnapshotInfo Info, List<(string Path, long Bytes, string Sha)> Files)?> ReadManifestAsync(IObjectStore store, string id, CancellationToken ct)
    {
        var raw = await store.GetAsync(SnapshotsPrefix + id + "/" + ManifestName, ct);
        if (raw is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var files = doc.RootElement.GetProperty("files").EnumerateArray()
                .Select(e => (
                    e.GetProperty("path").GetString() ?? throw new InvalidDataException("path がありません"),
                    e.GetProperty("bytes").GetInt64(),
                    e.GetProperty("sha256").GetString() ?? throw new InvalidDataException("sha256 がありません")))
                .ToList();
            var created = DateTimeOffset.TryParse(doc.RootElement.GetProperty("createdAt").GetString(), out var at) ? at : DateTimeOffset.MinValue;
            return (new SnapshotInfo(id, created, files.Count, files.Sum(f => f.Item2)), files);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or InvalidDataException)
        {
            return null;                                                                // 形の壊れた目録の控えは一覧から外す（ほかの控えは出す）
        }
    }

    /// <summary>
    /// 控えから content に戻す。一時フォルダに取り出して、ファイルの内容（SHA-256）と content としての検査（ContentLoader）を通してから、
    /// 今の content を safetyCopyDir にコピーし、content を控えの内容に入れ替える（控えに無い JSON は消す）。検査に落ちたら、何も変えない。
    /// </summary>
    public static async Task<int> RestoreAsync(IObjectStore store, string snapshotId, string contentDir, string safetyCopyDir, CancellationToken ct = default)
    {
        var manifest = await ReadManifestAsync(store, snapshotId, ct) ?? throw new InvalidOperationException($"控えが見つからないか、壊れています（{snapshotId}）");
        var temp = Path.Combine(Path.GetTempPath(), "fara-album-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var (path, bytes, sha) in manifest.Files)
            {
                if (!ContentPath.IsSafe(path)) throw new InvalidOperationException($"控えのファイル名が不正です: {path}");
                var data = await store.GetAsync(SnapshotsPrefix + snapshotId + "/" + path, ct) ?? throw new InvalidOperationException($"控えのファイルがありません: {path}");
                if (data.Length != bytes || Sha(data) != sha) throw new InvalidOperationException($"控えのファイルが壊れています: {path}");
                contents[path] = data;
                var target = Path.Combine(temp, path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllBytesAsync(target, data, ct);
            }
            ContentLoader.Load(temp);                                                   // 記事として読めない内容は戻さない（落ちたら例外。何も変えていない）

            if (Directory.Exists(contentDir) && ContentFiles(contentDir).Count > 0) CopyContent(contentDir, safetyCopyDir);
            Directory.CreateDirectory(contentDir);
            // ここから content を変える。途中で失敗したら、変える前の内容（メモリ上）に戻す（ContentSyncService.ApplyAsync と同じ）
            var keep = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
            var changed = new List<(string Path, byte[]? Before)>();
            try
            {
                foreach (var existing in ContentFiles(contentDir).Where(f => !keep.Contains(f)))
                {
                    var local = Path.Combine(contentDir, existing.Replace('/', Path.DirectorySeparatorChar));
                    changed.Add((existing, await File.ReadAllBytesAsync(local, ct)));
                    File.Delete(local);
                }
                foreach (var path in keep)
                {
                    var target = Path.Combine(contentDir, path.Replace('/', Path.DirectorySeparatorChar));
                    changed.Add((path, File.Exists(target) ? await File.ReadAllBytesAsync(target, ct) : null));
                    JsonFiles.WriteAtomic(target, contents[path]);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var failed = ContentPath.RollBack(contentDir, changed);
                if (failed.Count > 0)
                    throw new IOException("元に戻せなかったファイルがあります（変える前の内容は安全の控えにあります）:\n  " + string.Join("\n  ", failed), e);
                throw;
            }
            return manifest.Files.Count;
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    /// <summary>
    /// content を別のフォルダへコピーする（移さない。元は変えない）。コピー先は、なければ作り、あれば空のときだけ。
    /// 作業中のファイル（Git に入れていない変更や、新しいファイルも）をそのままコピーし、コピーが content として読めるか検査する（落ちたらコピー先を消して例外）。
    /// </summary>
    public static int CopyContent(string source, string dest)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"コピー元が見つかりません: {source}");
        var full = Path.GetFullPath(dest);
        var src = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((full + Path.DirectorySeparatorChar).StartsWith(src, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("コピー先は、コピー元の中には作れません");
        var existed = Directory.Exists(full);
        if (existed && Directory.EnumerateFileSystemEntries(full).Any()) throw new InvalidOperationException("コピー先のフォルダが空ではありません（空のフォルダか、新しい名前のフォルダを選んでください）");
        Directory.CreateDirectory(full);
        var count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(full, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
                count++;
            }
            ContentLoader.Load(full);                                                   // コピーが、そのままビルドに使える内容か
        }
        catch
        {
            if (!existed) Directory.Delete(full, recursive: true);
            else foreach (var e in Directory.EnumerateFileSystemEntries(full)) { if (Directory.Exists(e)) Directory.Delete(e, true); else File.Delete(e); }
            throw;
        }
        return count;
    }
}
