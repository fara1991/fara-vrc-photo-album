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
        var node = Read() ?? new JsonObject();
        if (string.IsNullOrEmpty(value)) node.Remove(name);
        else node[name] = value;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!);
        File.WriteAllText(Path, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private JsonObject? Read()
    {
        try
        {
            return File.Exists(Path) ? JsonNode.Parse(File.ReadAllText(Path)) as JsonObject : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
