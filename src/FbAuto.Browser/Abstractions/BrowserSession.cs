using FbAuto.Core.Models;

namespace FbAuto.Browser.Abstractions;

/// <summary>
/// 一个已打开的浏览器会话。字段都是「事后可核对」的东西：PID、端口、ws、profile 目录。
/// 出问题时把这些打进日志，比截图有用。
/// </summary>
public sealed record BrowserSession
{
    public required BrowserChannelKind Kind { get; init; }

    /// <summary>浏览器进程 ID。Chrome 是本进程，比特是它自己报的 pid。</summary>
    public int? ProcessId { get; init; }

    /// <summary>Chrome 的调试端口。IsAlive 要拿它去问 /json/version。</summary>
    public int? DebugPort { get; init; }

    /// <summary>CDP 的 ws 地址。</summary>
    public string? WsEndpoint { get; init; }

    /// <summary>比特返回的 http 端点（形如 127.0.0.1:5001），探活用。</summary>
    public string? HttpEndpoint { get; init; }

    /// <summary>Chrome 的 user-data-dir。</summary>
    public string ProfileDirectory { get; init; } = string.Empty;

    /// <summary>比特窗口 ID（profileId）。</summary>
    public string? BitProfileId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
}
