using FbAuto.Core.Models;

namespace FbAuto.Core.Models;

/// <summary>
/// 一种可添加的任务类型。显示名与原版左栏文案完全一致（含方括号与空格）。
/// </summary>
/// <param name="Kind">任务类型。</param>
/// <param name="DisplayName">界面显示名，取自 DLL 提取的界面文案。</param>
/// <param name="Channel">所属通道。</param>
public sealed record TaskTypeDescriptor(TaskKind Kind, string DisplayName, BrowserChannelKind Channel);

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
        new(TaskKind.DeleteMessage, "[删除消息]", BrowserChannelKind.Google),
    ];

    /// <summary>
    /// 按类型取描述，用于回填队列行的显示名。
    /// </summary>
    public static TaskTypeDescriptor Resolve(TaskKind kind) =>
        All.First(x => x.Kind == kind);
}
