using System.Windows;

namespace FaraVrcAlbum.Desktop;

/// <summary>使い方のウィンドウ（メニューの「ヘルプ」・F1）。利用者向けの説明だけ（開発の文書は開かない）。</summary>
public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
