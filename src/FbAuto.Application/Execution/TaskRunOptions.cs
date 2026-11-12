using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 一轮执行的可调参数。值来自配置，不是常量——并发与间隔是风控换来的，必须可调。
/// </summary>
public sealed record TaskRunOptions
{
    public int ConcurrentCount { get; init; } = 1;

    /// <summary>两次浏览器启动之间的最小间隔（秒）。不是任务之间的间隔。</summary>
    public int LaunchIntervalSeconds { get; init; } = 3;

    public bool Headless { get; init; }

    /// <summary>失败截图目录。空则不截图。</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary>单条任务的总超时。计划里给的口径是单条不超过 4 分钟，这里留到 10 分钟兜底。</summary>
    public TimeSpan TaskTimeout { get; init; } = TimeSpan.FromMinutes(10);
}

/// <summary>一轮执行的汇总，用于界面提示与日志。</summary>
public sealed record TaskRunSummary(int Started, int Succeeded, int Failed, int Interrupted)
{
    public static TaskRunSummary Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>任务状态变化通知。界面据此更新行；调度器不碰 UI 线程。</summary>
public sealed class TaskStatusChangedEventArgs : EventArgs
{
    public required Guid TaskId { get; init; }

    public required TaskRunStatus Status { get; init; }

    public string Result { get; init; } = string.Empty;

    public string? ScreenshotPath { get; init; }
}
