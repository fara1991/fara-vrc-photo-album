using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FaraVrcAlbum.Images;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>
/// デスクトップ版の手元の設定（local/desktop.json。Git 管理外）。既定のアバター・Unity のアバター/服のフォルダ。
/// 読めなければ既定値（壊れたファイルで起動を止めない）。読むだけでは書き換えない。
/// 壊れた設定の退避は、起動のとき（RecoverIfBroken）と、壊れた上に書くとき（Set）の 2 か所で行い、どちらも退避先を知らせる。
/// </summary>
/// <param name="path">設定ファイルのパス。</param>
/// <param name="onRecovered">書き込みのときに壊れた設定を退避したとき（退避先のパス）に呼ぶ。画面が知らせる。例外は握る（設定の保存を失敗させない）。</param>
public sealed class DesktopPreferences(string path, Action<string>? onRecovered = null)
{
    // 画面の 1 スレッド前提（今の使い方は画面のスレッドだけ）。ただし Preferences は読むたびに新しいインスタンスを作るので、
    // lock はインスタンスごとではなく static（プロセス内で 1 つ）にする。読み書きと退避をすべてこの lock で直列にし、同時の Set で更新が失われないようにする。
    private static readonly object Gate = new();

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

    /// <summary>
    /// 起動のとき 1 回だけ呼ぶ。設定が壊れていれば別名へ退避して、退避先のパスを返す（壊れていなければ、ファイルが無ければ null）。
    /// 退避以外は書かない（ファイルが無いときは作らない）。退避の知らせは呼び出し側（AlbumSession → 画面）が行う。
    /// </summary>
    public string? RecoverIfBroken()
    {
        lock (Gate)
        {
            if (!File.Exists(Path)) return null;
            if (ParseObject(File.ReadAllText(Path)) is not null) return null;
            return MoveAside();
        }
    }

    private string Get(string name)
    {
        lock (Gate)
        {
            return Read()?[name]?.GetValue<string>() ?? "";
        }
    }

    private void Set(string name, string value)
    {
        string? aside;
        lock (Gate)
        {
            // 壊れた設定の上に新しい内容を書くと、他の設定（BOOTH の DB の場所・素材フォルダなど）まで消えるため、
            // 壊れていたら先に別名へ退避してから書く（退避先は残る。手で戻せる）
            var node = ReadForWrite(out aside);
            if (string.IsNullOrEmpty(value)) node.Remove(name);
            else node[name] = value;
            // 一時ファイルに書いてから置き換える（書き込み中に落ちても、元の設定は壊れない）
            JsonFiles.WriteAtomic(Path, node.ToJsonString(JsonFiles.Options));
        }
        // 通知（モーダルのダイアログ）は lock の外で出す。出している間も、ほかの読み書きを止めない
        NotifyRecovered(aside);
    }

    /// <summary>書き込み用。ファイルが無ければ空、壊れていれば Path.broken-&lt;日時&gt; に移してから空を返す（退避先は aside に入る）。lock の中で呼ぶ。</summary>
    private JsonObject ReadForWrite(out string? aside)
    {
        aside = null;
        if (!File.Exists(Path)) return new JsonObject();
        var current = ParseObject(File.ReadAllText(Path));
        if (current is not null) return current;
        aside = MoveAside();
        return new JsonObject();
    }

    /// <summary>壊れたファイルを別名へ移す。同じ時刻に 2 回壊れても衝突しないよう、GUID の一部を付ける。lock の中で呼ぶ。</summary>
    private string MoveAside()
    {
        var aside = Path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        File.Move(Path, aside, overwrite: false);
        Debug.WriteLine($"DesktopPreferences: 壊れた設定を退避しました: {aside}");
        return aside;
    }

    private void NotifyRecovered(string? aside)
    {
        if (aside is null) return;
        try
        {
            onRecovered?.Invoke(aside);
        }
        catch (Exception e)
        {
            // 通知の失敗（画面の例外など）で、設定の読み書きを失敗させない
            Debug.WriteLine($"DesktopPreferences: 退避の通知で例外: {e.GetType().Name}");
        }
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
