namespace FaraVrcAlbum.Desktop.Services;

/// <summary>確認ダイアログ（はい・いいえ）。既定は「いいえ」。画面の実装は WPF 側、テストは固定の答え。</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);

    /// <summary>終わったことを知らせるダイアログ（OK だけ）。テストの既定は、何もしない。</summary>
    Task InformAsync(string title, string message) => Task.CompletedTask;

    /// <summary>フォルダを選ぶダイアログ。選ばなければ null。テストの固定の答えは null（既定）。</summary>
    string? PickFolder(string title, string? initial) => null;

    /// <summary>
    /// 観測していない Task（起動の読み込み・画面の読み直し・通知など）の例外を報告する。
    /// 画面の実装は App の ShowError（秘密を消してから表示）と同じ経路。テストの既定は、何もしない。
    /// </summary>
    void ReportError(Exception error) { }
}
