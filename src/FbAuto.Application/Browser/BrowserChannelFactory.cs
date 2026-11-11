using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Bit;
using FbAuto.Browser.Chrome;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Browser;

/// <summary>
/// 通道工厂。上层只认 <see cref="IBrowserChannel"/>，换通道不改任务代码。
/// </summary>
public static class BrowserChannelFactory
{
    public static IBrowserChannel Create(BrowserChannelKind kind, IAppLogger logger, AppConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        config ??= new AppConfig();

        return kind switch
        {
            BrowserChannelKind.Google => new ChromeChannel(logger, config.ChromePath),
            BrowserChannelKind.Bit => new BitBrowserChannel(logger, new BitBrowserApiClient(config.BitBrowserApiBaseUrl)),
            _ => throw new NotSupportedException($"不支持的浏览器通道：{kind}"),
        };
    }
}
