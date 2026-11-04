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

/// <summary>/report: 連絡先は任意（kit の既定。他のサイトと同じ）。</summary>
public sealed class ReportTests : BunitContext, IAsyncLifetime
{
    public ReportTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        // ファクトリーで登録する（コンテナーが作って、コンテキストの破棄のときに破棄する）
        Services.AddSingleton(_ => new HttpClient { BaseAddress = new Uri("https://album.test/") });
        Services.AddSingleton(_ => new AlbumStore(new HttpClient { BaseAddress = new Uri("https://album.test/") }));
        Services.AddFaraBlazorComponent(options => SiteInfo.Configure(options, new SiteSettings { SiteName = "Fara Photo Album", Author = "Fara", ImageBaseUrl = "https://i.test/" }));
        Services.AddSingleton(MaintenanceInfo.Off);
        Services.AddSupplyValueFromQueryProvider();
    }

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    [Fact]
    public void TheHeadingIsContactNotReportToAuthor()
    {
        var cut = Render<Report>();

        Assert.Equal("問い合わせ", cut.Find("h1").TextContent.Trim());
        Assert.DoesNotContain("作者へ報告", cut.Markup);
    }

    [Fact]
    public void TheContactIsOptional()
    {
        var cut = Render<Report>();

        Assert.Equal("連絡先（任意）", cut.Find("label[for=report-contact]").TextContent.Trim());
        Assert.NotEqual("true", cut.Find("#report-contact").GetAttribute("aria-required"));
        Assert.Contains("書かなくても送れます", cut.Markup);
        Assert.DoesNotContain("Twitter", cut.Markup);
    }
}
