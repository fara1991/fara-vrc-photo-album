using System.Windows;
using System.Windows.Controls;

namespace FaraVrcAlbum.Desktop.Views;

/// <summary>写真のプレビュー（PC の写真の場所か、アップロード済みの写真の URL）。どの画面も同じ見た目にするための部品。</summary>
public partial class PhotoPreview : UserControl
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(string), typeof(PhotoPreview), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(PhotoPreview), new PropertyMetadata("写真を選ぶと、ここに表示します"));

    public PhotoPreview()
    {
        InitializeComponent();
    }

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }
}
