using System.Text.Json;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools.Services;

/// <summary>
/// ワールドの「画像」と「情報」（訪問数・説明・作者など）を、VRChat の公開 API から、足りない分だけまとめて取る（アプリの「ワールドの画像・情報を取得する」ボタン）。
/// 公開（dev・本番）とは別の操作で、公開の前に、ここで済ませておく（公開は、記事の中身をサイトにするだけ）。
///   ・画像の無いワールド → 画像を取り、WebP（512・1024）にして R2 に置き、worlds.json の image に書く
///   ・情報の無いワールド → world-stats.json に書く（取得日時は、前に全体を取った日時のまま。全ワールドの更新は、ふだんは別のボタン（album world-stats）で行う）
/// 1 つのワールドの情報は 1 回だけ問い合わせる（画像の置き場所と情報が、同じ応答に入っている）。VRChat には約 1 分おきに読む（<see cref="WorldImageService.Interval"/>）。
/// 取れなかったワールド（削除された・imageUrl が無い・繋がらないなど）は、local/ に記録して、一定の時間（<see cref="RetryAfter"/>）は問い合わせない（毎回 1 分ずつ待たないため）。
/// 429 は失敗に数えず、そこで止める（続きは、時間をおいてもう一度）。
/// </summary>
public static class WorldFetchService
{
    /// <summary>前に失敗したワールドを、もう一度問い合わせるまでの時間（「もう一度取得する」の指定があれば、待たない）。</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromHours(24);

    /// <summary>失敗の記録（local/world-fetch-state.json。Git に入れない）。</summary>
    public sealed class State
    {
        public Dictionary<string, Failure> Failures { get; set; } = new();

        public sealed class Failure
        {
            public string At { get; set; } = "";

            public string Reason { get; set; } = "";
        }

        public static State Load(string file)
        {
            try
            {
                if (!File.Exists(file)) return new State();
                return JsonSerializer.Deserialize<State>(File.ReadAllText(file), JsonFiles.Options) ?? new State();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return new State();                                                    // 読めなければ、記録なし（全部をもう一度問い合わせる）
            }
        }

        public void Save(string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            JsonFiles.WriteAtomic(file, JsonSerializer.Serialize(this, JsonFiles.Options));
        }

        public bool IsWaiting(string worldId, DateTimeOffset now) =>
            Failures.TryGetValue(worldId, out var f) && DateTimeOffset.TryParse(f.At, out var at) && now - at < RetryAfter;
    }

    /// <summary>取るものの数。Worlds は、問い合わせるワールドの数、Waiting は、前に失敗して待っているワールドの数。</summary>
    public sealed record Pending(int Worlds, int Images, int Stats, int Waiting)
    {
        public bool Any => Worlds > 0;

        /// <summary>かかる時間の目安（分）。問い合わせ（ワールドごとに 1 回）と画像の取得（1 回）が、それぞれ約 1 分おき。</summary>
        public int Minutes => Worlds + Images;
    }

    /// <summary>いま取るものの数（VRChat には問い合わせない）。</summary>
    public static Pending CountPending(ContentEditor editor, string stateFile, DateTimeOffset now, bool retryFailed)
    {
        var content = editor.LoadContent();
        var state = State.Load(stateFile);
        int worlds = 0, images = 0, stats = 0, waiting = 0;
        foreach (var (id, entry) in content.Worlds)
        {
            var (needImage, needStats) = Needs(content, id, entry);
            if (!needImage && !needStats) continue;
            if (!retryFailed && state.IsWaiting(id, now))
            {
                waiting++;
                continue;
            }
            worlds++;
            if (needImage) images++;
            if (needStats) stats++;
        }
        return new Pending(worlds, images, stats, waiting);
    }

    /// <summary>このワールドで足りないもの（画像・情報）。どちらも揃っていれば両方 false。</summary>
    private static (bool NeedImage, bool NeedStats) Needs(ContentSet content, string id, WorldMasterEntry entry) =>
        (entry.Image is null, content.WorldStats?.Worlds.ContainsKey(id) != true);

    /// <summary>今回取る対象か（足りないものがあり、前に失敗して待っている最中でない。retryFailed なら待ちは見ない）。</summary>
    private static bool ShouldFetch(ContentSet content, string id, WorldMasterEntry entry, State state, DateTimeOffset now, bool retryFailed)
    {
        var (needImage, needStats) = Needs(content, id, entry);
        return (needImage || needStats) && (retryFailed || !state.IsWaiting(id, now));
    }

    public sealed record Result(int Images, int Stats, IReadOnlyList<string> Problems, bool Stopped);

    /// <summary>
    /// 足りない画像・情報を取る。cancel 以外では例外にしない（理由は log と Problems に出す）。
    /// </summary>
    /// <param name="openStore">R2 を開く（画像を取るワールドがあるときだけ、VRChat に問い合わせる前に 1 回呼ぶ）。開けなければ、画像は取らず、情報だけ取る。Owner は、使い終わったら閉じる。</param>
    public static async Task<Result> FetchAsync(
        ContentEditor editor, HttpClient http, Func<(IObjectStore Store, IDisposable? Owner)> openStore, string stateFile,
        DateTimeOffset now, bool retryFailed, Action<string> log, CancellationToken ct = default)
    {
        var problems = new List<string>();
        var images = 0;
        var statsDone = 0;
        var stopped = false;
        var stats = new WorldStatsFile();                                                // 情報（world-stats.json）。下の content を読んだときの内容を、ループの間ずっと使う

        List<(string Id, string Name, bool NeedImage, bool NeedStats)> targets;
        State state;
        try
        {
            var content = editor.LoadContent();
            state = State.Load(stateFile);
            stats = content.WorldStats ?? new WorldStatsFile();
            targets = content.Worlds.OrderBy(w => w.Key, StringComparer.Ordinal)
                .Where(w => ShouldFetch(content, w.Key, w.Value, state, now, retryFailed))
                .Select(w =>
                {
                    var (needImage, needStats) = Needs(content, w.Key, w.Value);
                    return (Id: w.Key, Name: w.Value.Name, NeedImage: needImage, NeedStats: needStats);
                })
                .ToList();
            var waiting = content.Worlds.Count(w => (Needs(content, w.Key, w.Value) is (true, _) or (_, true)) && !retryFailed && state.IsWaiting(w.Key, now));
            if (waiting > 0) log($"前に取れなかったワールド {waiting} 件は、{RetryAfter.TotalHours:0} 時間たつまで問い合わせません（「前に失敗したワールドも、もう一度取得する」で取れます）");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var text = $"ワールドの画像・情報を確かめられませんでした: {e.Message}";
            log(text);
            return new Result(0, 0, [text], false);
        }
        if (targets.Count == 0)
        {
            log("取得するものはありません（画像・情報の足りないワールドがありません）");
            return new Result(0, 0, problems, false);
        }

        // R2 は、VRChat に問い合わせる前に開く（設定が足りないとき、1 件ごとに約 1 分の待ちが無駄にならないように）。開けなければ、情報だけ取る
        (IObjectStore Store, IDisposable? Owner)? opened = null;
        if (targets.Any(t => t.NeedImage))
        {
            try
            {
                opened = openStore();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var text = $"R2 の設定が足りないか開けないので、ワールドの画像は取りません（情報だけ取ります）: {e.Message}";
                log(text);
                problems.Add(text);
            }
        }

        try
        {
            log($"{targets.Count} 件のワールドについて、VRChat から取ります（1 回ごとに約 1 分かかります）");
            foreach (var (id, name, needImage, needStats) in targets)
            {
                ct.ThrowIfCancellationRequested();
                var failure = (string?)null;
                try
                {
                    var info = await WorldImageService.FetchInfoAsync(http, id, ct);
                    var wantImage = needImage && opened is not null;
                    if (needStats)
                    {
                        // 情報は、ループの前に読んだものに、書いた分だけ足して書く（ワールドごとに content 全体を読み直さない）。書けなかったら、足した分も戻す
                        var had = stats.Worlds.TryGetValue(id, out var previous);
                        stats.Worlds[id] = info.Stats;
                        try
                        {
                            WorldImageService.WriteStats(editor, stats, now, keepDate: true);
                        }
                        catch
                        {
                            if (had) stats.Worlds[id] = previous!;
                            else stats.Worlds.Remove(id);
                            throw;
                        }
                        statsDone++;
                        log($"  {name}: 情報を書きました（訪問 {info.Stats.Visits:N0}・お気に入り {info.Stats.Favorites:N0}）");
                    }
                    if (wantImage)
                    {
                        if (info.ImageUrl is null) failure = "応答に画像（imageUrl）がありません";
                        else
                        {
                            await PutImageAsync(editor, http, opened!.Value.Store, id, info.ImageUrl, ct);
                            images++;
                            log($"  {name}: 画像を R2 に置きました");
                        }
                    }
                }
                catch (WorldImageService.TooManyRequestsException)
                {
                    var text = "VRChat の API が 429（多すぎる）を返したので止めました。時間をおいてもう一度取得すると、続きから行います";
                    log(text);
                    problems.Add(text);
                    stopped = true;
                    break;
                }
                catch (Exception e) when (!ct.IsCancellationRequested && WorldImageService.IsPerWorldFailure(e))
                {
                    // 通信・応答の形・画像の変換（NetVips → InvalidDataException）・R2 への保存など、このワールドだけの失敗は 1 件の問題にして次へ進む。
                    // バグ（NullReference など）は握らず投げる
                    failure = WorldImageService.Describe(e);
                }

                if (failure is null) state.Failures.Remove(id);
                else
                {
                    var text = $"  {name}（{id}）: {failure}";
                    log(text);
                    problems.Add(text);
                    state.Failures[id] = new State.Failure { At = now.ToString("o"), Reason = WorldImageService.Limit(failure, 300) };
                }
                TrySave(state, stateFile, log);
            }
        }
        finally
        {
            opened?.Owner?.Dispose();
        }

        log($"ワールドの画像を {images} 件、情報を {statsDone} 件、書きました" + (problems.Count > 0 ? $"（問題 {problems.Count} 件）" : ""));
        return new Result(images, statsDone, problems, stopped);
    }

    /// <summary>画像を読み、WebP（512・1024）にして R2 に置き、worlds.json の image を書く（ApplyImagesAsync と同じ処理）。</summary>
    private static async Task PutImageAsync(ContentEditor editor, HttpClient http, IObjectStore store, string id, string imageUrl, CancellationToken ct)
    {
        var bytes = await WorldImageService.DownloadAsync(http, imageUrl, ct);
        await WorldImageService.StoreImageAsync(editor, store, id, VrchatLinks.WorldImageKey(id, imageUrl), bytes, ct);
    }

    private static void TrySave(State state, string stateFile, Action<string> log)
    {
        try
        {
            state.Save(stateFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"失敗の記録を書けませんでした（次も問い合わせます）: {e.Message}");
        }
    }
}
