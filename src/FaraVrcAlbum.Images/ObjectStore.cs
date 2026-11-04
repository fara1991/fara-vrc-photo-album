using System.Collections.Concurrent;
using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace FaraVrcAlbum.Images;

/// <summary>R2（S3 互換）のオブジェクト操作。テストは <see cref="MemoryObjectStore"/>。</summary>
public interface IObjectStore
{
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);

    /// <summary>中身を読む。無ければ null。</summary>
    Task<byte[]?> GetAsync(string key, CancellationToken ct = default);

    Task PutAsync(string key, byte[] data, string contentType, string cacheControl, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>prefix で始まるオブジェクト（キーとバイト数）。</summary>
    IAsyncEnumerable<(string Key, long Size)> ListAsync(string prefix, CancellationToken ct = default);
}

/// <summary>R2 に置くときのヘッダー（計画書 §5.3）。</summary>
public static class ObjectHeaders
{
    /// <summary>写真（キーにハッシュを含む・内容が変わらない）。</summary>
    public const string ImmutableImage = "public, max-age=31536000, immutable";

    public static string ContentTypeFor(string key) => Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".webp" => "image/webp",
        ".jpg" => "image/jpeg",
        ".json" => "application/json; charset=utf-8",
        _ => throw new ArgumentException($"未対応の拡張子: {key}", nameof(key)),
    };
}

/// <summary>R2 の接続情報（値は表示しない。環境変数 / .env.local から読む。読み方は Tools 側）。</summary>
public sealed record R2Credentials(string AccountId, string AccessKeyId, string SecretAccessKey);

/// <summary>
/// Cloudflare R2 の S3 互換 API（docs/cloudflare-setup.md の C4）。
/// Cloudflare の公式例に従い、ServiceURL = https://{account}.r2.cloudflarestorage.com、リージョン auto、
/// PutObject は DisablePayloadSigning と DisableDefaultChecksumValidation を付ける（R2 が Streaming SigV4 を扱えないため）。
/// 実バケットへの接続は手元でのみ行う（この実装は CI・テストでは実際には使わない）。
/// </summary>
public sealed class S3ObjectStore : IObjectStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3ObjectStore(R2Credentials credentials, string bucket)
    {
        _bucket = bucket;
        _client = new AmazonS3Client(
            new BasicAWSCredentials(credentials.AccessKeyId, credentials.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = $"https://{credentials.AccountId}.r2.cloudflarestorage.com",
                AuthenticationRegion = "auto",
                ForcePathStyle = true,
            });
    }

    public string Bucket => _bucket;

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(new GetObjectMetadataRequest { BucketName = _bucket, Key = key }, ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.GetObjectAsync(new GetObjectRequest { BucketName = _bucket, Key = key }, ct);
            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task PutAsync(string key, byte[] data, string contentType, string cacheControl, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = new MemoryStream(data, writable: false),
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true,
        };
        request.Headers.CacheControl = cacheControl;
        await _client.PutObjectAsync(request, ct);
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await _client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key }, ct);
    }

    public async IAsyncEnumerable<(string Key, long Size)> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        string? token = null;
        do
        {
            var response = await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix, ContinuationToken = token }, ct);
            foreach (var (key, size) in ObjectsOf(response)) yield return (key, size);
            token = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (token is not null);
    }

    /// <summary>
    /// 一覧の応答のオブジェクト（キーとバイト数）。<b>該当するものが 1 つも無いとき、AWS SDK は S3Objects に空のリストではなく null を返す</b>ので、
    /// null は空として扱う（初めて sync/ や snapshots/ を調べるとき、これが無いと NullReferenceException になる）。
    /// </summary>
    internal static IEnumerable<(string Key, long Size)> ObjectsOf(ListObjectsV2Response response) =>
        (response.S3Objects ?? []).Select(o => (o.Key, o.Size ?? 0L));

    public void Dispose() => _client.Dispose();
}

/// <summary>テスト用のメモリ実装。PUT の失敗を差し込める（途中失敗の試験）。</summary>
public sealed class MemoryObjectStore : IObjectStore
{
    public sealed record Stored(byte[] Data, string ContentType, string CacheControl);

    private readonly ConcurrentDictionary<string, Stored> _objects = new(StringComparer.Ordinal);

    /// <summary>PUT の直前に呼ぶ（例外を投げると PUT は失敗する）。引数はキーと、これまでの PUT 回数。</summary>
    public Func<string, int, Exception?>? OnPut { get; set; }

    /// <summary>DELETE の直前に呼ぶ（例外を投げると削除は失敗する）。試験用。</summary>
    public Func<string, Exception?>? OnDelete { get; set; }

    public int PutCount { get; private set; }

    public int DeleteCount { get; private set; }

    public IReadOnlyDictionary<string, Stored> Objects => _objects;

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(_objects.ContainsKey(key));

    public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var stored) ? stored.Data : null);

    public Task PutAsync(string key, byte[] data, string contentType, string cacheControl, CancellationToken ct = default)
    {
        var error = OnPut?.Invoke(key, PutCount);
        if (error is not null) throw error;
        PutCount++;
        _objects[key] = new Stored(data.ToArray(), contentType, cacheControl);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var error = OnDelete?.Invoke(key);
        if (error is not null) throw error;
        DeleteCount++;
        _objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<(string Key, long Size)> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var (key, stored) in _objects.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) yield return (key, stored.Data.LongLength);
        }
        await Task.CompletedTask;
    }
}
