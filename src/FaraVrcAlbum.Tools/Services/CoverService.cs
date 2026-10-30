using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// カバー写真の OGP 画像（_og.jpg）を作る（計画書 §5.2・人と決定: og はカバー写真だけ）。
/// 元の PNG は uploaded\yyyy-mm または inbox\yyyy-mm から、台帳の内容ハッシュで探す。
/// 既に R2 にあれば何もしない。
/// </summary>
public static class CoverService
{
    public sealed record CoverPlan(string Key, LedgerEntry Entry, string SourcePath, bool AlreadyInR2);

    public static async Task<CoverPlan> PlanAsync(string key, PhotoLedger ledger, string uploadRoot, IObjectStore store, CancellationToken ct = default, string? sourceRoot = null)
    {
        var sha = ledger.ShaOfKey(key) ?? throw new InvalidOperationException($"台帳にありません: {key}");
        var entry = ledger.FindBySha(sha)!;
        if (entry.State != LedgerStates.Uploaded) throw new InvalidOperationException($"台帳の状態が uploaded ではありません: {key}（{entry.State}）");

        var month = PhotoKeys.MonthOf(key);
        var dirs = new List<string> { Path.Combine(uploadRoot, "uploaded", month), Path.Combine(uploadRoot, "inbox", month) };
        if (sourceRoot is not null) dirs.Add(Path.Combine(sourceRoot, month));                  // VRChat の写真格納フォルダ（直接アップロードした写真の元）
        var candidates = dirs
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.png", SearchOption.AllDirectories));
        foreach (var file in candidates.Order(StringComparer.Ordinal))
        {
            if (Hashing.Sha256Hex(await File.ReadAllBytesAsync(file, ct)) == sha)
            {
                return new CoverPlan(key, entry, file, await store.ExistsAsync(key + "_og.jpg", ct));
            }
        }
        throw new FileNotFoundException($"元の PNG が uploaded\\{month}・inbox\\{month}{(sourceRoot is null ? "" : "・VRChat の写真格納フォルダ")} に見つかりません（{key}）");
    }

    /// <summary>_og.jpg を作って R2 に置き、台帳の bytes["og"] を更新する。既にあれば何もしない。</summary>
    public static async Task ApplyAsync(CoverPlan plan, string bucket, PhotoLedger ledger, string ledgerPath, IObjectStore store, CancellationToken ct = default)
    {
        // OGP は本番の R2 にだけ置く（試験用を渡せば拒否）
        RepoContext.RequireProductionBucket(bucket, "表紙の OGP の作成");
        // 変換は決定的なので、既に R2 にあっても作り直してバイト数を台帳に控える（削除のときに取りこぼさないため）
        var og = PhotoEncoder.EncodeOg(await File.ReadAllBytesAsync(plan.SourcePath, ct));
        if (!plan.AlreadyInR2)
        {
            await store.PutAsync(plan.Key + og.Suffix, og.Data, og.ContentType, ObjectHeaders.ImmutableImage, ct);
        }
        plan.Entry.Bytes["og"] = og.Data.Length;
        ledger.Save(ledgerPath);
    }
}
