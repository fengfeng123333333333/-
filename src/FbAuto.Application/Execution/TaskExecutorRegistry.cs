using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 任务类型 → 执行体的映射。没注册的类型不静默跳过，而是明确报失败：
/// 「没有执行器」是程序缺陷，不该表现成「任务成功」或「什么也没发生」。
/// </summary>
public sealed class TaskExecutorRegistry
{
    private readonly Dictionary<TaskKind, ITaskExecutor> _map;

    public TaskExecutorRegistry(IEnumerable<ITaskExecutor> executors)
    {
        ArgumentNullException.ThrowIfNull(executors);

        _map = [];
        foreach (var executor in executors)
        {
            if (!_map.TryAdd(executor.Kind, executor))
            {
                throw new InvalidOperationException(
                    $"任务类型 {executor.Kind} 注册了两个执行器：{_map[executor.Kind].GetType().Name} 与 {executor.GetType().Name}");
            }
        }
    }

    public IReadOnlyCollection<TaskKind> RegisteredKinds => _map.Keys;

    public ITaskExecutor? Find(TaskKind kind) => _map.GetValueOrDefault(kind);
}
