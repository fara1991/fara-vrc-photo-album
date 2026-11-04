using FaraBlazorComponent.Razor;
using MudBlazor;

namespace FaraVrcAlbum.Web.Layout;

/// <summary>
/// このサイトの MudBlazor のテーマ。色は kit の既定（紫。SiteTheme）のまま、ボタン・入力欄の角丸を少し丸くし、サイドバー（浮いたパネル）の幅を決める。
/// 色を替えるときは、wwwroot/css/app.css の先頭の「テーマ」ブロックと、ここ（パレットを上書きする場合）を同じ値にそろえる。
/// </summary>
public static class AlbumTheme
{
    /// <summary>
    /// サイドバー（ドロワー）の幅。パネル本体 304px と、左右の余白 12px ずつ。本文はこの幅の分だけ右に寄る。
    /// 起動の前の静的 HTML の左余白（app.css の --album-drawer-w）と同じ値（tests/js/sidebar-css.test.mjs が確かめる）。
    /// </summary>
    public const string DrawerWidth = "328px";

    public static MudTheme Create()
    {
        var theme = SiteTheme.Create();
        theme.LayoutProperties.DefaultBorderRadius = "8px";  // ボタン・入力欄の角丸（kit 既定は 6px）
        theme.LayoutProperties.DrawerWidthLeft = DrawerWidth;
        return theme;
    }
}
