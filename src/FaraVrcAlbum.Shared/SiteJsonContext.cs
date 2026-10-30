using System.Text.Json;
using System.Text.Json.Serialization;
using FaraVrcAlbum.Models;

namespace FaraVrcAlbum;

/// <summary>data/*.json の読み書き（ソース生成。WebAssembly のトリミングでも壊れない）。</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(SiteSettings))]
[JsonSerializable(typeof(PostSource))]
[JsonSerializable(typeof(PostPage))]
[JsonSerializable(typeof(List<PostSummary>))]
[JsonSerializable(typeof(List<WorldSummary>))]
[JsonSerializable(typeof(WorldPage))]
[JsonSerializable(typeof(List<OutfitSummary>))]
[JsonSerializable(typeof(OutfitPage))]
[JsonSerializable(typeof(List<TagSummary>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, int>>))]
[JsonSerializable(typeof(Dictionary<string, List<PhotoRef>>))]
[JsonSerializable(typeof(Dictionary<string, WorldMasterEntry>))]
[JsonSerializable(typeof(Dictionary<string, TagMasterEntry>))]
[JsonSerializable(typeof(WorldStatsFile))]
public sealed partial class SiteJsonContext : JsonSerializerContext
{
}
