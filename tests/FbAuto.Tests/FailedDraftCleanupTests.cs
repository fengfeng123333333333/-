using FbAuto.Automation.Drafts;

namespace FbAuto.Tests;

/// <summary>
/// 失败草稿清理计划。
/// 最关键的一条：**必须倒序点**。正序点的话，删掉第 1 条后第 2 条前移成第 1 条，
/// 再去点「第 2 条」就点到了原本的第 3 条 —— 视频数 ≥3 时几乎必然删错。
/// </summary>
public sealed class FailedDraftCleanupTests
{
    [Fact]
    public void 三个都可移除_按倒序点击()
    {
        var plan = FailedDraftCleanupPlanner.Plan(
        [
            new DraftTarget(1, DraftPositionState.Removable),
            new DraftTarget(2, DraftPositionState.Removable),
            new DraftTarget(3, DraftPositionState.Removable),
        ]);

        Assert.Equal([3, 2, 1], plan.ClickOrder);
        Assert.Equal(3, plan.ClickCount);
        Assert.Equal(0, plan.SkippedCount);
    }

    [Fact]
    public void 倒序不是正序_这条防的是看起来能跑但会删错()
    {
        var plan = FailedDraftCleanupPlanner.Plan(
        [
            new DraftTarget(1, DraftPositionState.Removable),
            new DraftTarget(2, DraftPositionState.Removable),
        ]);

        Assert.NotEqual([1, 2], plan.ClickOrder);
        Assert.Equal([2, 1], plan.ClickOrder);
    }

    [Fact]
    public void 找不到按钮的位置被跳过并说明()
    {
        var plan = FailedDraftCleanupPlanner.Plan([new DraftTarget(2, DraftPositionState.NotFound)]);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DraftCleanupOutcome.SkipNotFound, step.Outcome);
        Assert.Equal("位置 2 没有找到对应移除按钮，跳过", step.LogLine);
        Assert.False(step.ShouldClick);
    }

    [Fact]
    public void 按钮不可用的位置被跳过并说明()
    {
        var plan = FailedDraftCleanupPlanner.Plan([new DraftTarget(3, DraftPositionState.Disabled)]);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DraftCleanupOutcome.SkipDisabled, step.Outcome);
        Assert.Equal("3 移除按钮不可用，跳过", step.LogLine);
    }

    [Fact]
    public void 混合状态_只点可移除的其余跳过()
    {
        var plan = FailedDraftCleanupPlanner.Plan(
        [
            new DraftTarget(1, DraftPositionState.Disabled),
            new DraftTarget(2, DraftPositionState.Removable),
            new DraftTarget(3, DraftPositionState.NotFound),
            new DraftTarget(4, DraftPositionState.Removable),
        ]);

        Assert.Equal([4, 2], plan.ClickOrder);
        Assert.Equal(2, plan.ClickCount);
        Assert.Equal(2, plan.SkippedCount);
        Assert.Equal(4, plan.Steps.Count);
    }

    [Fact]
    public void 日志里的文案与原版一致()
    {
        var plan = FailedDraftCleanupPlanner.Plan([new DraftTarget(5, DraftPositionState.Removable)]);

        Assert.Equal("已点击移除位置 5", plan.LogLines[0]);
        Assert.Equal("移除操作完成，共点击 1 个", plan.Summary);
    }

    [Fact]
    public void 全部跳过时总结说明没有可移除的()
    {
        var plan = FailedDraftCleanupPlanner.Plan(
        [
            new DraftTarget(1, DraftPositionState.NotFound),
            new DraftTarget(2, DraftPositionState.Disabled),
        ]);

        Assert.Equal(0, plan.ClickCount);
        Assert.Equal("没有可移除的失败视频", plan.Summary);
    }

    [Fact]
    public void 空列表_空计划()
    {
        var plan = FailedDraftCleanupPlanner.Plan([]);

        Assert.Empty(plan.Steps);
        Assert.Equal(0, plan.ClickCount);
        Assert.Equal("没有可移除的失败视频", plan.Summary);
    }

    [Fact]
    public void null_不崩()
    {
        var plan = FailedDraftCleanupPlanner.Plan(null);

        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void 重复位置只处理一次_避免误删下一条()
    {
        var plan = FailedDraftCleanupPlanner.Plan(
        [
            new DraftTarget(2, DraftPositionState.Removable),
            new DraftTarget(2, DraftPositionState.Removable),
        ]);

        Assert.Single(plan.Steps);
        Assert.Equal(1, plan.ClickCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 非法位置被跳过(int position)
    {
        var plan = FailedDraftCleanupPlanner.Plan([new DraftTarget(position, DraftPositionState.Removable)]);

        var step = Assert.Single(plan.Steps);
        Assert.Equal(DraftCleanupOutcome.SkipInvalidPosition, step.Outcome);
        Assert.Contains("非法", step.LogLine);
    }

    [Fact]
    public void 乱序输入也按位置倒序输出()
    {
        var plan = FailedDraftCleanupPlanner.Plan(
        [
            new DraftTarget(3, DraftPositionState.Removable),
            new DraftTarget(1, DraftPositionState.Removable),
            new DraftTarget(5, DraftPositionState.Removable),
        ]);

        Assert.Equal([5, 3, 1], plan.ClickOrder);
    }

    // ---------- 二级确认 ----------

    [Fact]
    public void 选项与确认按钮都在_可以继续()
    {
        var decision = DraftRemovalConfirmPlanner.Decide(removeOptionCount: 1, continueButtonCount: 2);

        Assert.True(decision.CanProceed);
        Assert.Equal(0, decision.OptionIndex);
        Assert.Equal(0, decision.ConfirmIndex);
        Assert.Contains("移除视频", decision.LogLine);
        Assert.Contains("确认继续按钮", decision.LogLine);
    }

    [Fact]
    public void 没有移除视频选项_不能继续()
    {
        var decision = DraftRemovalConfirmPlanner.Decide(0, 3);

        Assert.False(decision.CanProceed);
        Assert.Equal(-1, decision.OptionIndex);
        Assert.Contains("未找到 移除视频", decision.LogLine);
    }

    [Fact]
    public void 没有继续按钮_不能继续()
    {
        var decision = DraftRemovalConfirmPlanner.Decide(1, 0);

        Assert.False(decision.CanProceed);
        Assert.Contains("第 1 个继续按钮未找到", decision.LogLine);
    }
}
