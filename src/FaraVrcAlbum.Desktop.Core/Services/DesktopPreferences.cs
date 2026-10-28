using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// デスクトップ版の手元の設定（local/desktop.json。Git 管理外）。既定のアバター・Unity のアバター/服のフォルダ。
/// 読めなければ既定値（壊れたファイルで起動を止めない）。
/// </summary>
public sealed class DesktopPreferences(string path)
{
    public string Path { get; } = path;

    /// <summary>壊れた設定を退避して作り直したとき（退避先のパス）。画面（MainViewModel）が知らせる。</summary>
    public static event Action<string>? Recovered;

    /// <summary>新しく選んだ写真に自動で入れるアバターの id（空 = 入れない）。</summary>
    public string DefaultAvatar
    {
        get => Get("defaultAvatar");
        set => Set("defaultAvatar", value);
    }

    /// <summary>アバターの prefab を探すフォルダ（Unity のプロジェクトなど）。</summary>
    public string AvatarFolder
    {
        get => Get("avatarFolder");
        set => Set("avatarFolder", value);
    }

    /// <summary>服の一覧を読むフォルダ。</summary>
    public string OutfitFolder
    {
        get => Get("outfitFolder");
        set => Set("outfitFolder", value);
    }

    /// <summary>BOOTH Library Manager のデータベース（data.db）の場所。空なら既定の場所（AppData の pm.booth.library-manager）。</summary>
    public string BoothDatabase
    {
        get => Get("boothDatabase");
        set => Set("boothDatabase", value);
    }

    /// <summary>最後に控えを作った日時と内容（画面に出す文。例: 2026-10-27 21:30（8 ファイル））。</summary>
    public string LastBackup
    {
        get => Get("lastBackup");
        set => Set("lastBackup", value);
    }

    private string Get(string name) => Read()?[name]?.GetValue<string>() ?? "";

    private void Set(string name, string value)
    {
        // 壊れた設定の上に新しい内容を書くと、他の設定（BOOTH の DB の場所・素材フォルダなど）まで消えるため、
        // 壊れていたら先に別名へ退避してから書く（退避先は残る。手で戻せる）
        var node = ReadForWrite();
        if (string.IsNullOrEmpty(value)) node.Remove(name);
        else node[name] = value;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        File.WriteAllText(Path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    /// <summary>書き込み用。ファイルが無ければ空、壊れていれば Path.broken-&lt;日時&gt; に移してから空を返す。</summary>
    private JsonObject ReadForWrite()
    {
        if (!File.Exists(Path)) return new JsonObject();
        var current = ParseObject(File.ReadAllText(Path));
        if (current is not null) return current;
        // 同じ時刻に 2 回壊れても衝突しないよう、GUID の一部を付ける
        var aside = Path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        File.Move(Path, aside, overwrite: false);
        Debug.WriteLine($"DesktopPreferences: 壊れた設定を退避しました: {aside}");
        Recovered?.Invoke(aside);
        return new JsonObject();
    }

    private JsonObject? Read() => File.Exists(Path) ? ParseObject(File.ReadAllText(Path)) : null;

    private static JsonObject? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
