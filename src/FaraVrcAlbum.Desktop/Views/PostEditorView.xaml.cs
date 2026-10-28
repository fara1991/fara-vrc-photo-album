using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FaraVrcAlbum.Desktop.Services;
using MaterialDesignThemes.Wpf;

namespace FaraVrcAlbum.Desktop.Views;

/// <summary>
/// 画面の表示と、本文の装飾ボタン（処理は <see cref="MarkdownEdit"/> に置き、ここでは入力欄に当てるだけ）。
/// 文字を直接書き換えるので、元に戻す（Ctrl+Z）も効く。
/// </summary>
public partial class PostEditorView : UserControl
{
    public PostEditorView()
    {
        InitializeComponent();
    }

    private void Apply(Func<string, int, int, MarkdownEdit.Result> edit)
    {
        var box = DocumentBox;
        var r = edit(box.Text, box.SelectionStart, box.SelectionLength);
        box.Focus();
        // 変わる範囲だけを置き換える（TextBox の編集として 1 回分になるので、Ctrl+Z で戻せる）
        box.Select(r.ReplaceStart, r.ReplaceLength);
        box.SelectedText = r.Replacement;
        box.Select(r.SelectStart, r.SelectLength);
    }

    private void Bold_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Wrap(t, s, l, "**", "**", "太字にする文字"));

    private void Italic_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Wrap(t, s, l, "*", "*", "斜体にする文字"));

    private bool _expanded;

    private void Underline_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Wrap(t, s, l, "++", "++", "下線を付ける文字"));

    private void Strike_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Wrap(t, s, l, "~~", "~~", "取り消す文字"));

    private void AlignLeft_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Align(t, s, l, "left"));

    private void AlignCenter_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Align(t, s, l, "center"));

    private void AlignRight_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Align(t, s, l, "right"));

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        DocumentBox.Focus();
        DocumentBox.Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        DocumentBox.Focus();
        DocumentBox.Redo();
    }

    /// <summary>入力欄を広げる（題・日付・タグの欄とプレビューを隠す）・もとに戻す。</summary>
    private void Expand_Click(object sender, RoutedEventArgs e)
    {
        _expanded = !_expanded;
        var visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        FieldsPanel.Visibility = visibility;
        BodyHint.Visibility = visibility;
        PreviewPane.Visibility = visibility;
        Grid.SetColumnSpan(LeftPane, _expanded ? 3 : 1);
        ExpandIcon.Kind = _expanded ? PackIconKind.FullscreenExit : PackIconKind.Fullscreen;
        DocumentBox.Focus();
    }

    private void Heading_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "## "));

    private void Bullet_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "- "));

    private void Number_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "1. "));

    private void Quote_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.ToggleLinePrefix(t, s, l, "> "));

    private void Link_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.Link(t, s, l));

    private void Rule_Click(object sender, RoutedEventArgs e) => Apply((t, s, l) => MarkdownEdit.InsertRule(t, s, l));

    private void DocumentBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (e.Key == Key.B) { Bold_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.I) { Italic_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.U) { Underline_Click(sender, e); e.Handled = true; }
    }
}
