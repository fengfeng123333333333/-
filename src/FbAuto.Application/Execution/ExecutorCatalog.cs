using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 任务类型 → 执行器的登记表。
/// </summary>
/// <remarks>
/// 从组合根抽出来的理由很实际：它得能被测。「谷歌版视频上传已经接上真流程」这件事，
/// 如果只写在 <c>App.xaml.cs</c> 的私有方法里，就没有任何用例能发现它被改回
/// <see cref="NotWiredTaskExecutor"/> —— 而那种回归的表现是「程序正常跑、任务全失败、
/// 日志说功能没做」，很容易被当成环境问题查半天。
/// </remarks>
public static class ExecutorCatalog
{
    /// <summary>按左栏任务类型表生成全部执行器，顺序与界面一致。</summary>
    public static IReadOnlyList<ITaskExecutor> CreateAll(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return [.. TaskTypeCatalog.All.Select(t => Create(t.Kind, config))];
    }

    /// <summary>
    /// 单个类型的执行器。没接入的类型落回「未接入」占位 ——
    /// 它明确记失败并说明原因，而不是假装成功或悄悄跳过。
    /// </summary>
    public static ITaskExecutor Create(TaskKind kind, AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return kind switch
        {
            TaskKind.VideoUploadGoogle => new VideoUploadExecutor(config),
            TaskKind.ProfileEditGoogle => new ProfileEditExecutor(config),
            TaskKind.DeleteMessage => new DeleteMessageExecutor(config),
            _ => new NotWiredTaskExecutor(kind),
        };
    }
}
