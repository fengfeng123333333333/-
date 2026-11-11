namespace FbAuto.Browser.Abstractions;

/// <summary>
/// 启动参数。Chrome 通道用 ProfileDirectory/ExecutablePath/Headless，比特通道只用 BitProfileId。
/// </summary>
public sealed record BrowserLaunchRequest
{
    /// <summary>Chrome 的 user-data-dir，每个账号一个，互不串味。</summary>
    public string ProfileDirectory { get; init; } = string.Empty;

    /// <summary>比特窗口 ID。比特通道必填。</summary>
    public string? BitProfileId { get; init; }

    /// <summary>代理，形如 127.0.0.1:7890 或 http://127.0.0.1:7890。为空表示直连。</summary>
    public string? Proxy { get; init; }

    /// <summary>Chrome 可执行文件路径。为空则自动查找。</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>无头模式。仅供自测与排障，正常业务必须带界面——无头更容易被识别。</summary>
    public bool Headless { get; init; }

    /// <summary>追加参数，原样透传给 Chrome。</summary>
    public IReadOnlyList<string> ExtraArguments { get; init; } = [];

    /// <summary>从启动到调试端口就绪的等待上限。</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
