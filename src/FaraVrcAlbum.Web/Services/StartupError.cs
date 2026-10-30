using FaraBlazorComponent.Razor;
using FaraVrcAlbum.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace FaraVrcAlbum.Web.Services;

/// <summary>起動の前後の後始末（読み込みの失敗の表示・保存が無いときの既定のテーマ）。</summary>
public static class StartupError
{
    /// <summary>
    /// 起動できなかったことを、画面下のエラー表示（#blazor-error-ui）で知らせる。静的 HTML の本文は残る。
    /// js/startup.js が表示を出す（インラインの script は CSP で使えないので外部ファイル）。出すと kit の boot-indicator.js が読み込み表示を外す。
    /// </summary>
    public static async Task ShowAsync(IJSRuntime js)
    {
        try { await js.InvokeVoidAsync("faraAlbumStartup.showError"); }
        catch (JSException) { /* 表示できなくても、読み込み表示は 60 秒で外れる */ }
    }

    /// <summary>
    /// 保存されたテーマが無い（初めて・保存が使えない）ときの既定は、写真向きのダーク。kit は読めないとき light にするので、決めておく。
    /// 描画の前の属性は js/theme-default.js が同じ規則で付ける（両方そろえないと ThemeSwitch の表示と html の data-bs-theme がずれる）。
    /// </summary>
    public static async Task ApplyDefaultThemeAsync(IServiceProvider services)
    {
        var js = services.GetRequiredService<IJSRuntime>();
        if (js is not IJSInProcessRuntime sync) return;
        try
        {
            if (!string.IsNullOrEmpty(await sync.InvokeAsync<string?>("localStorage.getItem", SiteInfo.StorageKey))) return;
        }
        catch (JSException)
        {
            // 保存が使えないときも、ダークにする
        }
        await services.GetRequiredService<SiteThemeState>().SetAsync(SiteThemeState.Dark);
    }
}
