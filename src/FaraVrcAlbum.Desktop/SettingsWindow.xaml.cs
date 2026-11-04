using System.Windows;
using FaraVrcAlbum.Desktop.ViewModels;

namespace FaraVrcAlbum.Desktop;

/// <summary>設定のウィンドウ（メニューの「設定」・Ctrl+,）。中身は設定の画面（SettingsView）。</summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel settings)
    {
        InitializeComponent();
        DataContext = settings;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
