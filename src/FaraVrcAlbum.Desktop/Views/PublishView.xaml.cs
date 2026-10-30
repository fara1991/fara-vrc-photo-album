using System.Windows.Controls;

namespace FaraVrcAlbum.Desktop.Views;

/// <summary>公開の画面。表示だけ（処理は PublishViewModel）。</summary>
public partial class PublishView : UserControl
{
    public PublishView()
    {
        InitializeComponent();
    }

    /// <summary>出力（ログ）が増えたら、いちばん下（新しい行）が見えるように、自動でスクロールする。</summary>
    private void LogBox_TextChanged(object sender, TextChangedEventArgs e) => LogBox.ScrollToEnd();
}
