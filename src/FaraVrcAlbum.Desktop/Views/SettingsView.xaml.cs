using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FaraVrcAlbum.Desktop.ViewModels;

namespace FaraVrcAlbum.Desktop.Views;

/// <summary>
/// 画面の表示だけ（処理は ViewModel）。ここにあるのは、WPF でしか書けない部分:
///   「表示」ボタンを押している間だけ値を出す（マウスを離す・外れる・フォーカスが移ると隠す。Space・Enter でも同じ）
///   PasswordBox の値を ViewModel に渡す（PasswordBox は値をバインドできないため）
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private static SettingRowViewModel? Row(object sender) => (sender as FrameworkElement)?.DataContext as SettingRowViewModel;

    private void Reveal_Down(object sender, MouseButtonEventArgs e)
    {
        if (Row(sender) is { } row) row.IsRevealed = true;
        (sender as UIElement)?.CaptureMouse();
        e.Handled = true;
    }

    private void Reveal_Up(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row) row.IsRevealed = false;
        if (sender is UIElement element && element.IsMouseCaptured) element.ReleaseMouseCapture();
    }

    private void Reveal_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        if (Row(sender) is { } row) row.IsRevealed = true;
        e.Handled = true;
    }

    private void Reveal_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        if (Row(sender) is { } row) row.IsRevealed = false;
        e.Handled = true;
    }

    private void Secret_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box && Row(sender) is { } row) row.SetNewValue(box.Password);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row) row.Clear();
    }
}
