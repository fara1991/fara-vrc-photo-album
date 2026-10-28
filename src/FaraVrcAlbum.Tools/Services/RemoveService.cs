using System.Globalization;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>削除の計画（何を消すか）。Apply の前に表示して確かめる。</summary>
public sealed record RemovePlan(
    string Key,
    LedgerEntry Entry,
    IReadOnlyList<string> R2Keys,
    IReadOnlyList<string> PurgeUrls);

/// <summary>
/// 写真の削除: R2 の 4 ファイルを削除 → 台帳を removed（ハッシュは残す）。
/// **記事（非公開のものも含む）の写真の一覧・表紙・本文で使われている写真は、削除させない**（先に記事から外す。一覧を出して止める）。記事は書き換えない。
/// 途中で失敗したら、台帳は uploaded のまま（同じ削除をやり直せる）。
/// </summary>
public static class RemoveService
{
    public static RemovePlan Plan(string key, PhotoLedger ledger, ContentEditor editor, string imageBaseUrl)
    {
        var entry = ledger.FindByKey(key) ?? throw new InvalidOperationException($"台帳にありません: {key}");
        if (entry.State == LedgerStates.Removed) throw new InvalidOperationException($"この写真は削除済みです: {key}");
        if (entry.State != LedgerStates.Uploaded) throw new InvalidOperationException($"台帳の状態が uploaded ではありません: {key}（{entry.State}）");

        var blockers = new List<string>();
        foreach (var path in editor.PostFiles())
        {
            var post = ContentEditor.ReadObject(path);
            var name = Path.GetFileName(path);
            // 表紙は cover、省略時は先頭の写真（SiteModel と同じ決め方）
            var cover = post["cover"]?.GetValue<string>() ?? (post["photos"] as JsonArray)?.FirstOrDefault()?["key"]?.GetValue<string>();
            if (cover == key) blockers.Add($"{name}: 表紙（cover、省略時は先頭の写真）がこの写真です（別の写真に変えてから削除）");
            if ((post["body"]?.GetValue<string>() ?? "").Contains(key, StringComparison.Ordinal))
            {
                blockers.Add($"{name}: 本文にこの写真のキーがあります（本文から外してから削除）");
            }
            if (post["photos"] is JsonArray photos && photos.Any(p => p?["key"]?.GetValue<string>() == key))
            {
                blockers.Add($"{name}: 記事で使われています（記事の写真から外してから削除）");
            }
        }
        if (blockers.Count > 0) throw new InvalidOperationException("記事で使われているため削除を止めました:\n  " + string.Join("\n  ", blockers.Distinct()));

        var suffixes = entry.Bytes.Keys.Select(SuffixOf).ToList();
        return new RemovePlan(
            key,
            entry,
            suffixes.Select(s => key + s).ToList(),
            suffixes.Select(s => imageBaseUrl + key + s).ToList());
    }

    /// <summary>
    /// 順序: (1) R2 の 4 ファイルを削除 → (2) 台帳を removed にして保存。記事は書き換えない（使われている写真は、計画の段階で止めている）。
    /// (1) で落ちたら台帳は uploaded のまま（やり直せる）。
    /// </summary>
    public static async Task ApplyAsync(RemovePlan plan, string bucket, PhotoLedger ledger, string ledgerPath, IObjectStore store, DateTimeOffset now, CancellationToken ct = default)
    {
        // 削除は本番バケットだけ（試験用を渡せば、何も書く前に拒否する）
        RepoContext.RequireProductionBucket(bucket, "削除");
        var deleted = 0;
        foreach (var objectKey in plan.R2Keys)
        {
            try
            {
                await store.DeleteAsync(objectKey, ct);
                deleted++;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // 途中で止まった（一部だけ消えた）。台帳は uploaded のまま、同じ削除をやり直せる状態にする
                throw new RemoveIncompleteException(plan.Key, deleted, plan.R2Keys.Count, e);
            }
        }

        // 台帳を removed にする（R2 の削除は済んでいる）
        plan.Entry.State = LedgerStates.Removed;
        plan.Entry.RemovedAt = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        ledger.Save(ledgerPath);
    }

    /// <summary>
    /// R2 の削除が途中で止まった（写真の一部だけ消えた）。台帳は元のまま。同じコマンドを再実行すると続きから完了する（削除は冪等）。
    /// verify と audit で欠けが分かる。IOException のままにして、呼び出し側の扱い（通信エラー）は変えない。
    /// </summary>
    public sealed class RemoveIncompleteException(string key, int deleted, int total, Exception inner)
        : IOException($"写真 {key} の削除が途中で止まりました（R2 から {deleted}/{total} 件を削除済み）。" +
                      "台帳は元のままです（一部の画像は R2 に無いため、表示が欠けることがあります）。" +
                      "同じコマンドを再実行すると続きから完了します（削除は冪等）。verify と audit で欠けを確認できます。" +
                      $" 原因: {inner.Message}", inner);

    /// <summary>台帳の bytes のキー（"640"・"1280"・"2048"・"og"）からファイルの接尾辞を作る。</summary>
    public static string SuffixOf(string name) => name == "og" ? "_og.jpg" : $"_{name}.webp";
}
