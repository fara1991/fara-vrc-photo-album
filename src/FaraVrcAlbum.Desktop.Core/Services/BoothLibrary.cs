using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>BOOTH の商品 1 件（BOOTH Library Manager の保存データから読む）。</summary>
public sealed record BoothItem(long Id, string Name, string ShopName, string ShopSubdomain, string ParentCategory, string SubCategory)
{
    /// <summary>商品ページ（https）。</summary>
    public string Url => $"https://booth.pm/ja/items/{Id}";
}

/// <summary>
/// 公式の BOOTH Library Manager が手元に持っている商品の情報（data.db。SQLite）を読む。BOOTH のサイトには問い合わせない。
/// ダウンロードしたフォルダ（b + 商品 ID）が、どの商品か・カテゴリは何かを調べるのに使う。データベースは触らず、コピーを読む（アプリが開いていても読める）。
/// </summary>
public static partial class BoothLibrary
{
    /// <summary>服として扱うカテゴリ（BOOTH の「3D モデル」→「3D 衣装」）。</summary>
    public const string OutfitParentCategory = "3Dモデル";

    /// <summary>画面（ログ・通知）に出す注意。読む側は <see cref="TakeWarnings"/> で取る。</summary>
    private static readonly ConcurrentQueue<string> Warnings = new();

    /// <summary>たまった注意（消せなかった一時フォルダなど）を取り出す。</summary>
    public static IReadOnlyList<string> TakeWarnings()
    {
        var list = new List<string>();
        while (Warnings.TryDequeue(out var warning)) list.Add(warning);
        return list;
    }

    /// <summary>一時フォルダの名前（fara-album-booth- + GUID の 32 桁）。これに合う、古いフォルダだけを掃除する。</summary>
    [GeneratedRegex(@"^fara-album-booth-[0-9a-f]{32}$")]
    private static partial Regex TempFolderName();

    /// <summary>コピーの一時フォルダを作る前に、消し残し（以前の失敗・異常終了）を掃除する。使用中のものを消さないよう、1 時間より古いものだけ。</summary>
    private static string NewTempFolder()
    {
        var root = Path.GetTempPath();
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            foreach (var dir in Directory.EnumerateDirectories(root).Where(d => TempFolderName().IsMatch(Path.GetFileName(d))).ToList())
            {
                if (Directory.GetLastWriteTimeUtc(dir) < cutoff) TryDeleteTemp(dir);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warnings.Enqueue($"BOOTH の一時フォルダの掃除ができませんでした: {e.Message}");
        }
        return Path.Combine(root, "fara-album-booth-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>コピーした data.db の一時フォルダを消す。消せなくても結果は変えない（開いたままのプロセスなど）。消せなかったことは <see cref="TakeWarnings"/> で画面に出す（次回の取り込み・起動で掃除する）。</summary>
    private static void TryDeleteTemp(string temp)
    {
        try
        {
            Directory.Delete(temp, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 購入履歴を含むデータのコピーが残っている。次回の取り込み・起動のときに掃除する
            Warnings.Enqueue($"BOOTH のデータのコピー（一時フォルダ）を消せませんでした。次回の取り込みのときに掃除します: {e.Message}");
            Debug.WriteLine($"BoothLibrary: 一時フォルダを消せませんでした: {e.Message}");
        }
    }

    public const string OutfitSubCategory = "3D衣装";

    /// <summary>BOOTH Library Manager のデータベースの既定の場所。</summary>
    public static string DefaultDatabasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pm.booth.library-manager", "data.db");

    [GeneratedRegex(@"^b(?<id>\d+)$")]
    private static partial Regex ItemFolder();

    /// <summary>フォルダ名（b + 商品 ID）から商品 ID。その形でなければ null。</summary>
    public static long? ItemIdOfFolder(string folderName) =>
        ItemFolder().Match(folderName) is { Success: true } m && long.TryParse(m.Groups["id"].Value, out var id) ? id : null;

    /// <summary>データベースから全商品を読む。データベースが無い・読めないときは null。</summary>
    public static IReadOnlyList<BoothItem>? Load(string? databasePath = null)
    {
        var path = databasePath ?? DefaultDatabasePath;
        if (!File.Exists(path)) return null;
        var temp = NewTempFolder();
        try
        {
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, "data.db");
            File.Copy(path, copy);
            foreach (var extra in new[] { "-wal", "-shm" })
                if (File.Exists(path + extra)) File.Copy(path + extra, copy + extra);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            // 利用者が名前を書き換えているとき（overwritten_booth_items）は、そちらの名前を使う
            command.CommandText = """
                SELECT i.id, COALESCE(NULLIF(o.name, ''), i.name), sh.name, i.shop_subdomain, p.name, s.name
                FROM booth_items i
                JOIN sub_categories s ON s.id = i.sub_category
                JOIN parent_categories p ON p.id = s.parent_category_id
                JOIN shops sh ON sh.subdomain = i.shop_subdomain
                LEFT JOIN overwritten_booth_items o ON o.booth_item_id = i.id
                """;
            var items = new List<BoothItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                items.Add(new BoothItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
            return items;
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteTemp(temp);
        }
    }

    /// <summary>商品 ID → サムネイル画像の URL（BOOTH のサーバー上の画像。https のものだけ）。読めなければ空。画面の一覧に出すだけで、ダウンロードして保存はしない。</summary>
    public static IReadOnlyDictionary<long, string> LoadThumbnails(string? databasePath = null)
    {
        var path = databasePath ?? DefaultDatabasePath;
        var result = new Dictionary<long, string>();
        if (!File.Exists(path)) return result;
        var temp = NewTempFolder();
        try
        {
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, "data.db");
            File.Copy(path, copy);
            foreach (var extra in new[] { "-wal", "-shm" })
                if (File.Exists(path + extra)) File.Copy(path + extra, copy + extra);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, thumbnail_url FROM booth_items WHERE thumbnail_url IS NOT NULL";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var url = reader.GetString(1);
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps) result[reader.GetInt64(0)] = url;
            }
            return result;
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new Dictionary<long, string>();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteTemp(temp);
        }
    }

    /// <summary>商品 ID → 商品の情報（画像・説明・タグ・公開日・更新日・成人向けか）。読めなければ空。サイトに載せる情報の元（アプリが content/outfits.json に書く）。</summary>
    public static IReadOnlyDictionary<long, FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothInfo> LoadDetails(string? databasePath = null)
    {
        var path = databasePath ?? DefaultDatabasePath;
        var result = new Dictionary<long, FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothInfo>();
        if (!File.Exists(path)) return result;
        var temp = NewTempFolder();
        try
        {
            Directory.CreateDirectory(temp);
            var copy = Path.Combine(temp, "data.db");
            File.Copy(path, copy);
            foreach (var extra in new[] { "-wal", "-shm" })
                if (File.Exists(path + extra)) File.Copy(path + extra, copy + extra);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();

            var tags = new Dictionary<long, List<string>>();
            using (var tagCommand = connection.CreateCommand())
            {
                tagCommand.CommandText = "SELECT booth_item_id, tag FROM booth_item_tag_relations ORDER BY booth_item_id, rowid";
                using var tagReader = tagCommand.ExecuteReader();
                while (tagReader.Read())
                {
                    var id = tagReader.GetInt64(0);
                    if (!tags.TryGetValue(id, out var list)) tags[id] = list = [];
                    var tag = DisplayName.Clean(tagReader.GetString(1), 60);
                    if (tag is not null) list.Add(tag);
                }
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, thumbnail_url, description, adult, published_at, updated_at FROM booth_items";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var thumbnail = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (thumbnail is not null && !(Uri.TryCreate(thumbnail, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)) thumbnail = null;
                result[id] = new FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothInfo(
                    thumbnail,
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    tags.GetValueOrDefault(id) ?? [],
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    !reader.IsDBNull(3) && reader.GetBoolean(3));
            }
            return result;
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new Dictionary<long, FaraVrcAlbum.Tools.Services.AvatarOutfitService.BoothInfo>();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDeleteTemp(temp);
        }
    }

    [GeneratedRegex(@"/items/(?<id>\d+)")]
    private static partial Regex ItemUrl();

    /// <summary>服の URL（https://booth.pm/ja/items/123）から商品 ID。その形でなければ null。</summary>
    public static long? ItemIdOfUrl(string? url) =>
        url is not null && ItemUrl().Match(url) is { Success: true } m && long.TryParse(m.Groups["id"].Value, out var id) ? id : null;

    /// <summary>
    /// 表示名（服の名前・お店の名前）に使える形にする。制御文字を除き、64 文字を超えるときは短くして「…」を付ける（検査の上限に合わせる）。
    /// </summary>
    public static string ToDisplayName(string raw)
    {
        var cleaned = DisplayName.Clean(raw, int.MaxValue) ?? raw.Trim();
        var runes = cleaned.EnumerateRunes().ToList();
        if (runes.Count <= 64) return cleaned;
        return string.Concat(runes.Take(63).Select(r => r.ToString())).TrimEnd() + "…";
    }
}
