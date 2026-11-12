using FbAuto.Application.Execution;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Tests;

/// <summary>
/// 执行器登记表。这条测试防的是「接口明明写好了，注册却还指着占位实现」——
/// 那种回归在界面上完全看不出来：任务正常跑、正常失败、日志说「功能尚未接入」。
/// </summary>
/// <remarks>
/// 从 S10 起五类任务全部接上真执行器，所以这里的重点从「哪些还没接」变成了
/// **「每一类有没有接对执行器、有没有读对配置」**：
/// 比特版与谷歌版共用同一个执行器类，靠 <c>Kind</c> 区分；
/// 若 <c>Kind</c> 传错，任务照样跑，但会用谷歌版的目录传比特版的视频，日志里看不出异常。
/// </remarks>
public sealed class ExecutorCatalogTests
{
    private static readonly AppConfig Config = new();

    [Fact]
    public void 五类任务全部接上了真执行器()
    {
        var executors = ExecutorCatalog.CreateAll(Config);

        // 占位实现还在（给将来新增的类型用），但登记表里不该再出现它。
        Assert.DoesNotContain(executors, e => e is NotWiredTaskExecutor);
    }

    [Theory]
    [InlineData(TaskKind.VideoUploadGoogle, typeof(VideoUploadExecutor))]
    [InlineData(TaskKind.VideoUploadBit, typeof(VideoUploadExecutor))]
    [InlineData(TaskKind.ProfileEditGoogle, typeof(ProfileEditExecutor))]
    [InlineData(TaskKind.ProfileEditBit, typeof(ProfileEditExecutor))]
    [InlineData(TaskKind.DeleteMessage, typeof(DeleteMessageExecutor))]
    public void 每种类型都落到预期实现上(TaskKind kind, Type expected)
    {
        var executor = ExecutorCatalog.Create(kind, Config);

        Assert.IsType(expected, executor);
        Assert.Equal(kind, executor.Kind);
    }

    /// <summary>
    /// 比特版的执行器必须读比特版那套配置。
    /// </summary>
    /// <remarks>
    /// 这条是「比特版差异覆写」的核心断言。原版 settings.json 里业务路径全是成对的
    /// （<c>GoogleVideoPath</c> / <c>BitVideoPath</c>），读错一边的表现是：
    /// 比特版任务拿谷歌版的目录去上传 —— 不报错、不失败，只是传错了东西。
    /// </remarks>
    [Fact]
    public void 比特版执行器读比特版配置()
    {
        var config = new AppConfig
        {
            VideoDirectory = @"D:\google-videos",
            BitVideoDirectory = @"D:\bit-videos",
            SignatureFile = @"D:\google\sig.txt",
            BitSignatureFile = @"D:\bit\sig.txt",
        };

        var video = (VideoUploadExecutor)ExecutorCatalog.Create(TaskKind.VideoUploadBit, config);
        var profile = (ProfileEditExecutor)ExecutorCatalog.Create(TaskKind.ProfileEditBit, config);

        Assert.Equal(BrowserChannelKind.Bit, video.Profile.Channel);
        Assert.Equal(@"D:\bit-videos", video.Profile.VideoDirectory);
        Assert.Equal(BrowserChannelKind.Bit, profile.Profile.Channel);
        Assert.Equal(@"D:\bit\sig.txt", profile.Profile.SignatureFile);
    }

    [Fact]
    public void 谷歌版执行器读谷歌版配置()
    {
        var config = new AppConfig
        {
            VideoDirectory = @"D:\google-videos",
            BitVideoDirectory = @"D:\bit-videos",
            SignatureFile = @"D:\google\sig.txt",
            BitSignatureFile = @"D:\bit\sig.txt",
        };

        var video = (VideoUploadExecutor)ExecutorCatalog.Create(TaskKind.VideoUploadGoogle, config);
        var profile = (ProfileEditExecutor)ExecutorCatalog.Create(TaskKind.ProfileEditGoogle, config);

        Assert.Equal(BrowserChannelKind.Google, video.Profile.Channel);
        Assert.Equal(@"D:\google-videos", video.Profile.VideoDirectory);
        Assert.Equal(@"D:\google\sig.txt", profile.Profile.SignatureFile);
    }

    /// <summary>
    /// 任务类型到「默认通道」的映射。带版本后缀的四类就是它们各自的通道。
    /// </summary>
    /// <remarks>
    /// 删除消息在左栏只有一个入口，实际入队时跟随界面顶部的「浏览器类型」下拉，
    /// 这里返回的是**默认值**（谷歌版），只用于「取哪一套配置」—— 而删除消息的配置不分通道，
    /// 所以取哪一套都一样。真正决定用哪个浏览器的是 <c>TaskTypeCatalog.ChannelFor</c>，
    /// 由 <see cref="TaskTypeCatalogTests"/> 覆盖。
    /// </remarks>
    [Fact]
    public void 任务类型到通道的映射与左栏一致()
    {
        Assert.Equal(BrowserChannelKind.Google, TaskTypeCatalog.ChannelOf(TaskKind.VideoUploadGoogle));
        Assert.Equal(BrowserChannelKind.Bit, TaskTypeCatalog.ChannelOf(TaskKind.VideoUploadBit));
        Assert.Equal(BrowserChannelKind.Google, TaskTypeCatalog.ChannelOf(TaskKind.ProfileEditGoogle));
        Assert.Equal(BrowserChannelKind.Bit, TaskTypeCatalog.ChannelOf(TaskKind.ProfileEditBit));
        Assert.Equal(BrowserChannelKind.Google, TaskTypeCatalog.ChannelOf(TaskKind.DeleteMessage));
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
