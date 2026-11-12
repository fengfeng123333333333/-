using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Media;
using FbAuto.Automation.Popups;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 「规则表 + 页面」的适配层。
/// 这段是纯逻辑与真实浏览器之间唯一的接缝，测牢了，等出口通了只需要补一个 Playwright 实现。
/// </summary>
public sealed class PageInteropExtensionTests
{
    [Fact]
    public async Task 观测弹窗_可见且可用()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors["div[aria-label='关闭']"] = true;
        page.EnabledSelectors["div[aria-label='关闭']"] = true;

        var observations = await page.ObservePopupsAsync();

        var hit = observations.Single(o => o.RuleId == "notification-close");
        Assert.True(hit.Visible);
        Assert.True(hit.Enabled);

        var miss = observations.Single(o => o.RuleId == "dialog-what-happened");
        Assert.False(miss.Visible);
    }

    [Fact]
    public async Task 观测弹窗_可见但禁用()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors["div[aria-label='关闭']"] = true;
        page.EnabledSelectors["div[aria-label='关闭']"] = false;

        var observations = await page.ObservePopupsAsync();

        var hit = observations.Single(o => o.RuleId == "notification-close");
        Assert.True(hit.Visible);
        Assert.False(hit.Enabled);
    }

    [Fact]
    public async Task 观测结果直接喂给决策器_能关掉()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors["div[aria-label='关闭']"] = true;
        page.EnabledSelectors["div[aria-label='关闭']"] = true;

        var observations = await page.ObservePopupsAsync();
        var step = PopupDismissPlanner.Decide(observations);
        await page.ExecuteAsync(step);

        Assert.Equal(["div[aria-label='关闭']"], page.Clicked);
        Assert.Equal(0, page.EscapeCount);
    }

    [Fact]
    public async Task 执行点击动作()
    {
        var page = new FakePageInterop();
        var step = new PopupDismissStep(PopupDismissAction.Click, "sel", "r", "理由", "日志行");

        var line = await page.ExecuteAsync(step);

        Assert.Equal("日志行", line);
        Assert.Equal(["sel"], page.Clicked);
    }

    [Fact]
    public async Task 执行按键动作()
    {
        var page = new FakePageInterop();
        var step = new PopupDismissStep(PopupDismissAction.PressEscape, null, null, "理由", "已按 Escape 关闭提示");

        await page.ExecuteAsync(step);

        Assert.Equal(1, page.EscapeCount);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 空操作不产生任何页面动作()
    {
        var page = new FakePageInterop();

        await page.ExecuteAsync(PopupDismissPlanner.Decide([]));

        Assert.Empty(page.Clicked);
        Assert.Equal(0, page.EscapeCount);
    }

    [Fact]
    public async Task 封面被拒检测()
    {
        var page = new FakePageInterop();
        Assert.False(await page.IsCoverRejectedAsync());

        page.VisibleSelectors[CoverRejectionSignals.RejectedDialogSelector] = true;
        Assert.True(await page.IsCoverRejectedAsync());
    }

    [Fact]
    public async Task 关闭封面被拒提示_有关闭按钮就点()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[CoverRejectionSignals.CloseButtonSelector] = true;

        var line = await page.DismissCoverRejectionAsync();

        Assert.Equal("已点击关闭按钮", line);
        Assert.Equal([CoverRejectionSignals.CloseButtonSelector], page.Clicked);
        Assert.Equal(0, page.EscapeCount);
    }

    [Fact]
    public async Task 关闭封面被拒提示_没有关闭按钮就按键()
    {
        var page = new FakePageInterop();

        var line = await page.DismissCoverRejectionAsync();

        Assert.Equal("已按 Escape 关闭提示", line);
        Assert.Empty(page.Clicked);
        Assert.Equal(1, page.EscapeCount);
    }

    [Fact]
    public async Task 未接通的占位实现_查询返回否_动作抛异常()
    {
        var page = NotConnectedPageInterop.Instance;

        Assert.False(await page.IsVisibleAsync("x"));
        Assert.False(await page.IsEnabledAsync("x"));
        Assert.Equal(0, await page.CountAsync("x"));

        // 动作必须抛，而不是静默什么都不做 —— 静默失败在自动化程序里最难排查。
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.ClickAsync("x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.PressEscapeAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.SetFileInputAsync("x", "a.png"));
    }
}
