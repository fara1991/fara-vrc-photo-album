using System.Text;
using System.Text.Json;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum.Build;

/// <summary>data/*.json を書く（アプリが読む）。人の名前は書かない。</summary>
internal static class DataWriter
{
    // 日本語を \uXXXX にしない（ファイルが 3 倍近く大きくなるため）。アプリ（Shared の SiteJsonContext）と同じ命名規則・null の扱い。
    // fetch で読む JSON なので、< > & もそのまま出してよい（HTML の中に埋め込まない）
    internal static readonly SiteJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    public static void Write(string dataDir, SiteModel model)
    {
        Directory.CreateDirectory(Path.Combine(dataDir, "posts"));
        Save(Path.Combine(dataDir, "site.json"), JsonSerializer.Serialize(model.Settings, Context.SiteSettings));
        Save(Path.Combine(dataDir, "posts-index.json"), JsonSerializer.Serialize(model.Summaries.ToList(), Context.ListPostSummary));
        Save(Path.Combine(dataDir, "archive.json"), JsonSerializer.Serialize(model.ArchiveCounts(), Context.DictionaryStringDictionaryStringInt32));
        foreach (var post in model.Posts)
            Save(Path.Combine(dataDir, "posts", post.Slug + ".json"), JsonSerializer.Serialize(model.ToPage(post), Context.PostPage));

        Save(Path.Combine(dataDir, "tags.json"), JsonSerializer.Serialize(model.TagSummaries(), Context.ListTagSummary));

        Save(Path.Combine(dataDir, "worlds.json"), JsonSerializer.Serialize(model.WorldSummaries(), Context.ListWorldSummary));
        Directory.CreateDirectory(Path.Combine(dataDir, "worlds"));
        foreach (var worldId in model.PhotosByWorld.Keys)
        {
            var page = new WorldPage { Id = worldId, Name = model.WorldName(worldId), Image = model.Content.Worlds[worldId].Image, Stats = model.WorldStatsOf(worldId), StatsFetchedAt = model.WorldStatsOf(worldId) is null ? null : model.WorldStatsFetchedAt, Photos = model.WorldPhotos(worldId) };
            Save(Path.Combine(dataDir, "worlds", WorldId.ToPathSegment(worldId) + ".json"), JsonSerializer.Serialize(page, Context.WorldPage));
        }

        // 服の一覧と、服ごとの写真（その服を着て撮った写真が 1 枚以上ある服だけ）
        Save(Path.Combine(dataDir, "outfits.json"), JsonSerializer.Serialize(model.OutfitSummaries(), Context.ListOutfitSummary));
        Directory.CreateDirectory(Path.Combine(dataDir, "outfits"));
        foreach (var outfitId in model.PhotosByOutfit.Keys)
        {
            var master = model.OutfitMaster(outfitId);
            var outfitPage = new OutfitPage
            {
                Id = outfitId, Name = master.Name, Shop = master.Shop, Url = master.Url,
                Thumbnail = master.Thumbnail, Description = OutfitDescription.Summarize(master.Description), Tags = master.Tags is { Count: > 0 } ? master.Tags : null,
                PublishedAt = master.PublishedAt, UpdatedAt = master.UpdatedAt, FetchedAt = master.FetchedAt,
                Photos = model.OutfitPhotos(outfitId),
            };
            Save(Path.Combine(dataDir, "outfits", outfitId + ".json"), JsonSerializer.Serialize(outfitPage, Context.OutfitPage));
        }
    }

    internal static void Save(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }
}
