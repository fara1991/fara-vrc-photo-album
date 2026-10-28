using Bunit;
using FaraBlazorComponent.Core;
using FaraBlazorComponent.Razor;
using FaraVrcAlbum.Models;
using FaraVrcAlbum.Web.Pages;
using FaraVrcAlbum.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FaraVrcAlbum.Web.Tests;

/// <summary>/report: 連絡先は Twitter のユーザー名（@○○）が必須（kit v0.4.0 のオプション）。</summary>
public sealed class ReportTests : BunitContext, IAsyncLifetime
{
    public ReportTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient { BaseAddress = new Uri("https://album.test/") });
        Services.AddSingleton(new AlbumStore(new HttpClient { BaseAddress = new Uri("https://album.test/") }));
        Services.AddFaraBlazorComponent(options => SiteInfo.Configure(options, new SiteSettings { SiteName = "Fara Photo Album", Author = "Fara", ImageBaseUrl = "https://i.test/" }));
        Services.AddSingleton(MaintenanceInfo.Off);
        Services.AddSupplyValueFromQueryProvider();
    }

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void TheContactIsRequiredAndAskedAsAnXUserName()
    {
        var cut = Render<Report>();

        Assert.Equal("Twitter のユーザー名（@○○）（必須）", cut.Find("label[for=report-contact]").TextContent.Trim());
        Assert.Equal("true", cut.Find("#report-contact").GetAttribute("aria-required"));
        Assert.Equal("@example", cut.Find("#report-contact").GetAttribute("placeholder"));
        Assert.Contains("Discord では受け付けていません", cut.Markup);
    }
}
