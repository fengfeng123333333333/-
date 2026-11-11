using FbAuto.Core.Models;
using Microsoft.Playwright;

namespace FbAuto.Browser.Abstractions;

/// <summary>
/// 浏览器通道。两条路（谷歌版 / 比特版）一个接口，上层任务逻辑不关心底下是谁。
/// 六个方法就是全部能力：启动 → 取 ws → 连上 → 开页 → 探活 → 关闭。
/// </summary>
public interface IBrowserChannel
{
    BrowserChannelKind Kind { get; }

    /// <summary>
    /// 启动浏览器并返回会话句柄。Chrome 自行拉起进程，比特调本地接口打开指定窗口。
    /// 失败一律抛 <see cref="BrowserChannelException"/>，消息是中文且指明原因。
    /// </summary>
    Task<BrowserSession> LaunchAsync(BrowserLaunchRequest request, CancellationToken cancellationToken = default);

    /// <summary>取 CDP 的 ws 地址。Chrome 读 DevToolsActivePort，比特在打开时接口已返回。</summary>
    Task<string> GetWsEndpointAsync(BrowserSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// 用 ConnectOverCDP 接管已存在的浏览器。
    /// 绝不能用 Launch：那等于开一个全新无登录态的浏览器，账号全要重登。
    /// </summary>
    Task<IBrowser> ConnectAsync(BrowserSession session, CancellationToken cancellationToken = default);

    /// <summary>在已有 context 上新开一页。每个任务一页，不留上一个任务的页面。</summary>
    Task<IPage> NewPageAsync(IBrowser browser, CancellationToken cancellationToken = default);

    /// <summary>存活判定：进程在 + CDP/接口可连，两条都满足才算活。只看进程会误判。</summary>
    Task<bool> IsAliveAsync(BrowserSession session, CancellationToken cancellationToken = default);

    /// <summary>只关自己开的这一个：Chrome 按 PID 关，比特调接口关窗口。绝不按进程名杀 chrome.exe。</summary>
    Task CloseAsync(BrowserSession session, CancellationToken cancellationToken = default);
}
