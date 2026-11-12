using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Flows;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Popups;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S6 集测：三个组合流程在真 Chrome + 本地仿真页上跑通。
/// </summary>
/// <remarks>
/// 这些用例补的正是 S5 的缺口 —— S5 的 357 条用例证明的是「判断逻辑对」，一条也没证明
/// 「页面动作真的能落地」。这里每条用例都要求页面状态发生**可观测的变化**：
/// 上传记录多了哪个文件名、弹窗还在不在、剩下列表的 data-id 是哪几个。
///
/// 其中最有价值的两条：
/// <list type="bullet">
/// <item>「本地预检挡掉的图不能出现在上传记录里」—— 证明预校验真的省掉了一次上传，
/// 而不是只写了一行日志。</item>
/// <item>「倒序移除后三个条目全清，删除顺序是 3→2→1」—— 用真实 DOM 的位移证明倒序是对的；
/// 若改成正序，剩下列表里会留下第 2 条。</item>
/// </list>
/// </remarks>
[Collection("chrome")]
public sealed class PageFlowIntegrationTests : ChromePageTestBase
{
    private static PlaywrightPageInterop Interop(Microsoft.Playwright.IPage page) =>
        new(page, new PageInteropOptions(ActionTimeout: TimeSpan.FromSeconds(5)));

    // ── 封面换图 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task 封面换图_本地挡掉的图不会真的上传()
    {
        var tooSmall = ImageFixtures.WritePng(Dir.Path, "small.png", 100, 100);
        var rejected = ImageFixtures.WritePng(Dir.Path, "reject-cover.png", 820, 312);
        var good = ImageFixtures.WritePng(Dir.Path, "good.png", 820, 312);

        var page = await OpenPageAsync(FacebookMimicPage.CoverEditor());
        var interop = Interop(page);

        var result = await CoverAttemptFlow.RunForCoverAsync(
            interop,
            [tooSmall, rejected, good],
            rejectionWindow: TimeSpan.FromSeconds(2),
            dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.True(result.Accepted, result.Summary);
        Assert.Equal(2, result.UploadAttempts);

        // 关键断言：small.png 被本地预检挡掉，页面从未收到它。
        Assert.Equal(["reject-cover.png", "good.png"], await ReadAsync<string[]>(page, "() => window.__uploads"));
        Assert.Equal("good.png", await ReadAsync<string>(page, "() => window.__accepted"));

        Assert.False(await interop.IsCoverRejectedAsync(), "拒绝提示应当已被关掉");
        Assert.Contains(result.LogLines, line => line.Contains("本地预校验未通过"));
        Assert.Contains(result.LogLines, line => line.Contains("检测到"));
        Assert.Contains(result.LogLines, line => line.Contains("已点击关闭按钮"));
    }

    [Fact]
    public async Task 封面换图_全部被页面拒绝时判失败并记次数()
    {
        var first = ImageFixtures.WritePng(Dir.Path, "reject-1.png", 820, 312);
        var second = ImageFixtures.WritePng(Dir.Path, "reject-2.png", 820, 312);

        var page = await OpenPageAsync(FacebookMimicPage.CoverEditor());
        var interop = Interop(page);

        var result = await CoverAttemptFlow.RunForCoverAsync(
            interop,
            [first, second],
            maxAttempts: 3,
            rejectionWindow: TimeSpan.FromSeconds(2),
            dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.False(result.Accepted);
        Assert.Equal(FbAuto.Automation.Media.CoverAttemptStatus.Exhausted, result.Status);
        Assert.Equal(2, result.UploadAttempts);
        Assert.Contains("2 张封面图被页面拒绝", result.Summary);
        Assert.Equal(2, (await ReadAsync<string[]>(page, "() => window.__uploads")).Length);
    }

    [Fact]
    public async Task 封面换图_对话框没有关闭按钮时按_Escape()
    {
        var rejected = ImageFixtures.WritePng(Dir.Path, "reject-cover.png", 820, 312);
        var good = ImageFixtures.WritePng(Dir.Path, "good.png", 820, 312);

        var page = await OpenPageAsync(FacebookMimicPage.CoverEditor(withCloseButton: false));
        var interop = Interop(page);

        var result = await CoverAttemptFlow.RunForCoverAsync(
            interop,
            [rejected, good],
            rejectionWindow: TimeSpan.FromSeconds(2),
            dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.True(result.Accepted, result.Summary);
        Assert.Contains(result.LogLines, line => line.Contains("已按 Escape 关闭提示"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__escaped"));
        Assert.Equal("good.png", await ReadAsync<string>(page, "() => window.__accepted"));
    }

    [Fact]
    public async Task 封面换图_头像路径不做尺寸预检()
    {
        // 头像会被裁成方形，封面的 400×150 最小尺寸对它没有意义。
        // 100×100 的头像必须能直接上传，而不是被本地预检挡掉。
        var tinyAvatar = ImageFixtures.WritePng(Dir.Path, "tiny-avatar.png", 100, 100);
        var good = ImageFixtures.WritePng(Dir.Path, "good-avatar.png", 400, 400);

        var page = await OpenPageAsync(FacebookMimicPage.AvatarEditor());
        var interop = Interop(page);

        var result = await CoverAttemptFlow.RunForAvatarAsync(
            interop,
            [tinyAvatar, good],
            rejectionWindow: TimeSpan.FromSeconds(2),
            dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.True(result.Accepted, result.Summary);
        Assert.Equal(1, result.UploadAttempts);
        Assert.Equal(["tiny-avatar.png"], await ReadAsync<string[]>(page, "() => window.__uploads"));
        Assert.DoesNotContain(result.LogLines, line => line.Contains("本地预校验"));
    }

    // ── 弹窗清理 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task 弹窗清理_点关闭按钮后弹窗真的消失()
    {
        var page = await OpenPageAsync(FacebookMimicPage.NotificationPopup());
        var interop = Interop(page);

        var result = await PopupCleanupFlow.RunAsync(interop, dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.True(result.DismissedAny);
        Assert.Single(result.Steps);
        Assert.Equal(PopupDismissAction.Click, result.Steps[0].Action);
        Assert.False(result.StillVisible);
        Assert.False(await interop.IsVisibleAsync("div[aria-label='关闭']"));
        Assert.Equal(1, await ReadAsync<int>(page, "() => window.__clicks"));
        Assert.False(await ReadAsync<bool>(page, "() => window.__escaped"));
    }

    [Fact]
    public async Task 弹窗清理_关闭按钮禁用时改用_Escape()
    {
        var page = await OpenPageAsync(FacebookMimicPage.NotificationPopup(ariaDisabled: true));
        var interop = Interop(page);

        var result = await PopupCleanupFlow.RunAsync(interop, dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.True(result.DismissedAny);
        Assert.Equal(PopupDismissAction.PressEscape, result.Steps[0].Action);
        Assert.True(await ReadAsync<bool>(page, "() => window.__escaped"));
        Assert.False(await interop.IsVisibleAsync("div[aria-label='关闭']"));

        // 点了一次但没生效：这次点击是被禁用判出来挡掉的，不是点完才发现。
        Assert.Equal(0, await ReadAsync<int>(page, "() => window.__clicks"));
    }

    [Fact]
    public async Task 弹窗清理_没有弹窗时绝不按_Escape()
    {
        var page = await OpenPageAsync(FacebookMimicPage.EditorDialogOpen());
        var interop = Interop(page);

        var result = await PopupCleanupFlow.RunAsync(interop, dismissTimeout: TimeSpan.FromSeconds(1));

        Assert.False(result.DismissedAny);
        Assert.Empty(result.Steps);
        Assert.False(result.StillVisible);
        Assert.Contains(PopupDismissPlanner.NoPopupLine, result.LogLines);

        // 这条是整组里最关键的：按了 Escape，用户填了一半的资料就没了。
        Assert.False(await ReadAsync<bool>(page, "() => window.__escaped"));
        Assert.True(await interop.IsVisibleAsync("#editor"), "编辑对话框必须还开着");
        Assert.Equal(
            "已经填好的内容",
            await page.Locator("#name").InputValueAsync());
    }

    // ── 失败草稿清理 ──────────────────────────────────────────────────────

    [Fact]
    public async Task 草稿清理_倒序移除三条全清且顺序是倒的()
    {
        var page = await OpenPageAsync(FacebookMimicPage.DraftList(3));
        var interop = Interop(page);

        var result = await FailedDraftFlow.RunAsync(
            interop,
            [1, 2, 3],
            dialogTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(3, result.ClickedCount);
        Assert.Equal(3, result.ConfirmedCount);

        // 删除顺序必须是 3→2→1：正序会因条目前移把第 2 条漏掉。
        Assert.Equal(["3", "2", "1"], await ReadAsync<string[]>(page, "() => window.__removed"));
        Assert.Equal(0, await interop.CountAsync("#list .row"));
        Assert.False(await ReadAsync<bool>(page, "() => window.__escaped"));
    }

    [Fact]
    public async Task 草稿清理_位置被禁用时跳过且不影响其他条目()
    {
        var page = await OpenPageAsync(FacebookMimicPage.DraftList(3, disabledPosition: 2));
        var interop = Interop(page);

        var result = await FailedDraftFlow.RunAsync(
            interop,
            [1, 2, 3],
            dialogTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(2, result.ClickedCount);
        Assert.Equal(["3", "1"], await ReadAsync<string[]>(page, "() => window.__removed"));
        Assert.Equal(1, await interop.CountAsync("#list .row"));
        Assert.True(await page.Locator("[data-id='2']").IsVisibleAsync(), "被禁用的第 2 条必须还在");
        Assert.Contains(result.LogLines, line => line.Contains("2 移除按钮不可用，跳过"));
    }

    [Fact]
    public async Task 草稿清理_列表为空时不点任何东西()
    {
        var page = await OpenPageAsync(FacebookMimicPage.DraftList(0, withHiddenLeadingRow: false));
        var interop = Interop(page);

        var result = await FailedDraftFlow.RunAsync(
            interop,
            [1],
            dialogTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(0, result.ClickedCount);
        Assert.Empty(await ReadAsync<string[]>(page, "() => window.__removed"));
        Assert.Contains(result.LogLines, line => line.Contains("位置 1 没有找到对应移除按钮，跳过"));
        Assert.Contains("没有可移除的失败视频", result.LogLines);
    }
}
