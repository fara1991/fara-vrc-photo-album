using System.Globalization;
using System.Text;
using FaraVrcAlbum;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.Demo;

/// <summary>デモ用の作業フォルダの場所。</summary>
/// <summary>デモの場所。SourceRoot は VRChat の写真のフォルダの代わり（yyyy-mm の下に写真がある）。</summary>
public sealed record DemoLayout(string RepoRoot, string UploadRoot, string SourceRoot);

/// <summary>
/// デモ用の合成データ（実在の人名・実在のワールド・実写真は使わない）。画面の確認と、スクリーンショット用。
/// リポジトリは作業用フォルダに作る（実リポジトリの .env.local・台帳には触れない）。R2 は <see cref="SeedStore"/> のメモリだけ。
/// 含むもの: 取り込み待ちの PNG（XMP あり・なし・2025-11 より前・登録済み・削除済み）、本番の台帳（uploaded 4 件・removed 1 件）、
/// 記事（公開 1・非公開 1）、R2 の写真（孤児を 1 つ含む）。
/// </summary>
public static class DemoWorkspace
{
    public const string WorldA = "wrld_11111111-1111-4111-8111-111111111111";
    public const string WorldB = "wrld_22222222-2222-4222-8222-222222222222";
    public const string WorldC = "wrld_33333333-3333-4333-8333-333333333333";

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>作業用フォルダにデモを作る（既にあれば作り直す）。</summary>
    public static DemoLayout Create(string root)
    {
        PrepareRoot(root);
        var repo = Path.Combine(root, "repo");
        var upload = Path.Combine(root, "upload");
        var source = Path.Combine(root, "VRChat");
        foreach (var dir in new[]
                 {
                     Path.Combine(repo, "content", "posts"), Path.Combine(repo, "content", "catalog"), Path.Combine(repo, "local"),
                     Path.Combine(source, "2025-10"), Path.Combine(source, "2026-10"), Path.Combine(upload, "inbox"),
                     Path.Combine(upload, "uploaded", "2026-10"), Path.Combine(upload, "_work"),
                 })
        {
            Directory.CreateDirectory(dir);
        }
        Write(Path.Combine(repo, "FaraVrcAlbum.sln"), "");
        Write(Path.Combine(repo, "appsettings.json"), "{ \"ImageBaseUrl\": \"https://demo.invalid/\" }\n");
        Write(Path.Combine(repo, "content", "worlds.json"), $$"""
            {
              "{{WorldA}}": { "name": "デモ展望台", "aliases": [] },
              "{{WorldB}}": { "name": "デモの夜の街", "aliases": [] },
              "{{WorldC}}": { "name": "デモの森", "aliases": [] }
            }
            """);
        Write(Path.Combine(repo, "content", "tags.json"), "{ \"night\": { \"name\": \"夜景\" } }");

        var ledger = PhotoLedger.Empty();
        // アップロード用フォルダを設定に書いておく（写真の場所は、起動の引数で渡す。設定では変えられない）
        Write(Path.Combine(repo, "local", "album.local.json"),
            new System.Text.Json.Nodes.JsonObject { ["uploadRoot"] = upload }.ToJsonString());
        var inbox = source;

        // VRChat の写真（取り込みの候補）。台帳の状態の違いも作る
        var inboxE = Inbox(inbox, "2026-10", "VRChat_2026-10-25_09-15-30.500_640x360.png", Taken("2026-10-25 09:15:30.500"), WorldA, "デモ展望台", 180, xmp: true);
        Register(ledger, inboxE.Sha, inboxE.Taken, LedgerStates.Uploaded, WorldA, "デモ展望台");
        Inbox(inbox, "2026-10", "VRChat_2026-10-23_21-33-12.345_640x360.png", Taken("2026-10-23 21:33:12.345"), WorldA, "デモ展望台", 90, xmp: true);
        Inbox(inbox, "2026-10", "VRChat_2026-10-23_21-40-01.120_640x360.png", Taken("2026-10-23 21:40:01.120"), WorldB, "デモの夜の街", 120, xmp: true);
        Inbox(inbox, "2026-10", "VRChat_2026-10-24_20-05-44.010_640x360.png", Taken("2026-10-24 20:05:44.010"), WorldC, "デモの森", 150, xmp: true);
        Inbox(inbox, "2026-10", "VRChat_2026-10-24_22-10-00.000_640x360.png", Taken("2026-10-24 22:10:00.000"), null, null, 60, xmp: false);
        var inboxG = Inbox(inbox, "2026-10", "VRChat_2026-10-26_12-00-00.000_640x360.png", Taken("2026-10-26 12:00:00.000"), WorldB, "デモの夜の街", 100, xmp: true);
        Register(ledger, inboxG.Sha, inboxG.Taken, LedgerStates.Removed, WorldB, "デモの夜の街");
        Inbox(inbox, "2025-10", "VRChat_2025-10-02_10-00-00.000_640x360.png", Taken("2025-10-02 10:00:00.000"), WorldA, "デモ展望台", 70, xmp: true);

        // 記事に入っている写真（台帳の uploaded のみ。PNG は inbox に無い）
        var h = Uploaded(ledger, "2026-10-20 18:00:00.000", WorldA, "デモ展望台");
        var i = Uploaded(ledger, "2026-10-21 19:30:10.250", WorldC, "デモの森");
        var j = Uploaded(ledger, "2026-10-22 20:20:20.000", WorldB, "デモの夜の街");

        ledger.Save(Path.Combine(repo, "content", "catalog", "photos.json"));

        WritePost(repo, "demo-first", "デモの記事（公開）", "2026-10-24T20:00:00+09:00", draft: false,
            photos: [h, i, inboxE.Key]);
        WritePost(repo, "demo-draft", "デモの非公開の記事", "2026-10-26T20:00:00+09:00", draft: true,
            photos: [j]);

        return new DemoLayout(repo, upload, source);
    }

    /// <summary>デモの作業フォルダの印（これがあるフォルダだけ作り直す）。</summary>
    public const string Marker = ".fara-album-demo";

    /// <summary>
    /// 作業フォルダを用意する。既存のフォルダは、デモの印（<see cref="Marker"/>）があるものだけ消す。
    /// 印の無い非空のフォルダ、リポジトリ（FaraVrcAlbum.sln がある）は拒否する（実データを消さないため）。
    /// </summary>
    public static void PrepareRoot(string root)
    {
        var full = Path.GetFullPath(root);
        if (Directory.Exists(full))
        {
            if (File.Exists(Path.Combine(full, "FaraVrcAlbum.sln")))
            {
                throw new InvalidOperationException("リポジトリのフォルダはデモの作業フォルダにできません（--demo には空のフォルダ・新しいフォルダを指定）");
            }
            var isDemo = File.Exists(Path.Combine(full, Marker));
            var isEmpty = !Directory.EnumerateFileSystemEntries(full).Any();
            if (!isDemo && !isEmpty)
            {
                throw new InvalidOperationException("デモの印（.fara-album-demo）が無い非空のフォルダには作れません（空のフォルダか、新しいフォルダを指定）");
            }
            if (isDemo) Directory.Delete(full, recursive: true);
        }
        Directory.CreateDirectory(full);
        File.WriteAllText(Path.Combine(full, Marker), "Fara Photo Album の合成データ（デモ）。実データではありません。\n");
    }

/// <summary>デモの R2（本番バケット）に、写真・孤児を入れる。</summary>
    public static async Task SeedStore(IObjectStore store, PhotoLedger ledger, CancellationToken ct = default)
    {
        foreach (var entry in ledger.BySha.Values.Where(x => x.State == LedgerStates.Uploaded))
        {
            foreach (var suffix in new[] { "_640.webp", "_1280.webp", "_2048.webp" })
            {
                await store.PutAsync(entry.Key + suffix, [0x52, 0x49, 0x46, 0x46], "image/webp", ObjectHeaders.ImmutableImage, ct);
            }
        }
        // 参照の無いオブジェクト（監査の「孤児」を見せるため）
        await store.PutAsync("2026-10/20261009-010101-000_deadbeef_640.webp", [0x52], "image/webp", ObjectHeaders.ImmutableImage, ct);

    }

    private sealed record InboxShot(string Sha, DateTimeOffset Taken, string Key);

    private static DateTimeOffset Taken(string text) =>
        new(DateTime.ParseExact(text, "yyyy-MM-dd HH:mm:ss.fff", Inv), PhotoKeys.Jst);

    private static InboxShot Inbox(string inbox, string folder, string fileName, DateTimeOffset taken, string? worldId, string? worldName, int shade, bool xmp)
    {
        string? xmpText = null;
        if (xmp)
        {
            var created = taken.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", Inv);
            xmpText = SyntheticImages.Xmp(worldId ?? WorldA, worldName ?? "デモ展望台", created);
        }
        var png = SyntheticImages.Png(640, 360, shade, xmpText);
        var path = Path.Combine(inbox, folder, fileName);
        File.WriteAllBytes(path, png);
        // 書き込み中の判定（5 秒以内の更新は飛ばす）に掛からないよう、少し前の時刻にする
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
        var sha = Hashing.Sha256Hex(png);
        return new InboxShot(sha, taken, PhotoKeys.Build(taken, sha));
    }

    /// <summary>台帳に行を入れる（PNG の無い、記事だけの写真にも使う）。</summary>
    private static void Register(PhotoLedger ledger, string sha, DateTimeOffset taken, string state, string worldId, string worldName)
    {
        ledger.Put(sha, new LedgerEntry
        {
            Key = PhotoKeys.Build(taken, sha),
            State = state,
            UploadedAt = "2026-10-25T00:00:00Z",
            RemovedAt = state == LedgerStates.Removed ? "2026-10-25T01:00:00Z" : null,
            TakenAt = PhotoKeys.TakenAtText(taken),
            SourceName = "demo.png",
            W = 640,
            H = 360,
            Color = "4a6a8a",
            World = new LedgerWorld { Id = worldId, Name = worldName },
            Bytes = new Dictionary<string, long> { ["640"] = 1, ["1280"] = 1, ["2048"] = 1 },
            Encoder = 1,
        });
    }

    private static string Uploaded(PhotoLedger ledger, string takenText, string worldId, string worldName)
    {
        var taken = Taken(takenText);
        var sha = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("demo-" + takenText));
        Register(ledger, sha, taken, LedgerStates.Uploaded, worldId, worldName);
        return PhotoKeys.Build(taken, sha);
    }

    private static void WritePost(string repo, string slug, string title, string publishedAt, bool draft, IReadOnlyList<string> photos)
    {
        var items = string.Join(",\n", photos.Select(PhotoJson));
        Write(Path.Combine(repo, "content", "posts", slug + ".json"), $$"""
            {
              "schema": 1,
              "slug": "{{slug}}",
              "title": "{{title}}",
              "publishedAt": "{{publishedAt}}",
              "draft": {{(draft ? "true" : "false")}},
              "noindex": false,
              "tags": ["night"],
              "body": "デモ用の本文です（合成データ）。",
              "photos": [
            {{items}}
              ]
            }
            """);
    }

    private static string PhotoJson(string key) =>
        $"    {{ \"key\": \"{key}\", \"w\": 640, \"h\": 360, \"takenAt\": \"{TakenAtFromKey(key)}\", \"color\": \"4a6a8a\" }}";

    /// <summary>キー（yyyy-mm/yyyyMMdd-HHmmss-fff_…）から JSON の撮影日時（JST・秒）を作る。デモの写真だけに使う。</summary>
    private static string TakenAtFromKey(string key)
    {
        var stamp = key.Split('/')[1][..15];
        var date = DateTime.ParseExact(stamp, "yyyyMMdd-HHmmss", Inv);
        return date.ToString("yyyy-MM-ddTHH:mm:ss", Inv) + "+09:00";
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
