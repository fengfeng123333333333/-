namespace FbAuto.Core.Models;

/// <summary>
/// 一次任务执行的结果。失败时必须带可复查线索：原因、页面 URL、截图路径。
/// 这三样缺一样，事后就只能重跑一遍复现，而重跑往往复现不出来。
/// </summary>
public sealed record TaskExecutionResult
{
    public required bool Success { get; init; }

    /// <summary>成功写摘要（含关键数据），失败写中文原因（说清是什么、下一步怎么办）。</summary>
    public required string Message { get; init; }

    /// <summary>失败时所在页面 URL。</summary>
    public string? PageUrl { get; init; }

    /// <summary>失败截图绝对路径。</summary>
    public string? ScreenshotPath { get; init; }

    public static TaskExecutionResult Ok(string message) => new() { Success = true, Message = message };

    public static TaskExecutionResult Fail(string message, string? pageUrl = null, string? screenshotPath = null)
        => new() { Success = false, Message = message, PageUrl = pageUrl, ScreenshotPath = screenshotPath };
}
