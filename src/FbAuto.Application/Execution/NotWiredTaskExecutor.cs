using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 尚未接入页面动作的任务类型占位执行器。
///
/// 为什么要它而不是「跳过」：跳过会让任务停在待执行，看起来像卡住；
/// 而「成功」是假的，更不能用。这里明确记失败，并把「没接入」和「执行出错」区分开——
/// 使用者看到结果列就知道是功能没做，不是他的账号或网络有问题。
/// </summary>
public sealed class NotWiredTaskExecutor : ITaskExecutor
{
    public NotWiredTaskExecutor(TaskKind kind)
    {
        Kind = kind;
    }

    public TaskKind Kind { get; }

    public Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var message =
            $"浏览器通道已验证可用，但「{context.Task.DisplayName}」的页面动作尚未接入（后续步骤实现），本次不算完成。";

        context.Logger.Warn(message);
        return Task.FromResult(TaskExecutionResult.Fail(message, context.Page.Url));
    }
}
