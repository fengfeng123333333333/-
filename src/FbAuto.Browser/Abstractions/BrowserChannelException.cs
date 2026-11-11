namespace FbAuto.Browser.Abstractions;

/// <summary>浏览器层失败原因。上层据此决定是提示用户、换通道，还是直接判任务失败。</summary>
public enum BrowserChannelFailure
{
    /// <summary>找不到 Chrome 可执行文件。</summary>
    ExecutableNotFound,

    /// <summary>进程起不来，或起来后立刻退出。</summary>
    LaunchFailed,

    /// <summary>进程在，但调试端口迟迟不写出来。</summary>
    DebugEndpointTimeout,

    /// <summary>比特客户端没开，本地接口连不上。</summary>
    ClientNotRunning,

    /// <summary>比特窗口 ID 没给或不存在。</summary>
    ProfileNotFound,

    /// <summary>比特接口返回了非成功码。</summary>
    ApiRejected,

    /// <summary>CDP 连上了但握手失败。</summary>
    ConnectFailed,

    /// <summary>导航超时或页面打不开。</summary>
    NavigationFailed,

    /// <summary>等元素等到超时。</summary>
    ElementNotFound,

    /// <summary>会话已经关了或从未启动。</summary>
    SessionClosed,
}

/// <summary>
/// 浏览器层统一异常。消息直接给用户看，所以一律中文、说明原因、给出下一步动作。
/// </summary>
public sealed class BrowserChannelException : Exception
{
    public BrowserChannelException(BrowserChannelFailure reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public BrowserChannelFailure Reason { get; }

    public static BrowserChannelException ExecutableNotFound(string? path)
        => new(
            BrowserChannelFailure.ExecutableNotFound,
            string.IsNullOrWhiteSpace(path)
                ? "找不到 Chrome：自动查找失败。请安装谷歌浏览器，或在 config.json 的 ChromePath 里填 chrome.exe 的完整路径。"
                : $"找不到 Chrome：{path} 不存在。请检查 config.json 的 ChromePath，或确认该路径下确实有 chrome.exe。");

    public static BrowserChannelException LaunchFailed(string detail, Exception? inner = null)
        => new(BrowserChannelFailure.LaunchFailed, $"Chrome 启动失败：{detail}", inner);

    public static BrowserChannelException DebugEndpointTimeout(string profileDirectory, int seconds, int? processId = null)
        => new(
            BrowserChannelFailure.DebugEndpointTimeout,
            $"Chrome 已启动但调试端口在 {seconds} 秒内没有就绪（没读到 DevToolsActivePort 文件）。"
            + $"可能原因：该 profile 已被另一个浏览器实例占用；或安全软件拦截了本地调试端口。profile：{profileDirectory}"
            + (processId is null ? string.Empty : $"，PID：{processId}"));

    public static BrowserChannelException ClientNotRunning(string baseUrl, Exception? inner = null)
        => new(
            BrowserChannelFailure.ClientNotRunning,
            $"比特浏览器未启动：连不上本地接口 {baseUrl}。请先打开比特浏览器客户端并登录，再执行任务。",
            inner);

    public static BrowserChannelException ProfileNotFound(string? profileId)
        => new(
            BrowserChannelFailure.ProfileNotFound,
            string.IsNullOrWhiteSpace(profileId)
                ? "未指定比特窗口 ID。请在账号清单里填「比特窗口ID」，或换用谷歌版通道。"
                : $"比特浏览器里找不到窗口 ID 为 {profileId} 的实例。请核对账号清单里的「比特窗口ID」是否与客户端一致。");

    public static BrowserChannelException ApiRejected(string action, int? code, string? message)
        => new(
            BrowserChannelFailure.ApiRejected,
            $"比特接口拒绝{action}：code={code?.ToString() ?? "未知"}，{message ?? "无返回消息"}");

    public static BrowserChannelException ConnectFailed(string wsEndpoint, string detail)
        => new(
            BrowserChannelFailure.ConnectFailed,
            $"CDP 连接失败：{wsEndpoint}。{detail}");

    public static BrowserChannelException NavigationFailed(string url, int seconds, Exception? inner = null)
        => new(
            BrowserChannelFailure.NavigationFailed,
            $"打开页面超时或失败：{url}（上限 {seconds} 秒）。请检查网络出口是否能访问该站点。",
            inner);

    public static BrowserChannelException ElementNotFound(string selector, int seconds)
        => new(
            BrowserChannelFailure.ElementNotFound,
            $"等待元素超时：{selector}，{seconds} 秒内没有出现。页面可能改版，或账号登录态已失效。");

    public static BrowserChannelException SessionClosed()
        => new(BrowserChannelFailure.SessionClosed, "浏览器会话已关闭或从未启动，无法继续操作。");
}
