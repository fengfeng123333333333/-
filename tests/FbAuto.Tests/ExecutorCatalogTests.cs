using FbAuto.Application.Execution;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Tests;

/// <summary>
/// 执行器登记表。这条测试防的是「接口明明写好了，注册却还指着占位实现」——
/// 那种回归在界面上完全看不出来：任务正常跑、正常失败、日志说「功能尚未接入」。
/// </summary>
public sealed class ExecutorCatalogTests
{
    private static readonly AppConfig Config = new();

    [Fact]
    public void 谷歌版视频上传已接上真执行器()
    {
        var executors = ExecutorCatalog.CreateAll(Config);

        var google = executors.Single(e => e.Kind == TaskKind.VideoUploadGoogle);

        Assert.IsType<VideoUploadExecutor>(google);
    }

    [Fact]
    public void 谷歌版资料修改已接上真执行器()
    {
        var executors = ExecutorCatalog.CreateAll(Config);

        var google = executors.Single(e => e.Kind == TaskKind.ProfileEditGoogle);

        Assert.IsType<ProfileEditExecutor>(google);
    }

    [Fact]
    public void 删除消息已接上真执行器()
    {
        var executors = ExecutorCatalog.CreateAll(Config);

        var deleter = executors.Single(e => e.Kind == TaskKind.DeleteMessage);

        Assert.IsType<DeleteMessageExecutor>(deleter);
    }

    [Theory]
    [InlineData(TaskKind.VideoUploadBit)]
    [InlineData(TaskKind.ProfileEditBit)]
    public void 其余类型仍明确标记为未接入(TaskKind kind)
    {
        var executor = ExecutorCatalog.Create(kind, Config);

        // 未接入不能被伪装成成功或静默跳过，否则使用者会以为账号或网络出了问题。
        Assert.IsType<NotWiredTaskExecutor>(executor);
    }

    [Fact]
    public void 每种任务类型都恰好有一个执行器()
    {
        var executors = ExecutorCatalog.CreateAll(Config);

        Assert.Equal(TaskTypeCatalog.All.Count, executors.Count);
        Assert.Equal(
            TaskTypeCatalog.All.Select(t => t.Kind).ToArray(),
            executors.Select(e => e.Kind).ToArray());
    }

    [Fact]
    public void 登记表能直接喂给注册表且不冲突()
    {
        // 注册表对重复类型会抛异常，这条用例保证 CreateAll 不会产出重复。
        var registry = new TaskExecutorRegistry(ExecutorCatalog.CreateAll(Config));

        Assert.Equal(TaskTypeCatalog.All.Count, registry.RegisteredKinds.Count);
    }
}
