using FaraVrcAlbum.Desktop.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FaraVrcAlbum.Desktop.Core.Tests;

/// <summary>BOOTH の data.db のコピー（一時フォルダ）の掃除: 古い消し残しは次の読み込みで消し、使用中（新しいもの）と関係ないフォルダは消さない。</summary>
public sealed class BoothLeftoverTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), "fara-booth-leftover-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var folder in _folders) if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        SqliteConnection.ClearAllPools();
        File.Delete(_db);
    }

    private void CreateDatabase()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _db, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE booth_items (id INTEGER PRIMARY KEY, thumbnail_url TEXT); INSERT INTO booth_items VALUES (1, NULL);";
        command.ExecuteNonQuery();
    }

    private string MakeFolder(string name, TimeSpan age)
    {
        var folder = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "data.db"), "copy");
        Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow - age);
        _folders.Add(folder);
        return folder;
    }

    [Fact]
    public void OldLeftoverCopiesAreSweptNewOnesAndOtherFoldersAreKept()
    {
        CreateDatabase();
        var old = MakeFolder("fara-album-booth-" + Guid.NewGuid().ToString("N"), TimeSpan.FromHours(2));
        var recent = MakeFolder("fara-album-booth-" + Guid.NewGuid().ToString("N"), TimeSpan.FromMinutes(5));
        var lookalike = MakeFolder("fara-album-booth-not-a-guid", TimeSpan.FromHours(2));

        BoothLibrary.LoadThumbnails(_db);

        Assert.False(Directory.Exists(old));                       // 古い消し残しは掃除する
        Assert.True(Directory.Exists(recent));                     // 使用中かもしれないものは残す
        Assert.True(Directory.Exists(lookalike));                  // 形が違うものは触らない
    }
}
