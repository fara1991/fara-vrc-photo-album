using System.Text.Encodings.Web;
using System.Text.Unicode;
using FaraBlazorComponent.Core;
using FaraBlazorComponent.Razor.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FaraVrcAlbum.Build;

/// <summary>Pages の Razor 部品を HtmlRenderer で HTML にする（Web と同じ部品。サービスは要らない素の HTML の部品だけ）。フッターは <see cref="RenderFooter"/>。</summary>
internal sealed class PageRenderer : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly HtmlRenderer _renderer;

    public PageRenderer()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        // 日本語を &#x30B5; のような文字参照にしない（HtmlRenderer の既定は基本ラテンだけ。本文を読める形で出す。< > & " は従来どおり文字参照）
        collection.AddSingleton(UnicodeEncoder());
        _services = collection.BuildServiceProvider();
        _renderer = new HtmlRenderer(_services, NullLoggerFactory.Instance);
    }

    public string Render<TComponent>(IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        return _renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await _renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// kit の SiteFooter（Web で起動したあとのフッターと同じ部品・同じ SiteOptions）を、静的 HTML の本文にする。
    /// pagePath はそのページの URL（問い合わせの ?from= に入る。Web で開いたときと同じ値になる）。
    /// origin は、このビルドの環境の URL（https://ホスト。作者リンクは、確認用のホストのときだけ kit が確認用に切り替える）。
    /// ページごとに NavigationManager を作り直す（ページは移らない）。
    /// </summary>
    public static string RenderFooter(SiteOptions options, string origin, string pagePath)
    {
        var navigation = new StaticNavigation();
        navigation.Set(origin + "/", origin + pagePath);
        var services = new ServiceCollection();
        services.AddSingleton(UnicodeEncoder());
        services.AddSingleton(options);
        services.AddSingleton<NavigationManager>(navigation);
        using var provider = services.BuildServiceProvider();
        using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<SiteFooter>(ParameterView.Empty);
            return output.ToHtmlString();
        }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _services.Dispose();
    }

    private static HtmlEncoder UnicodeEncoder() => HtmlEncoder.Create(UnicodeRanges.All);

    /// <summary>書き出しの中の NavigationManager。URL を決めるだけで、移動はしない（移動しようとしたら例外にして、気付けるようにする）。</summary>
    private sealed class StaticNavigation : NavigationManager
    {
        public void Set(string baseUri, string uri) => Initialize(baseUri, uri);

        protected override void NavigateToCore(string uri, NavigationOptions options) =>
            throw new NotSupportedException("静的な HTML の書き出しでは、ページを移らない");
    }
}
