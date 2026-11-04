using System.Windows;
using FaraVrcAlbum.Desktop.ViewModels;

namespace FaraVrcAlbum.Desktop.Views;

/// <summary>フォルダから読んだ名前（アバターの prefab・服）を一覧にして、チェックしたものをまとめて登録する別ウィンドウ。</summary>
public partial class FolderImportWindow : Window
{
    /// <param name="showShop">お店の列を出す（服）。</param>
    /// <param name="showSuggestion">アバター名から予測した服の列と、「予測した服も着せる」を出す（アバター）。</param>
    public FolderImportWindow(FolderImportViewModel vm, string title, string hint, bool showShop, bool showSuggestion)
    {
        InitializeComponent();
        Title = title;
        HintText.Text = hint;
        ShopColumn.Visibility = showShop ? Visibility.Visible : Visibility.Collapsed;
        SuggestColumn.Visibility = showSuggestion ? Visibility.Visible : Visibility.Collapsed;
        LinkBox.Visibility = showSuggestion ? Visibility.Visible : Visibility.Collapsed;
        // アバター: 登録済みのアバターに、予測した服を着せる別のボタンと、置き換えの選択。
        // 服: 登録済みの服も「インポート」で BOOTH の画像・情報を取り込める。置き換えの選択は「すでにあるものも上書きする（再インポート）」（外すと、足りない項目だけを足す差分）
        var avatarAction = showSuggestion && vm.HasRegisteredAction && !vm.RegisteredViaImport;
        ApplyRegisteredButton.Visibility = avatarAction ? Visibility.Visible : Visibility.Collapsed;
        OverwriteBox.Visibility = avatarAction || vm.RegisteredViaImport ? Visibility.Visible : Visibility.Collapsed;
        if (vm.RegisteredViaImport)
        {
            OverwriteBox.Content = "すでに画像・情報があるものも上書きする（再インポート）";
            OverwriteBox.ToolTip = "外しておくと、服にまだ無い項目（画像・説明・タグ・日付）だけを BOOTH から足します（差分インポート）。入れると、すでにある項目も BOOTH の今の内容に置き換えます。";
            ImportButton.ToolTip = "チェックした未登録の服を登録し、登録済みの服には BOOTH の画像・説明・タグ・日付を取り込みます（BOOTH Library Manager のデータを使います）。";
        }
        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
