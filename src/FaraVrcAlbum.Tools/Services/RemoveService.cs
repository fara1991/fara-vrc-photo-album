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
    IReadOnlyList<string> PostPaths,
    IReadOnlyList<string> PurgeUrls);

/// <summary>
/// 写真の削除（計画書 §5.5.6）: 記事の photos[] から外して検査 → R2 の 4 ファイルを削除 → 台帳を removed（ハッシュは残す）→ 撮影者の登録簿の後始末。
/// 表紙（cover、省略時は先頭の写真）や本文に残っている写真は、先に止める（一覧を出す）。
/// 途中で失敗したら、R2 の削除より前なら記事を元に戻す（台帳は uploaded のまま、やり直せる）。
/// </summary>
public static class RemoveService
{
    public static RemovePlan Plan(string key, PhotoLedger ledger, ContentEditor editor, string imageBaseUrl)
    {
        var entry = ledger.FindByKey(key) ?? throw new InvalidOperationException($"台帳にありません: {key}");
        if (entry.State == LedgerStates.Removed) throw new InvalidOperationException($"この写真は削除済みです: {key}");
        if (entry.State != LedgerStates.Uploaded) throw new InvalidOperationException($"台帳の状態が uploaded ではありません: {key}（{entry.State}）");

        var blockers = new List<string>();
        var postPaths = new List<string>();
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
            if (post["photos"] is JsonArray photos && photos.Any(p => p?["key"]?.GetValue<string>() == key)) postPaths.Add(path);
        }
        if (blockers.Count > 0) throw new InvalidOperationException("参照が残っているため削除を止めました:\n  " + string.Join("\n  ", blockers));

        var suffixes = entry.Bytes.Keys.Select(SuffixOf).ToList();
        return new RemovePlan(
            key,
            entry,
            suffixes.Select(s => key + s).ToList(),
            postPaths,
            suffixes.Select(s => imageBaseUrl + key + s).ToList());
    }

    /// <summary>
    /// 順序（通常）: (1) 記事から外して検査 → (2) R2 の削除 → (3) 台帳を removed にして保存 → (4) 撮影者の登録簿の後始末。
    /// (1) か (2) で落ちたら記事を戻す（台帳は uploaded のまま。やり直せる）。
    /// </summary>
    public static async Task ApplyAsync(RemovePlan plan, string bucket, PhotoLedger ledger, string ledgerPath, IObjectStore store, ContentEditor editor, DateTimeOffset now, CancellationToken ct = default)
    {
        // 削除は本番バケットだけ（試験用を渡せば、何も書く前に拒否する）
        RepoContext.RequireProductionBucket(bucket, "削除");
        var snapshot = new FileSnapshot();
        foreach (var path in plan.PostPaths) snapshot.Capture(path);
        try
        {
            foreach (var path in plan.PostPaths)
            {
                var post = ContentEditor.ReadObject(path);
                if (post["photos"] is JsonArray photos)
                {
                    var kept = photos.Where(p => p?["key"]?.GetValue<string>() != plan.Key).Select(p => p!.DeepClone()).ToList();
                    post["photos"] = new JsonArray(kept.ToArray());
                }
                ContentEditor.WriteNode(path, post);
            }
            editor.ValidateOrThrow();

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
                    // 途中で止まった（一部だけ消えた）。記事と台帳は戻し、同じ削除をやり直せる状態にする
                    throw new RemoveIncompleteException(plan.Key, deleted, plan.R2Keys.Count, e);
                }
            }
        }
        catch
        {
            snapshot.Restore();
            throw;
        }

        // 台帳を removed にする（R2 の削除は済んでいる。ここから先で失敗しても、記事は戻さない）
        plan.Entry.State = LedgerStates.Removed;
        plan.Entry.RemovedAt = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        ledger.Save(ledgerPath);
    }

    /// <summary>
    /// R2 の削除が途中で止まった（写真の一部だけ消えた）。記事と台帳は元のまま。同じコマンドを再実行すると続きから完了する（削除は冪等）。
    /// verify と audit で欠けが分かる。IOException のままにして、呼び出し側の扱い（通信エラー）は変えない。
    /// </summary>
    public sealed class RemoveIncompleteException(string key, int deleted, int total, Exception inner)
        : IOException($"写真 {key} の削除が途中で止まりました（R2 から {deleted}/{total} 件を削除済み）。" +
                      "記事と台帳は元のままです（一部の画像は R2 に無いため、表示が欠けることがあります）。" +
                      "同じコマンドを再実行すると続きから完了します（削除は冪等）。verify と audit で欠けを確認できます。" +
                      $" 原因: {inner.Message}", inner);

    /// <summary>台帳の bytes のキー（"640"・"1280"・"2048"・"og"）からファイルの接尾辞を作る。</summary>
    public static string SuffixOf(string name) => name == "og" ? "_og.jpg" : $"_{name}.webp";
}
