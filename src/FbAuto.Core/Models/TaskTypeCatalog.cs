using FbAuto.Core.Models;

namespace FbAuto.Core.Models;

/// <summary>
/// 一种可添加的任务类型。显示名与原版左栏文案完全一致（含方括号与空格）。
/// </summary>
/// <param name="Kind">任务类型。</param>
/// <param name="DisplayName">界面显示名，取自 DLL 提取的界面文案。</param>
/// <param name="Channel">所属通道。仅当 <paramref name="FollowsBrowserType"/> 为 false 时使用。</param>
/// <param name="FollowsBrowserType">
/// 是否跟随界面顶部的「浏览器类型」下拉，而不是固定走 <paramref name="Channel"/>。
/// </param>
/// <remarks>
/// 为什么需要这个标记：「[删除消息]」在左栏**只有一个**，没有「- 谷歌版 / - 比特版」后缀，
/// 但原版串里出现了 <c>正在获取比特浏览器列表（删除消息）...</c> ——
/// 说明它会按当前的浏览器类型走比特浏览器。按固定通道处理等于把比特版账号全部排除在外。
/// </remarks>
public sealed record TaskTypeDescriptor(
    TaskKind Kind,
    string DisplayName,
    BrowserChannelKind Channel,
    bool FollowsBrowserType = false);

/// <summary>
/// 左栏「任务类型（点击添加）」的固定清单，顺序与原版一致。
/// </summary>
public static class TaskTypeCatalog
{
    public static IReadOnlyList<TaskTypeDescriptor> All { get; } =
    [
        new(TaskKind.VideoUploadGoogle, "[视频上传] - 谷歌版", BrowserChannelKind.Google),
        new(TaskKind.VideoUploadBit, "[视频上传] - 比特版", BrowserChannelKind.Bit),
        new(TaskKind.ProfileEditGoogle, "[资料修改] - 谷歌版", BrowserChannelKind.Google),
        new(TaskKind.ProfileEditBit, "[资料修改] - 比特版", BrowserChannelKind.Bit),
        // 删除消息只有一个入口，通道跟随界面顶部的「浏览器类型」。
        new(TaskKind.DeleteMessage, "[删除消息]", BrowserChannelKind.Google, FollowsBrowserType: true),
    ];

    /// <summary>
    /// 按类型取描述，用于回填队列行的显示名。
    /// </summary>
    public static TaskTypeDescriptor Resolve(TaskKind kind) =>
        All.First(x => x.Kind == kind);

    /// <summary>
    /// 任务类型所属的浏览器通道。
    /// </summary>
    /// <remarks>
    /// 执行器靠它决定「读哪一套配置」—— 谷歌版与比特版的业务路径是分开的（原版就是成对键）。
    /// 抽成一个方法而不是在执行器里写 switch：将来加第三条通道时，
    /// 漏改的地方会集中在 <see cref="All"/> 这一张表上，而不是散落在各执行器里。
    /// </remarks>
    public static BrowserChannelKind ChannelOf(TaskKind kind) => Resolve(kind).Channel;

    /// <summary>
    /// 实际入队时该用哪个通道。
    /// </summary>
    /// <param name="descriptor">任务类型描述。</param>
    /// <param name="global">界面顶部「浏览器类型」下拉对应的通道。</param>
    /// <remarks>
    /// 抽成纯函数是为了能测：界面层的 <c>AddTask</c> 没有测试覆盖（视图模型要构造一堆依赖），
    /// 而「删除消息在比特版下把账号挑错」这种事只会在真机上表现为「账号清单里明明有号却说没有」。
    /// </remarks>
    public static BrowserChannelKind ChannelFor(TaskTypeDescriptor descriptor, BrowserChannelKind global)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor.FollowsBrowserType ? global : descriptor.Channel;
    }
}
