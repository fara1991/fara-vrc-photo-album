using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FaraVrcAlbum.Build;

/// <summary>Pages の Razor 部品を HtmlRenderer で HTML にする（Web と同じ部品。サービスは要らない素の HTML の部品だけ）。</summary>
internal sealed class PageRenderer : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly HtmlRenderer _renderer;

    public PageRenderer()
    {
        var collection = new ServiceCollection();
        collection.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        // 日本語を &#x30B5; のような文字参照にしない（HtmlRenderer の既定は基本ラテンだけ。本文を読める形で出す。< > & " は従来どおり文字参照）
        collection.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));
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

    public void Dispose()
    {
        _renderer.Dispose();
        _services.Dispose();
    }
}
