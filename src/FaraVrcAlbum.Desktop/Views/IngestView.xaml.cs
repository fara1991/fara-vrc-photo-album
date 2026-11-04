using System.Windows.Controls;
using System.Windows.Input;
using FaraVrcAlbum.Desktop.ViewModels;

namespace FaraVrcAlbum.Desktop.Views;

/// <summary>取り込みの画面。Space で、選んでいる行のチェックを切り替える（キーボードだけでも選べるように）。</summary>
public partial class IngestView : UserControl
{
    public IngestView()
    {
        InitializeComponent();
    }

    private void InboxList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || sender is not DataGrid list) return;
        foreach (var item in list.SelectedItems.OfType<InboxItemViewModel>().ToList())
        {
            item.IsChecked = !item.IsChecked;
        }
        e.Handled = true;
    }
}
