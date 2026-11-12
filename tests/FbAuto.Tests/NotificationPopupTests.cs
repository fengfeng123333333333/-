using FbAuto.Automation.Popups;

namespace FbAuto.Tests;

/// <summary>
/// 通知弹窗关闭决策。
/// 容易出事的地方不是「有弹窗怎么关」，而是「没弹窗时千万不要按 Escape」——
/// 那一下可能把正在编辑的资料弹窗关掉，前面的填写全部丢失。
/// </summary>
public sealed class NotificationPopupTests
{
    [Fact]
    public void 没有观测到弹窗_什么都不做()
    {
        var step = PopupDismissPlanner.Decide([]);

        Assert.Equal(PopupDismissAction.None, step.Action);
        Assert.True(step.IsNoOp);
        Assert.Equal(PopupDismissPlanner.NoPopupLine, step.LogLine);
    }

    [Fact]
    public void 观测为null_什么都不做()
    {
        var step = PopupDismissPlanner.Decide(null);

        Assert.True(step.IsNoOp);
    }

    [Fact]
    public void 全部不可见_什么都不做()
    {
        var observations = new[]
        {
            new PopupObservation("notification-close", Visible: false),
            new PopupObservation("dialog-what-happened", Visible: false),
        };

        var step = PopupDismissPlanner.Decide(observations);

        Assert.True(step.IsNoOp);
    }

    [Fact]
    public void 关闭按钮可见_点它()
    {
        var step = PopupDismissPlanner.Decide([new PopupObservation("notification-close", true)]);

        Assert.Equal(PopupDismissAction.Click, step.Action);
        Assert.Equal("div[aria-label='关闭']", step.Selector);
        Assert.Contains("已点击关闭按钮", step.LogLine);
        Assert.Contains("通知弹窗关闭按钮", step.LogLine);
    }

    [Fact]
    public void 两类弹窗同时出现_先处理异常对话框()
    {
        // 规则表顺序即优先级：「发生了什么」是异常提示，必须先关掉，否则后续操作都点不到。
        var observations = new[]
        {
            new PopupObservation("notification-close", true),
            new PopupObservation("dialog-what-happened", true),
        };

        var step = PopupDismissPlanner.Decide(observations);

        Assert.Equal("dialog-what-happened", step.RuleId);
        Assert.Equal("div[aria-modal='true'][role='dialog']:has-text('发生了什么')", step.Selector);
    }

    [Fact]
    public void 关闭按钮被禁用_改用Escape并说明原因()
    {
        var step = PopupDismissPlanner.Decide([new PopupObservation("notification-close", true, Enabled: false)]);

        Assert.Equal(PopupDismissAction.PressEscape, step.Action);
        Assert.Null(step.Selector);
        Assert.Contains("不可用", step.Reason);
        Assert.Equal("已按 Escape 关闭提示", step.LogLine);
    }

    [Fact]
    public void 没见过的弹窗_用Escape兜底()
    {
        var step = PopupDismissPlanner.Decide([new PopupObservation("some-new-dialog", true)]);

        Assert.Equal(PopupDismissAction.PressEscape, step.Action);
        Assert.Contains("未在规则表", step.Reason);
    }

    [Fact]
    public void 继续按钮默认不启用_不会误点()
    {
        // 原版这条规则的选择器是坏的（[object Object]），我们给了可用选择器但默认关闭。
        // 默认表里没有 dialog-continue，所以只能走 Escape。
        var step = PopupDismissPlanner.Decide([new PopupObservation("dialog-continue", true)]);

        Assert.Equal(PopupDismissAction.PressEscape, step.Action);
    }

    [Fact]
    public void 显式开启可选规则后_才点继续()
    {
        var rules = PopupDismissCatalog.Build(includeOptIn: true);

        var step = PopupDismissPlanner.Decide([new PopupObservation("dialog-continue", true)], rules);

        Assert.Equal(PopupDismissAction.Click, step.Action);
        Assert.Contains("继续", step.Selector);
    }

    [Fact]
    public void 可选规则确实存在且标注为低置信度()
    {
        var rule = Assert.Single(PopupDismissCatalog.OptIn);

        Assert.False(rule.HighConfidence);
        Assert.DoesNotContain("[object Object]", rule.Selector);
    }

    [Fact]
    public void 规则表里声明按Escape的规则_直接按Escape()
    {
        var rules = new[] { new PopupDismissRule("esc", "必须按键的提示", string.Empty, PopupDismissAction.PressEscape) };

        var step = PopupDismissPlanner.Decide([new PopupObservation("esc", true)], rules);

        Assert.Equal(PopupDismissAction.PressEscape, step.Action);
        Assert.Equal("esc", step.RuleId);
    }

    [Fact]
    public void 自定义规则表生效_证明顺序可配置()
    {
        var rules = new[]
        {
            new PopupDismissRule("b", "规则B", "sel-b", PopupDismissAction.Click),
            new PopupDismissRule("a", "规则A", "sel-a", PopupDismissAction.Click),
        };

        var step = PopupDismissPlanner.Decide(
            [new PopupObservation("a", true), new PopupObservation("b", true)],
            rules);

        Assert.Equal("b", step.RuleId); // 按传入顺序，不是按 Id 字典序
    }

    [Fact]
    public void 默认规则只有两条且顺序固定()
    {
        Assert.Equal(2, PopupDismissCatalog.Default.Count);
        Assert.Equal("dialog-what-happened", PopupDismissCatalog.Default[0].Id);
        Assert.Equal("notification-close", PopupDismissCatalog.Default[1].Id);
    }

    [Fact]
    public void 按Id查规则()
    {
        Assert.NotNull(PopupDismissCatalog.Find("notification-close"));
        Assert.Null(PopupDismissCatalog.Find("nope"));
        Assert.Null(PopupDismissCatalog.Find("dialog-continue"));            // 默认不含
        Assert.NotNull(PopupDismissCatalog.Find("dialog-continue", includeOptIn: true));
    }

    // ---------- 单轮上限 ----------

    [Fact]
    public void 空操作不消耗预算()
    {
        var pass = new PopupDismissPass(maxSteps: 2);

        pass.Record(PopupDismissPlanner.Decide([]));
        pass.Record(PopupDismissPlanner.Decide([]));

        Assert.Empty(pass.Steps);
        Assert.True(pass.CanContinue);
        Assert.False(pass.ReachedLimit);
    }

    [Fact]
    public void 达到上限后停手并标记仍可能有弹窗()
    {
        var pass = new PopupDismissPass(maxSteps: 2);
        var click = PopupDismissPlanner.Decide([new PopupObservation("notification-close", true)]);

        pass.Record(click);
        pass.Record(click);

        Assert.False(pass.CanContinue);
        Assert.True(pass.ReachedLimit);
        Assert.Contains("可能仍有弹窗", pass.BuildSummary());
    }

    [Fact]
    public void 上限小于1直接抛()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PopupDismissPass(0));
    }

    [Fact]
    public void 总结分开统计点击与按键()
    {
        var pass = new PopupDismissPass(maxSteps: 5);
        pass.Record(PopupDismissPlanner.Decide([new PopupObservation("notification-close", true)]));
        pass.Record(PopupDismissPlanner.Decide([new PopupObservation("unknown", true)]));

        var summary = pass.BuildSummary();

        Assert.Contains("关闭弹窗 2 个", summary);
        Assert.Contains("点击关闭 1 次", summary);
        Assert.Contains("按 Escape 1 次", summary);
    }

    [Fact]
    public void 没关任何弹窗时的总结()
    {
        var pass = new PopupDismissPass();

        Assert.Equal("本轮没有需要关闭的弹窗", pass.BuildSummary());
    }
}
