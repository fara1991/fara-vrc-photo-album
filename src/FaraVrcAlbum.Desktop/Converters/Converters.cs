using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

using System.Windows.Media.Imaging;

namespace FaraVrcAlbum.Desktop.Converters;

/// <summary>true で表示、false で非表示（ConverterParameter=invert で反転）。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (parameter is "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool を反転する。</summary>
public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// inbox の PNG のサムネイル（幅 160 px）。ファイルをロックしないように、内容を読んでから閉じる。
/// 読めなければ null（枠だけ出す）。元ファイルは読むだけで、書き換えも移動もしない。
/// </summary>
public sealed class ThumbnailConverter : IValueConverter
{
    private const int Width = 160;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || !File.Exists(path)) return null;
        try
        {
            var bytes = ReadShared(path);
            using var memory = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = Width;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>他のプロセス（エクスプローラー等）が開いていても読めるように、共有の読み取りで開く。</summary>
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>
/// プレビューの画像（PC の写真の場所か、アップロード済みの写真の URL）。幅 1000 px に縮めて読む。
/// ファイルはロックしないように、内容を読んでから閉じる。読めなければ null（枠と「表示できません」だけ出す）。
/// </summary>
public sealed class PreviewImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string source || source.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.DecodePixelWidth = 1000;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                image.UriSource = uri;                                                   // ダウンロードは WPF が裏で行う
            }
            else
            {
                if (!File.Exists(source)) return null;
                image.StreamSource = new MemoryStream(File.ReadAllBytes(source));
            }
            image.EndInit();
            if (image.CanFreeze && !image.IsDownloading) image.Freeze();
            return image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>プレビューの文字の揃え（left / center / right）を TextAlignment にする。</summary>
public sealed class AlignConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
