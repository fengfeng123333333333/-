using FbAuto.Browser.Abstractions;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 通道提供者。抽出来是为了让调度器可测：测试注入假通道，不必真启浏览器。
/// </summary>
public interface IBrowserChannelProvider
{
    IBrowserChannel Create(BrowserChannelKind kind);
}

/// <summary>默认实现：转调 <see cref="Browser.BrowserChannelFactory"/>。</summary>
public sealed class ConfigBrowserChannelProvider : IBrowserChannelProvider
{
    private readonly IAppLogger _logger;
    private readonly Core.Configuration.AppConfig _config;

    public ConfigBrowserChannelProvider(IAppLogger logger, Core.Configuration.AppConfig config)
    {
        _logger = logger;
        _config = config;
    }

    public IBrowserChannel Create(BrowserChannelKind kind) =>
        Browser.BrowserChannelFactory.Create(kind, _logger, _config);
}
