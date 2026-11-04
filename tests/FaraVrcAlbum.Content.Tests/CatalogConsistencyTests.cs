using System.Text.Json.Nodes;
using Xunit;

namespace FaraVrcAlbum.Content.Tests;

/// <summary>
/// 記事の写真が、台帳（content/catalog/photos.json）と食い違わないこと（オフライン。R2 には触らない）。
/// 手元は本物の content/、CI は samples/content を検査する（<see cref="RepoRoot.ContentDir"/>）。
/// 記事の写真の寸法・撮影時刻・色・ワールドは、台帳（取り込んだときの記録）と同じでなければならない。
/// </summary>
public class CatalogConsistencyTests
{
    [Fact]
    public void EveryPostPhotoMatchesItsUploadedLedgerEntry()
    {
        var content = RepoRoot.ContentDir();
        var ledger = JsonNode.Parse(File.ReadAllText(Path.Combine(content, "catalog", "photos.json")))!.AsObject();
        var byKey = ledger.Select(pair => pair.Value!.AsObject()).ToDictionary(entry => entry["key"]!.GetValue<string>(), StringComparer.Ordinal);
        var worlds = JsonNode.Parse(File.ReadAllText(Path.Combine(content, "worlds.json")))!.AsObject();
        var problems = new List<string>();
        var photoCount = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(content, "posts"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var post = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var slug = post["slug"]!.GetValue<string>();
            foreach (var node in post["photos"]?.AsArray() ?? new JsonArray())
            {
                var photo = node!.AsObject();
                photoCount++;
                var key = photo["key"]!.GetValue<string>();
                if (!byKey.TryGetValue(key, out var entry))
                {
                    problems.Add($"{slug}: {key} が台帳にありません");
                    continue;
                }
                if (entry["state"]?.GetValue<string>() != "uploaded") problems.Add($"{slug}: {key} の台帳の状態が uploaded ではありません");
                foreach (var field in new[] { "w", "h", "takenAt", "color" })
                {
                    if (!JsonNode.DeepEquals(photo[field], entry[field])) problems.Add($"{slug}: {key} の {field} が台帳と違います（記事 {photo[field]} / 台帳 {entry[field]}）");
                }
                var worldId = photo["world"]?["id"]?.GetValue<string>();
                if (worldId != entry["world"]?["id"]?.GetValue<string>()) problems.Add($"{slug}: {key} のワールド ID が台帳と違います");
                if (worldId is not null)
                {
                    var master = worlds[worldId]?.AsObject();
                    if (master is null) problems.Add($"{slug}: {key} のワールド {worldId} が content/worlds.json にありません");
                    else if (photo["world"]?["name"]?.GetValue<string>() != master["name"]?.GetValue<string>()) problems.Add($"{slug}: {key} のワールド名がマスタの name と違います");
                }
            }
        }

        Assert.True(photoCount > 0, "記事に写真がありません");
        Assert.Empty(problems);
    }
}
