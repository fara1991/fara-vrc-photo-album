using FaraBlazorComponent.Razor;
using MudBlazor;

namespace FaraVrcAlbum.Web.Layout;

/// <summary>
/// このサイトの MudBlazor のテーマ。色は kit の既定（紫。SiteTheme）のまま、ボタン・入力欄の角丸だけを少し丸くする。
/// 色を替えるときは、wwwroot/css/app.css の先頭の「テーマ」ブロックと、ここ（パレットを上書きする場合）を同じ値にそろえる。
/// </summary>
public static class AlbumTheme
{
    public static MudTheme Create()
    {
        var theme = SiteTheme.Create();
        theme.LayoutProperties.DefaultBorderRadius = "8px";  // ボタン・入力欄の角丸（kit 既定は 6px）
        return theme;
    }
}
