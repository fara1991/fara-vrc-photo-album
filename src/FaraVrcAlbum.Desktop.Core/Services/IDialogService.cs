namespace FaraVrcAlbum.Desktop.Services;

/// <summary>確認ダイアログ（はい・いいえ）。既定は「いいえ」。画面の実装は WPF 側、テストは固定の答え。</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel);

    /// <summary>終わったことを知らせるダイアログ（OK だけ）。テストの既定は、何もしない。</summary>
    Task InformAsync(string title, string message) => Task.CompletedTask;

    /// <summary>フォルダを選ぶダイアログ。選ばなければ null。テストの固定の答えは null（既定）。</summary>
    string? PickFolder(string title, string? initial) => null;
}
