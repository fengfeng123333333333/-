using FbAuto.Browser.Abstractions;
using Microsoft.Playwright;

namespace FbAuto.Browser.Internal;

/// <summary>
/// 两条通道共用的 CDP 接线：ConnectOverCDP + 复用首个 context 开页。
/// 抽出来是为了保证两边的连接语义完全一致——一边用 Launch、一边用 Connect 这种错不能再犯。
/// </summary>
internal static class CdpConnector
{
    /// <summary>连接上限。本机回环，30 秒还没连上基本就是不正常。</summary>
    public const int DefaultTimeoutMilliseconds = 30_000;

    public static async Task<IPlaywright> CreatePlaywrightAsync()
    {
        try
        {
            return await Playwright.CreateAsync();
        }
        catch (Exception ex)
        {
            throw new BrowserChannelException(
                BrowserChannelFailure.ConnectFailed,
                $"Playwright 驱动初始化失败：{ex.Message}。确认 bin 目录下有 .playwright 驱动目录（缺了就重新 build）。",
                ex);
        }
    }

    public static async Task<IBrowser> ConnectAsync(IPlaywright playwright, string wsEndpoint, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
    {
        try
        {
            return await playwright.Chromium.ConnectOverCDPAsync(wsEndpoint, new BrowserTypeConnectOverCDPOptions
            {
                Timeout = timeoutMilliseconds,
            });
        }
        catch (Exception ex)
        {
            throw BrowserChannelException.ConnectFailed(wsEndpoint, ex.Message);
        }
    }

    /// <summary>在已有 context 上新开一页；没有 context 才建一个。</summary>
    public static async Task<IPage> NewPageAsync(IBrowser browser)
    {
        var context = browser.Contexts.Count > 0
            ? browser.Contexts[0]
            : await browser.NewContextAsync();

        return await context.NewPageAsync();
    }
}
