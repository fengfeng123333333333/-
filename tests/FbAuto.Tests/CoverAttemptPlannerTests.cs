using FbAuto.Automation.Media;

namespace FbAuto.Tests;

/// <summary>
/// 封面换图重试。
/// 这里最容易错的是「重试预算怎么算」：本地预校验跳过的图**没有上传**，
/// 不该占用上传次数，否则明明还有机会却提前放弃。
/// </summary>
public sealed class CoverAttemptPlannerTests
{
    [Fact]
    public void 没有候选图_状态为无候选()
    {
        var planner = new CoverAttemptPlanner([]);

        Assert.Equal(CoverAttemptStatus.NoCandidate, planner.Status);
        Assert.False(planner.HasNext);
        Assert.Null(planner.TakeNext());
        Assert.Equal("没有可用的封面图片", planner.BuildFailureSummary());
    }

    [Fact]
    public void 候选为null_不崩()
    {
        var planner = new CoverAttemptPlanner(null);

        Assert.Equal(CoverAttemptStatus.NoCandidate, planner.Status);
    }

    [Fact]
    public void 空白路径被过滤掉()
    {
        var planner = new CoverAttemptPlanner(["", "  ", "a.jpg"]);

        Assert.Equal("a.jpg", planner.TakeNext());
        Assert.Null(planner.TakeNext());
    }

    [Fact]
    public void 按顺序取图_每次消耗一次上传次数()
    {
        var planner = new CoverAttemptPlanner(["a.jpg", "b.jpg", "c.jpg"], maxAttempts: 3);

        Assert.Equal("a.jpg", planner.TakeNext());
        Assert.Equal(1, planner.UploadAttempts);
        Assert.Equal("b.jpg", planner.TakeNext());
        Assert.Equal(2, planner.UploadAttempts);
    }

    [Fact]
    public void 达到上限后不再给图并标记耗尽()
    {
        var planner = new CoverAttemptPlanner(["a.jpg", "b.jpg", "c.jpg", "d.jpg"], maxAttempts: 2);

        Assert.Equal("a.jpg", planner.TakeNext());
        planner.MarkRejected();
        Assert.Equal("b.jpg", planner.TakeNext());
        planner.MarkRejected();

        Assert.Null(planner.TakeNext());
        Assert.Equal(CoverAttemptStatus.Exhausted, planner.Status);
        Assert.False(planner.HasNext);
    }

    [Fact]
    public void 候选比上限少_取完即耗尽()
    {
        var planner = new CoverAttemptPlanner(["a.jpg"], maxAttempts: 5);

        Assert.Equal("a.jpg", planner.TakeNext());
        planner.MarkRejected();

        Assert.Equal(CoverAttemptStatus.Exhausted, planner.Status);
        Assert.Null(planner.TakeNext());
    }

    [Fact]
    public void 本地跳过不占上传次数()
    {
        // 核心行为：3 张图里 2 张本地被挡，剩下的 1 张仍应占用「第 1 次」上传，
        // 而不是因为「已经处理了 2 张」就以为用掉了 2 次预算。
        var planner = new CoverAttemptPlanner(["bad1.jpg", "bad2.jpg", "good.jpg"], maxAttempts: 3);

        planner.SkipLocally("bad1.jpg", "尺寸过小");
        planner.SkipLocally("bad2.jpg", "格式不支持");

        Assert.Equal(0, planner.UploadAttempts);
        Assert.Equal("good.jpg", planner.TakeNext());
        Assert.Equal(1, planner.UploadAttempts);
    }

    [Fact]
    public void 页面接受后状态为成功且不再给图()
    {
        var planner = new CoverAttemptPlanner(["a.jpg", "b.jpg"], maxAttempts: 3);
        planner.TakeNext();

        planner.MarkAccepted();

        Assert.Equal(CoverAttemptStatus.Accepted, planner.Status);
        Assert.False(planner.HasNext);
        Assert.Null(planner.TakeNext());
    }

    [Fact]
    public void 拒绝时记录已试过的图()
    {
        var planner = new CoverAttemptPlanner(["a.jpg", "b.jpg"], maxAttempts: 3);
        planner.TakeNext();
        planner.MarkRejected("尺寸不符合要求");

        Assert.Single(planner.Tried);
        Assert.Equal("a.jpg", planner.Tried[0]);
        Assert.Null(planner.Current);
    }

    [Fact]
    public void 拒绝不带原因时用原版兜底文案()
    {
        var planner = new CoverAttemptPlanner(["a.jpg"], maxAttempts: 1);
        planner.TakeNext();

        planner.MarkRejected();

        Assert.Contains(CoverRejectionSignals.RetryLine, planner.LogLines.Last());
    }

    [Fact]
    public void 拒绝带原因时原因在前兜底在后()
    {
        var planner = new CoverAttemptPlanner(["a.jpg"], maxAttempts: 1);
        planner.TakeNext();

        planner.MarkRejected("页面提示尺寸不符合要求");

        var line = planner.LogLines.Last();
        Assert.Contains("页面提示尺寸不符合要求", line);
        Assert.Contains(CoverRejectionSignals.RetryLine, line);
    }

    [Fact]
    public void 本地跳过会留下可读日志()
    {
        var planner = new CoverAttemptPlanner(["a.jpg"]);

        planner.SkipLocally(@"D:\imgs\a.jpg", "尺寸 200×100 小于最小要求 400×150");

        var line = Assert.Single(planner.LogLines);
        Assert.Contains("a.jpg", line);
        Assert.Contains("200×100", line);
    }

    [Fact]
    public void 失败总结带上跳过数量与尝试次数()
    {
        var planner = new CoverAttemptPlanner(["bad.jpg", "x.jpg", "y.jpg"], maxAttempts: 2);
        planner.SkipLocally("bad.jpg", "过小");
        planner.TakeNext();
        planner.MarkRejected();
        planner.TakeNext();
        planner.MarkRejected();

        var summary = planner.BuildFailureSummary();

        Assert.Contains("2 张封面图被页面拒绝", summary);
        Assert.Contains("1 张未通过本地预校验", summary);
        Assert.Contains("上限 2 次", summary);
    }

    [Fact]
    public void 最大尝试次数小于1直接抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CoverAttemptPlanner(["a.jpg"], maxAttempts: 0));
    }

    [Fact]
    public void 页面拒绝提示的选择器与文案锁定为原版取值()
    {
        // 这些串是要跟原版日志逐行对比的，改动必须是有意的。
        Assert.Equal("div[aria-label='请选择其他封面照片'][aria-modal='true'][role='dialog']", CoverRejectionSignals.RejectedDialogSelector);
        Assert.Equal("div[aria-label='关闭']", CoverRejectionSignals.CloseButtonSelector);
        Assert.Equal("已点击关闭按钮", CoverRejectionSignals.ClickedCloseLine);
        Assert.Equal("已按 Escape 关闭提示", CoverRejectionSignals.PressedEscapeLine);
        Assert.Equal("检测封面尺寸提示时异常: 原因", CoverRejectionSignals.ExceptionLine("原因"));
        Assert.Equal("检测到 请选择其他封面照片 提示，关闭提示...", CoverRejectionSignals.DetectionLine());
    }
}
