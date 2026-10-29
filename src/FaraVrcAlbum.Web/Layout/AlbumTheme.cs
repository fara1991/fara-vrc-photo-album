using FaraBlazorComponent.Razor;
using MudBlazor;

namespace FaraVrcAlbum.Web.Layout;

/// <summary>
/// このサイトの MudBlazor のテーマ（ローズ〜ピンク寄り）。kit の既定（紫。SiteTheme）の字体・レイアウトはそのままに、色だけを替える。
/// CSS の色（wwwroot/css/app.css の先頭の「テーマ」ブロック）と同じ値にそろえる。色を調整するときは、両方を同じように直す。
/// </summary>
public static class AlbumTheme
{
    public static MudTheme Create()
    {
        var theme = SiteTheme.Create();

        theme.PaletteLight.Primary = "#b83a64";             // --fbc-brand（ライト）。白文字で 5.5:1
        theme.PaletteLight.PrimaryContrastText = "#ffffff";
        theme.PaletteLight.Background = "#fdf6f8";          // --fbc-body-bg
        theme.PaletteLight.Surface = "#ffffff";
        theme.PaletteLight.TextPrimary = "#2a1520";         // --album-text
        theme.PaletteLight.TextSecondary = "#7a5160";       // --fbc-muted
        theme.PaletteLight.DrawerBackground = "#1e0d17";    // --album-sidebar-solid
        theme.PaletteLight.AppbarBackground = "#3a1425";    // --fbc-header-bg の右端

        theme.PaletteDark.Primary = "#d6336c";              // --fbc-brand（ダーク）。白文字で 4.6:1
        theme.PaletteDark.PrimaryContrastText = "#ffffff";
        theme.PaletteDark.Background = "#130a0e";           // --fbc-body-bg
        theme.PaletteDark.Surface = "#1d1217";              // --album-card-bg
        theme.PaletteDark.TextPrimary = "#fbeff3";          // --album-text
        theme.PaletteDark.TextSecondary = "#c9abb8";        // --fbc-muted
        theme.PaletteDark.DrawerBackground = "#1e0d17";
        theme.PaletteDark.AppbarBackground = "#3a1425";

        theme.LayoutProperties.DefaultBorderRadius = "8px";  // ボタン・入力欄の角丸（kit 既定は 6px）
        return theme;
    }
}
