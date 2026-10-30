using System.Windows;
using System.Windows.Controls;
using FaraVrcAlbum.Desktop.ViewModels;

namespace FaraVrcAlbum.Desktop.Views;

public partial class AvatarOutfitView : UserControl
{
    public AvatarOutfitView()
    {
        InitializeComponent();
    }

    private void OpenAvatarImport_Click(object sender, RoutedEventArgs e) =>
        Open(((AvatarOutfitViewModel)DataContext).AvatarImport, "Unity のフォルダからアバターを登録",
            "Unity のアバターがあるフォルダを選んで「読み直す」を押すと、フォルダの中の .prefab のファイル名を一覧にし、登録済みの服の名前から、着ている服を予測して出します。登録する名前にチェックを入れて「インポート」を押します。", showShop: false, showSuggestion: true);

    private void OpenOutfitImport_Click(object sender, RoutedEventArgs e) =>
        Open(((AvatarOutfitViewModel)DataContext).OutfitImport, "フォルダから服を登録",
            "服の入っているフォルダ（BOOTH Library Manager のフォルダ）を選んで「読み直す」を押すと、BOOTH のカテゴリ「3D モデル」→「3D 衣装」の商品だけを、商品名とお店つきで一覧にします。登録する服にチェックを入れて「インポート」を押すと、商品ページの URL と、BOOTH の画像・説明・タグ・日付も自動で入ります。すでに登録した服にチェックを入れて「インポート」を押すと、BOOTH の画像・情報を取り込み直せます（既定は、服にまだ無い項目だけを足す差分。「すでに画像・情報があるものも上書きする」を入れると、BOOTH の今の内容にそろえる再インポート）。", showShop: true, showSuggestion: false);

    private void Open(FolderImportViewModel vm, string title, string hint, bool showShop, bool showSuggestion)
    {
        var window = new FolderImportWindow(vm, title, hint, showShop, showSuggestion) { Owner = Window.GetWindow(this) };
        if (vm.Folder.Trim().Length > 0 && vm.Candidates.Count == 0) vm.ScanCommand.Execute(null);     // 前回のフォルダがあれば、開いたときに読む
        window.ShowDialog();
    }
}
