using FbAuto.Application.Execution;
using FbAuto.Core.Models;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 执行器注册表。重点是「没注册」和「注册重了」这两种情况不能静默：
/// 前者会让任务永远跑不起来，后者说明装配写错了。
/// </summary>
public sealed class TaskExecutorRegistryTests
{
    [Fact]
    public void 按类型取到对应的执行器()
    {
        var video = new StubTaskExecutor(TaskKind.VideoUploadGoogle);

        var registry = new TaskExecutorRegistry([video, new StubTaskExecutor(TaskKind.DeleteMessage)]);

        Assert.Same(video, registry.Find(TaskKind.VideoUploadGoogle));
        Assert.Null(registry.Find(TaskKind.ProfileEditBit));
    }

    [Fact]
    public void 没注册的类型返回空而不是抛异常()
    {
        var registry = new TaskExecutorRegistry([new StubTaskExecutor(TaskKind.VideoUploadGoogle)]);

        Assert.Null(registry.Find(TaskKind.DeleteMessage));
        Assert.Single(registry.RegisteredKinds);
    }

    [Fact]
    public void 同一类型注册两个执行器时直接报错()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TaskExecutorRegistry([
                new StubTaskExecutor(TaskKind.VideoUploadGoogle),
                new StubTaskExecutor(TaskKind.VideoUploadGoogle),
            ]));

        Assert.Contains("注册了两个执行器", exception.Message);
    }

    [Fact]
    public void 五个任务类型可以全部注册()
    {
        var registry = new TaskExecutorRegistry(
            TaskTypeCatalog.All.Select(t => new NotWiredTaskExecutor(t.Kind)));

        Assert.Equal(5, registry.RegisteredKinds.Count);
        Assert.All(TaskTypeCatalog.All, t => Assert.NotNull(registry.Find(t.Kind)));
    }
}
