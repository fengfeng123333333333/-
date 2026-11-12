namespace FbAuto.Automation.Uploads;

/// <summary>
/// 一个账号这一批要上传的内容。
/// </summary>
/// <param name="Videos">按文件名排好序的 MP4 绝对路径。</param>
/// <param name="Description">整段描述文本（描述文件整体一份，不是一行一条）。</param>
/// <param name="LogLines">按发生顺序要写进任务日志的行。</param>
/// <param name="Failure">为 null 表示可以继续；否则是中文失败原因。</param>
public sealed record VideoBatchPlan(
    IReadOnlyList<string> Videos,
    string Description,
    IReadOnlyList<string> LogLines,
    string? Failure)
{
    public bool IsValid => Failure is null && Videos.Count > 0;
}

/// <summary>
/// 把「视频目录 + 描述文件」变成一份可执行的批次清单。
/// </summary>
/// <remarks>
/// 原版的行为从 DLL 串还原出来是这样：
/// <list type="bullet">
/// <item>只认 <c>*.mp4</c>（串 <c>*.mp4</c>）。</item>
/// <item>**描述文件是一个 <c>.txt</c>，整体作为一段描述**，不是一行一条视频。
/// 依据是界面上只有一个「视频描述」框，且失败提示是
/// <c>没有选择描述文件或文件不存在，将以空描述上传</c> —— 若按行配对，缺行会报「描述条数不足」。</item>
/// <item>描述缺失不算错误，按空描述继续。这一点保持原样：把可选输入变成阻断条件，
/// 会让「只想传视频不写描述」这个正常用法跑不起来。</item>
/// </list>
/// </remarks>
public static class VideoBatchPlanner
{
    /// <summary>原版只上传这种扩展名。</summary>
    public const string VideoPattern = "*.mp4";

    /// <summary>
    /// 扫描目录，产出批次清单。不抛异常 —— 所有问题都以 <see cref="VideoBatchPlan.Failure"/> 返回，
    /// 因为这类错误要写进任务结果给用户看，而不是变成堆栈。
    /// </summary>
    public static VideoBatchPlan Plan(string? videoDirectory, string? descriptionFile)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(videoDirectory))
        {
            return Fail(lines, "没有配置视频目录");
        }

        if (!Directory.Exists(videoDirectory))
        {
            return Fail(lines, $"视频目录不存在: {videoDirectory}");
        }

        string[] videos;
        try
        {
            videos = Directory.GetFiles(videoDirectory, VideoPattern, SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            return Fail(lines, $"读取视频目录失败: {ex.Message}");
        }

        if (videos.Length == 0)
        {
            return Fail(lines, $"视频目录中没有 MP4 文件: {videoDirectory}");
        }

        // 排序不是为了好看：不排序时 GetFiles 的返回顺序取决于文件系统，
        // 同一份素材两次跑的上传顺序可能不同，出了问题没法复现。
        Array.Sort(videos, StringComparer.OrdinalIgnoreCase);

        var description = ReadDescription(descriptionFile, lines);

        lines.Add($"共 {videos.Length} 个 MP4 视频");
        lines.Add($"视频描述: {(string.IsNullOrEmpty(description) ? "（空）" : description)}");

        return new VideoBatchPlan(videos, description, lines, null);
    }

    private static string ReadDescription(string? descriptionFile, List<string> lines)
    {
        if (string.IsNullOrWhiteSpace(descriptionFile) || !File.Exists(descriptionFile))
        {
            lines.Add("没有选择描述文件或文件不存在，将以空描述上传");
            return string.Empty;
        }

        try
        {
            // 去掉 BOM 与首尾空白：素材里的 txt 多半是记事本存的，末尾带换行，
            // 原样填进去会让描述框结尾多一个空行。
            return File.ReadAllText(descriptionFile).Trim();
        }
        catch (Exception ex)
        {
            lines.Add($"读取描述文件失败（{ex.Message}），将以空描述上传");
            return string.Empty;
        }
    }

    private static VideoBatchPlan Fail(List<string> lines, string message)
    {
        lines.Add(message);
        return new VideoBatchPlan([], string.Empty, lines, message);
    }
}
