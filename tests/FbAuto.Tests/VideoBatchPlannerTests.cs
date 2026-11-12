using FbAuto.Automation.Uploads;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 视频批次清单。原版是「一个账号一次传完目录里所有 MP4」，
/// 不是一条任务一个视频 —— 这一点直接决定了上传判定的粒度。
/// </summary>
public sealed class VideoBatchPlannerTests
{
    private static void Touch(string path)
    {
        File.WriteAllBytes(path, [0x00, 0x01]);
    }

    [Fact]
    public void 没配置视频目录时明确失败()
    {
        var plan = VideoBatchPlanner.Plan(null, null);

        Assert.False(plan.IsValid);
        Assert.Contains("没有配置视频目录", plan.Failure);
    }

    [Fact]
    public void 目录不存在时明确失败并带上路径()
    {
        using var dir = new TempDirectory();
        var missing = dir.Combine("video-missing");

        var plan = VideoBatchPlanner.Plan(missing, null);

        Assert.False(plan.IsValid);
        Assert.Contains("视频目录不存在", plan.Failure);
        Assert.Contains(missing, plan.Failure);
    }

    [Fact]
    public void 目录里没有MP4时明确失败()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "note.txt"));

        var plan = VideoBatchPlanner.Plan(videoDir, null);

        Assert.False(plan.IsValid);
        Assert.Contains("没有 MP4 文件", plan.Failure);
    }

    [Fact]
    public void 只认MP4_其他扩展名不进批次()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "a.mp4"));
        Touch(Path.Combine(videoDir, "b.mov"));
        Touch(Path.Combine(videoDir, "c.txt"));

        var plan = VideoBatchPlanner.Plan(videoDir, null);

        Assert.True(plan.IsValid);
        Assert.Single(plan.Videos);
        Assert.EndsWith("a.mp4", plan.Videos[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MP4扩展名大小写都认()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "UP.MP4"));

        var plan = VideoBatchPlanner.Plan(videoDir, null);

        Assert.True(plan.IsValid);
        Assert.Single(plan.Videos);
    }

    [Fact]
    public void 按文件名排序_保证两次跑的顺序一致()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);

        // 故意乱序创建。GetFiles 的返回顺序取决于文件系统，不排序就没法复现。
        Touch(Path.Combine(videoDir, "c.mp4"));
        Touch(Path.Combine(videoDir, "a.mp4"));
        Touch(Path.Combine(videoDir, "b.mp4"));

        var plan = VideoBatchPlanner.Plan(videoDir, null);

        Assert.Equal(
            ["a.mp4", "b.mp4", "c.mp4"],
            plan.Videos.Select(p => Path.GetFileName(p)!).ToArray());
    }

    [Fact]
    public void 描述文件整体作为一段描述()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "a.mp4"));

        var descFile = dir.Combine("desc.txt");
        File.WriteAllText(descFile, "  第一行\r\n第二行  ");

        var plan = VideoBatchPlanner.Plan(videoDir, descFile);

        Assert.True(plan.IsValid);
        Assert.Equal("第一行\r\n第二行", plan.Description);
    }

    [Fact]
    public void 描述文件不存在时按空描述继续_不阻断()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "a.mp4"));

        var plan = VideoBatchPlanner.Plan(videoDir, dir.Combine("nope.txt"));

        // 只传视频不写描述是正常用法，不该因此跑不起来。
        Assert.True(plan.IsValid);
        Assert.Equal(string.Empty, plan.Description);
        Assert.Contains(plan.LogLines, x => x.Contains("以空描述上传"));
    }

    [Fact]
    public void 日志里写出视频个数与描述内容()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "a.mp4"));
        Touch(Path.Combine(videoDir, "b.mp4"));

        var descFile = dir.Combine("desc.txt");
        File.WriteAllText(descFile, "文案");

        var plan = VideoBatchPlanner.Plan(videoDir, descFile);

        Assert.Contains(plan.LogLines, x => x.Contains("共 2 个 MP4 视频"));
        Assert.Contains(plan.LogLines, x => x.Contains("视频描述: 文案"));
    }

    [Fact]
    public void 空描述文件视为空描述()
    {
        using var dir = new TempDirectory();
        var videoDir = dir.Combine("videos");
        Directory.CreateDirectory(videoDir);
        Touch(Path.Combine(videoDir, "a.mp4"));

        var descFile = dir.Combine("desc.txt");
        File.WriteAllText(descFile, "   \r\n  ");

        var plan = VideoBatchPlanner.Plan(videoDir, descFile);

        Assert.True(plan.IsValid);
        Assert.Equal(string.Empty, plan.Description);
    }
}
