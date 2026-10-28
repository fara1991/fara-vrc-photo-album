using System.Windows;

namespace FaraVrcAlbum.Desktop.Services;

/// <summary>確認ダイアログ（はい・いいえ。既定は「いいえ」。Esc と「いいえ」は何もしない）と、終わったことを知らせるダイアログ（OK だけ）。</summary>
public sealed class WpfDialogService(Func<Window?> owner) : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel)
    {
        // 「はい」の文言は選べないので、確認の中身（ボタンの意味）を本文の末尾に添える
        var text = $"{message}\n\n[はい] = {confirmLabel}　[いいえ] = 何もしない";
        var answer = owner() is { } window && window.IsVisible
            ? MessageBox.Show(window, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            : MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return Task.FromResult(answer == MessageBoxResult.Yes);
    }

    public Task InformAsync(string title, string message)
    {
        if (owner() is { } window && window.IsVisible) MessageBox.Show(window, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    public string? PickFolder(string title, string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(initial) && System.IO.Directory.Exists(initial)) dialog.InitialDirectory = initial;
        var ok = owner() is { } window && window.IsVisible ? dialog.ShowDialog(window) : dialog.ShowDialog();
        return ok == true ? dialog.FolderName : null;
    }
}
