using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Tools.Tests;

/// <summary>テスト用の一時フォルダ（bin 配下。%TEMP% は使わない）。</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
    }
}

/// <summary>
/// 一時的なリポジトリ（FaraVrcAlbum.sln・appsettings.json・content/）。ContentLoader がそのまま読める最小構成。
/// </summary>
internal sealed class RepoFixture : IDisposable
{
    public const string Key1 = "2026-10/20261023-213312-345_a1b2c3d4";
    public const string Key2 = "2026-10/20261023-214001-120_0f1e2d3c";
    public const string WorldOld = "wrld_00000000-0000-4000-8000-000000000001";
    public const string WorldNew = "wrld_951b8ef4-bc5a-47c5-a156-2b62057d313f";

    private readonly TempDir _dir = new();

    public RepoFixture()
    {
        File.WriteAllText(System.IO.Path.Combine(Root, "FaraVrcAlbum.sln"), "");
        File.WriteAllText(System.IO.Path.Combine(Root, "appsettings.json"), "{ \"ImageBaseUrl\": \"https://img.example.test/\" }");
        Directory.CreateDirectory(ContentDir);
        Directory.CreateDirectory(Posts);
        Write(Worlds, """
            {
              "wrld_00000000-0000-4000-8000-000000000001": { "name": "旧名のワールド", "aliases": [] }
            }
            """);
        Write(Tags, "{ \"night\": { \"name\": \"夜景\" } }");
    }

    public string Root => _dir.Path;

    public string ContentDir => System.IO.Path.Combine(Root, "content");

    public string Posts => System.IO.Path.Combine(ContentDir, "posts");

    public string Worlds => System.IO.Path.Combine(ContentDir, "worlds.json");

    public string Tags => System.IO.Path.Combine(ContentDir, "tags.json");

    public string LedgerPath => System.IO.Path.Combine(ContentDir, "catalog", "photos.json");

    public ContentEditor Editor => new(ContentDir);

    public RepoContext Repo => new(Root);

    public string PostPath(string slug) => System.IO.Path.Combine(Posts, slug + ".json");

    public static void Write(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>公開の記事 1 件（写真 Key1 と Key2。Key1 が表紙）。</summary>
    public void WriteExistingPost(string slug = "old-post", string? cover = Key1)
    {
        var coverLine = cover is null ? "" : $"\"cover\": \"{cover}\",";
        Write(PostPath(slug), $$"""
            {
              "schema": 1,
              "slug": "{{slug}}",
              "title": "既存の記事",
              "publishedAt": "2026-10-24T20:00:00+09:00",
              "draft": false,
              "noindex": false,
              {{coverLine}}
              "tags": [],
              "body": "本文です。",
              "photos": [
                {
                  "key": "{{Key1}}", "w": 640, "h": 360, "takenAt": "2026-10-23T21:33:12+09:00", "color": "1b2a3c",
                  "world": { "id": "{{WorldOld}}", "name": "旧名のワールド" }
                },
                {
                  "key": "{{Key2}}", "w": 1280, "h": 720, "takenAt": "2026-10-23T21:40:01+09:00", "color": "0f1e2d"
                }
              ]
            }
            """);
    }

    /// <summary>台帳（uploaded 2 件・removed 1 件）。</summary>
    public PhotoLedger Ledger(params (string Key, string Sha, string State, string TakenAt)[] rows)
    {
        var ledger = PhotoLedger.Empty();
        foreach (var row in rows)
        {
            ledger.Put(row.Sha, new LedgerEntry
            {
                Key = row.Key,
                State = row.State,
                TakenAt = row.TakenAt,
                SourceName = "VRChat_test.png",
                W = 640,
                H = 360,
                Color = "abcdef",
                World = new LedgerWorld { Id = WorldNew, Name = "新しいワールド" },
                Bytes = new Dictionary<string, long> { ["640"] = 1, ["1280"] = 1, ["2048"] = 1, ["og"] = 1 },
                Encoder = 1,
            });
        }
        return ledger;
    }

    public void Dispose() => _dir.Dispose();
}

/// <summary>HTTP を差し替える（公開の画像の検査用）。</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(respond(request));
}

internal static class FakeImages
{
    /// <summary>VP8L の 30 バイトのヘッダー（寸法だけが分かればよい）。</summary>
    public static byte[] WebpHeader(int width, int height)
    {
        var bytes = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        Encoding.ASCII.GetBytes("VP8L").CopyTo(bytes, 12);
        bytes[20] = 0x2F;
        var bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BitConverter.GetBytes(bits).CopyTo(bytes, 21);
        return bytes;
    }

    public static HttpResponseMessage Response(HttpRequestMessage request, string contentType, string cacheControl, byte[] body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(body),
            RequestMessage = request,
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        response.Headers.TryAddWithoutValidation("Cache-Control", cacheControl);
        return response;
    }
}
