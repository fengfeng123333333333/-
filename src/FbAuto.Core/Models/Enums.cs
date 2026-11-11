namespace FbAuto.Core.Models;

/// <summary>
/// 任务类型。字段取值与原版一致：视频上传 / 资料修改 各分谷歌版与比特版，另有删除消息。
/// </summary>
public enum TaskKind
{
    VideoUploadGoogle = 0,
    VideoUploadBit = 1,
    ProfileEditGoogle = 2,
    ProfileEditBit = 3,
    DeleteMessage = 4,
}

/// <summary>
/// 浏览器通道。<see cref="Google"/> 走 Chrome 调试端口，<see cref="Bit"/> 走比特浏览器本地接口。
/// </summary>
public enum BrowserChannelKind
{
    Google = 0,
    Bit = 1,
}

/// <summary>
/// 任务状态。原版状态机含暂停态（WaitIfPausedAsync 存在），此处保留 <see cref="Paused"/>。
/// </summary>
public enum TaskRunStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Success = 3,
    Failed = 4,
    Interrupted = 5,
}

/// <summary>
/// 状态的中文显示名。界面文案一字不改，改字须同步 docs/evidence 下的证据。
/// </summary>
public static class StatusText
{
    public static string ToDisplay(this TaskRunStatus status) => status switch
    {
        TaskRunStatus.Pending => "待执行",
        TaskRunStatus.Running => "执行中",
        TaskRunStatus.Paused => "已暂停",
        TaskRunStatus.Success => "成功",
        TaskRunStatus.Failed => "失败",
        TaskRunStatus.Interrupted => "中断",
        _ => "未知",
    };

    public static string ToDisplay(this BrowserChannelKind channel) => channel switch
    {
        BrowserChannelKind.Google => "谷歌版",
        BrowserChannelKind.Bit => "比特版",
        _ => "未知",
    };
}
