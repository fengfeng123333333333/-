using FbAuto.Core.Models;

namespace FbAuto.Core.Models;

/// <summary>
/// 一条待执行任务。S1 阶段只承载队列显示所需字段，参数字典留给 S4/S5 填充。
/// </summary>
public sealed class AutomatedTask
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required TaskKind Kind { get; init; }

    public required BrowserChannelKind Channel { get; init; }

    /// <summary>账号别名，对应账号清单的「别名」列（如 acc01）。</summary>
    public string AccountAlias { get; set; } = string.Empty;

    /// <summary>队列「浏览器」列显示名，如 Chrome-01 / Bit-05。</summary>
    public string BrowserName { get; set; } = string.Empty;

    public TaskRunStatus Status { get; set; } = TaskRunStatus.Pending;

    /// <summary>队列「结果」列文本：成功写耗时，失败写原因摘要。</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>失败截图的绝对路径，S8 落库后持久化。</summary>
    public string? ScreenshotPath { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    /// <summary>任务类型显示名，供列表直接绑定。</summary>
    public string DisplayName => TaskTypeCatalog.Resolve(Kind).DisplayName;
}
