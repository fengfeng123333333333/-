using FbAuto.Application.Execution;
using FbAuto.Core.Models;

namespace FbAuto.Tests.Support;

/// <summary>
/// 可编程的执行器：测试用闭包决定它返回什么、抛什么、睡多久。
/// 默认成功，便于只关心调度行为的用例。
/// </summary>
public sealed class StubTaskExecutor : ITaskExecutor
{
    private readonly Func<TaskExecutionContext, CancellationToken, Task<TaskExecutionResult>> _handler;
    private int _callCount;

    public StubTaskExecutor(
        TaskKind kind,
        Func<TaskExecutionContext, CancellationToken, Task<TaskExecutionResult>>? handler = null)
    {
        Kind = kind;
        _handler = handler ?? ((_, _) => Task.FromResult(TaskExecutionResult.Ok("完成")));
    }

    public TaskKind Kind { get; }

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return _handler(context, cancellationToken);
    }
}
