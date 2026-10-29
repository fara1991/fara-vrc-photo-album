using FaraBlazorComponent.Core;
using FaraBlazorComponent.Razor;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Web;
using FaraVrcAlbum.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
var http = new HttpClient { BaseAddress = baseAddress };
builder.Services.AddSingleton(http);

// 起動に要るもの（サイトの設定と記事の一覧 = data/*.json、メンテナンスの状態 = /api/maintenance）を同時に読む（順に待つと通信の往復が起動に足される）。
// 最初の描画の前に必要（静的な本文を、空の画面に置き換えないため）。メンテナンスの状態は 2 秒で打ち切り、読めなければ通常（kit の MaintenanceInfo）
var store = new AlbumStore(http);
var storeTask = store.LoadAsync();
var maintenanceTask = MaintenanceInfo.LoadAsync(http);
await Task.WhenAll(storeTask, maintenanceTask);

if (!storeTask.Result)
{
    // 設定か記事のデータが読めないときは、アプリを起動しない。静的 HTML（本文入り）のページが、そのまま残って読める。
    // 読み込み表示は #blazor-error-ui が出ると外れる（kit の boot-indicator.js）ので、60 秒待たせない
    await using var failed = builder.Build();
    await StartupError.ShowAsync(failed.Services.GetRequiredService<IJSRuntime>());
    return;
}

builder.Services.AddSingleton(store);
// 共通部品（レイアウト・フッター・報告フォームなど）が読む SiteOptions に、サイトの設定を入れる
builder.Services.AddFaraBlazorComponent(options => SiteInfo.Configure(options, store.Settings));
builder.Services.AddSingleton(maintenanceTask.Result);

if (maintenanceTask.Result.State == MaintenanceState.On)
{
    // 開いたまま MAINTENANCE_MODE=true になったとき（ページの読み込みは Worker が 503 で止めるので、通常は届かない）
    builder.RootComponents.Add<MaintenanceStart>("#app");
}
else
{
    builder.RootComponents.Add<App>("#app");
}
builder.RootComponents.Add<HeadOutlet>("head::after");   // ページの題名と、報告・404 の noindex に必要

var host = builder.Build();
await StartupError.ApplyDefaultThemeAsync(host.Services);
await host.RunAsync();
