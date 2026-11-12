using FbAuto.Automation.Messages;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 删除消息的纯逻辑：轮次规划器 + 编排（用假页面）。
/// </summary>
/// <remarks>
/// 删除是**不可逆**动作，所以这里的用例不是在测「功能能用」，而是在钉住**什么时候必须停**：
/// 到上限要停、列表空要停、连续失败要停。少一条都可能表现为「在真机上多删了几十条」。
/// </remarks>
public sealed class MessageDeleteTests
{
    // ── 轮次规划器 ────────────────────────────────────────────────────────

    [Fact]
    public void 没到上限且列表有货时继续()
    {
        var planner = new MessageDeletePlanner(limit: 20);

        var decision = planner.Plan(remaining: 8);

        Assert.True(decision.ShouldContinue);
        Assert.Equal(MessageDeleteStop.Continue, decision.Stop);
        Assert.Contains("第 1 条", decision.LogLine, StringComparison.Ordinal);
    }

    [Fact]
    public void 删到上限就停()
    {
        var planner = new MessageDeletePlanner(limit: 2);
        planner.RecordSuccess(5, 4);
        planner.RecordSuccess(4, 3);

        var decision = planner.Plan(remaining: 3);

        Assert.False(decision.ShouldContinue);
        Assert.Equal(MessageDeleteStop.LimitReached, decision.Stop);
        Assert.Equal(2, planner.Deleted);
        Assert.Contains("上限 2 条", decision.LogLine, StringComparison.Ordinal);
    }

    /// <summary>
    /// 上限 0 表示不限。这条很重要：使用者把上限设成 0 是想「清空」，
    /// 若代码把 0 当成「一条都不删」，功能会表现为「跑了但什么都没做」。
    /// </summary>
    [Fact]
    public void 上限为零表示不限()
    {
        var planner = new MessageDeletePlanner(limit: 0);
        for (var i = 0; i < 50; i++)
        {
            planner.RecordSuccess(50 - i, 49 - i);
        }

        var decision = planner.Plan(remaining: 1);

        Assert.True(decision.ShouldContinue);
        Assert.Equal(0, planner.Limit);
    }

    [Fact]
    public void 负上限也按不限处理()
    {
        var planner = new MessageDeletePlanner(limit: -5);

        var decision = planner.Plan(remaining: 3);

        Assert.True(decision.ShouldContinue);
        Assert.Equal(0, planner.Limit);
    }

    [Fact]
    public void 列表空了就停()
    {
        var planner = new MessageDeletePlanner(limit: 20);

        var decision = planner.Plan(remaining: 0);

        Assert.False(decision.ShouldContinue);
        Assert.Equal(MessageDeleteStop.ListEmpty, decision.Stop);
    }

    /// <summary>
    /// 上限与空列表同时成立时报「已达上限」。这不是吹毛求疵：
    /// 报错原因决定了使用者下一步看哪里 —— 报「空」他会以为号里本来就没消息，
    /// 实际是上限把流程卡在了中途。
    /// </summary>
    [Fact]
    public void 上限与空列表同时成立时报已达上限()
    {
        var planner = new MessageDeletePlanner(limit: 1);
        planner.RecordSuccess(1, 0);

        var decision = planner.Plan(remaining: 0);

        Assert.Equal(MessageDeleteStop.LimitReached, decision.Stop);
    }

    [Fact]
    public void 连续失败到阈值就停()
    {
        var planner = new MessageDeletePlanner(limit: 20, maxConsecutiveFailures: 3);
        planner.RecordFailure("点不动");
        planner.RecordFailure("点不动");
        planner.RecordFailure("点不动");

        var decision = planner.Plan(remaining: 9);

        Assert.False(decision.ShouldContinue);
        Assert.Equal(MessageDeleteStop.TooManyFailures, decision.Stop);
        Assert.Contains("连续 3 条", decision.LogLine, StringComparison.Ordinal);
    }

    [Fact]
    public void 失败不到阈值时继续()
    {
        var planner = new MessageDeletePlanner(limit: 20, maxConsecutiveFailures: 3);
        planner.RecordFailure("点不动");
        planner.RecordFailure("点不动");

        var decision = planner.Plan(remaining: 9);

        Assert.True(decision.ShouldContinue);
    }

    [Fact]
    public void 成功一次就把连续失败清零()
    {
        var planner = new MessageDeletePlanner(limit: 20, maxConsecutiveFailures: 3);
        planner.RecordFailure("点不动");
        planner.RecordFailure("点不动");
        planner.RecordSuccess(9, 8);
        planner.RecordFailure("点不动");
        planner.RecordFailure("点不动");

        var decision = planner.Plan(remaining: 8);

        Assert.True(decision.ShouldContinue);
    }

    [Fact]
    public void 失败不计入删除条数()
    {
        var planner = new MessageDeletePlanner(limit: 5);
        planner.RecordFailure("点不动");

        Assert.Equal(0, planner.Deleted);
        Assert.Equal(1, planner.ConsecutiveFailures);
    }

    /// <summary>
    /// 「删了但数量没降」默认**不**作为退出条件。
    /// </summary>
    /// <remarks>
    /// Messenger 的会话列表是虚拟滚动：删一条会从下面补一条进来，渲染行数本来就不变。
    /// 若默认按「数量没降」判停，真机上会表现为「删两条就停」，功能等于废掉。
    /// 这条用例就是把这个决定钉住 —— 谁要是把默认值改成启用，它会红。
    /// </remarks>
    [Fact]
    public void 默认不因数量没降而停()
    {
        var planner = new MessageDeletePlanner(limit: 20);
        planner.RecordSuccess(10, 10);
        planner.RecordSuccess(10, 10);
        planner.RecordSuccess(10, 10);

        var decision = planner.Plan(remaining: 10);

        Assert.True(decision.ShouldContinue);
        Assert.Equal(3, planner.Deleted);
    }

    [Fact]
    public void 显式开启后连续两次数量没降就停()
    {
        var planner = new MessageDeletePlanner(limit: 20, stallThreshold: 2);
        planner.RecordSuccess(10, 10);

        // 第一次还没到阈值
        Assert.True(planner.Plan(remaining: 10).ShouldContinue);

        planner.RecordSuccess(10, 10);
        var decision = planner.Plan(remaining: 10);

        Assert.False(decision.ShouldContinue);
        Assert.Equal(MessageDeleteStop.NoProgress, decision.Stop);
    }

    [Fact]
    public void 数量降下来就清掉停滞计数()
    {
        var planner = new MessageDeletePlanner(limit: 20, stallThreshold: 2);
        planner.RecordSuccess(10, 10);
        planner.RecordSuccess(10, 9);
        planner.RecordSuccess(9, 9);

        var decision = planner.Plan(remaining: 9);

        Assert.True(decision.ShouldContinue);
    }

    [Theory]
    [InlineData(MessageDeleteStop.LimitReached, true)]
    [InlineData(MessageDeleteStop.ListEmpty, true)]
    [InlineData(MessageDeleteStop.TooManyFailures, false)]
    [InlineData(MessageDeleteStop.MaxRounds, false)]
    [InlineData(MessageDeleteStop.NoProgress, false)]
    public void 只有达上限和列表空算正常收尾(MessageDeleteStop stop, bool normal)
    {
        Assert.Equal(normal, MessageDeletePlanner.IsNormalStop(stop));
    }

    [Fact]
    public void 汇总里写出实际删除条数()
    {
        var planner = new MessageDeletePlanner(limit: 3);
        planner.RecordSuccess(5, 4);
        planner.RecordSuccess(4, 3);
        planner.RecordFailure("点不动");

        var summary = planner.Summarize(MessageDeleteStop.LimitReached);

        Assert.Contains("实际删除 2 条", summary, StringComparison.Ordinal);
        Assert.Contains("失败 1 条", summary, StringComparison.Ordinal);
    }

    // ── 编排（假页面） ────────────────────────────────────────────────────

    /// <summary>
    /// 删满上限就停，不多删一条。
    /// </summary>
    [Fact]
    public async Task 删满上限即停()
    {
        var page = InboxWith(5);
        page.OnClick = (selector, _) =>
        {
            if (selector == FacebookSelectors.DeleteConversationConfirm)
            {
                page.Counts[FacebookSelectors.ConversationRow]--;
            }
        };

        var result = await MessageDeleteFlow.RunAsync(page, Fast(limit: 2));

        Assert.True(result.Success);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(MessageDeleteStop.LimitReached, result.Stop);
        Assert.Equal(2, page.Clicked.Count(x => x == FacebookSelectors.DeleteConversationConfirm));
    }

    [Fact]
    public async Task 列表本来就空时一次都不点()
    {
        var page = InboxWith(0);

        var result = await MessageDeleteFlow.RunAsync(page, Fast(limit: 20));

        Assert.True(result.Success);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(MessageDeleteStop.ListEmpty, result.Stop);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 不限条数时删到列表空为止()
    {
        var page = InboxWith(3);
        page.OnClick = (selector, _) =>
        {
            if (selector == FacebookSelectors.DeleteConversationConfirm)
            {
                page.Counts[FacebookSelectors.ConversationRow]--;
            }
        };

        var result = await MessageDeleteFlow.RunAsync(page, Fast(limit: 0));

        Assert.True(result.Success);
        Assert.Equal(3, result.Deleted);
        Assert.Equal(MessageDeleteStop.ListEmpty, result.Stop);
    }

    /// <summary>
    /// 第一步就点不动时，连续尝试 3 次后中止，且判为失败。
    /// </summary>
    /// <remarks>
    /// 「判失败」这一条不能松：页面改版时若报成功，使用者看到「删除完成 0 条」会以为号里本来就没消息。
    /// </remarks>
    [Fact]
    public async Task 入口点不动时连续失败后中止并判失败()
    {
        var page = InboxWith(5);
        page.VisibleSelectors.Remove(FacebookSelectors.ConversationInfoButton);

        var result = await MessageDeleteFlow.RunAsync(page, Fast(limit: 20));

        Assert.False(result.Success);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(3, result.Failed);
        Assert.Equal(MessageDeleteStop.TooManyFailures, result.Stop);
        Assert.Contains(result.LogLines, x => x.Contains("继续下一条", StringComparison.Ordinal));
    }

    /// <summary>
    /// 一条失败不该让整批停 —— 下一条可能好好的（比如某个会话已被对方删除，点开是空）。
    /// </summary>
    [Fact]
    public async Task 失败一条后仍继续删下一条()
    {
        var page = InboxWith(3);
        var attempts = 0;
        page.OnClick = (selector, _) =>
        {
            if (selector != FacebookSelectors.DeleteConversationConfirm)
            {
                return;
            }

            attempts++;
            if (attempts == 1)
            {
                // 第一条「删了但列表没变」—— 模拟对方已删会话、点了没反应
                return;
            }

            page.Counts[FacebookSelectors.ConversationRow]--;
        };

        var result = await MessageDeleteFlow.RunAsync(page, Fast(limit: 2));

        Assert.True(result.Success);
        Assert.Equal(2, attempts);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(0, result.Failed);

        // 「点了但列表没变」虽然不算失败，但必须留痕，否则没人发现那条其实没删掉。
        Assert.Contains(result.LogLines, x => x.Contains("未下降", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 先导航到配置的收件箱地址()
    {
        var page = InboxWith(1);

        await MessageDeleteFlow.RunAsync(page, Fast(limit: 1, inboxUrl: "file:///mimic/messenger.html"));

        Assert.Equal(["file:///mimic/messenger.html"], page.NavigatedUrls);
    }

    /// <summary>
    /// **不限条数 + 页面点不动**：前几个出口全都不触发，只有轮次硬上限能兜住。
    /// </summary>
    /// <remarks>
    /// 这条是注入实验拽出来的真缺陷：把「已达上限」那个判断去掉之后，
    /// 这组用例里的「不限条数」场景会**无限循环**，测试进程直接挂死 ——
    /// 不是失败，是没有任何输出。现实里对应「上限配成 0（想清空）+ 页面点了没反应」，
    /// 结果是任务永远跑不完，而日志看起来每轮都在干活。
    /// </remarks>
    [Fact]
    public async Task 不限条数且列表数量不降时由轮次上限兜住()
    {
        // 不设 OnClick：点确认后列表永远不变，等价于「点了没生效」
        var page = InboxWith(3);

        var result = await MessageDeleteFlow.RunAsync(page, Fast(limit: 0, maxRounds: 5));

        Assert.False(result.Success);
        Assert.Equal(MessageDeleteStop.MaxRounds, result.Stop);
        Assert.Contains(result.LogLines, x => x.Contains("安全上限", StringComparison.Ordinal));
    }

    [Fact]
    public void 轮次上限按条数上限自动推算()
    {
        Assert.Equal(50, new MessageDeletePlanner(limit: 20).MaxRounds);
        Assert.Equal(500, new MessageDeletePlanner(limit: 0).MaxRounds);
    }

    [Fact]
    public void 轮次超过显式上限就停()
    {
        var planner = new MessageDeletePlanner(limit: 0, maxRounds: 3);

        Assert.True(planner.Plan(remaining: 9).ShouldContinue);
        Assert.True(planner.Plan(remaining: 9).ShouldContinue);
        Assert.True(planner.Plan(remaining: 9).ShouldContinue);

        var decision = planner.Plan(remaining: 9);

        Assert.False(decision.ShouldContinue);
        Assert.Equal(MessageDeleteStop.MaxRounds, decision.Stop);
    }

    [Fact]
    public async Task 取消令牌在导航前生效()
    {
        var page = InboxWith(5);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MessageDeleteFlow.RunAsync(page, Fast(limit: 20), cts.Token));
    }

    // ── 工具 ──────────────────────────────────────────────────────────────

    /// <summary>一个「有 n 条会话、四步都能点」的假收件箱。</summary>
    private static FakePageInterop InboxWith(int conversationCount)
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.ConversationRow] = conversationCount > 0;
        page.VisibleSelectors[FacebookSelectors.ConversationInfoButton] = true;
        page.VisibleSelectors[FacebookSelectors.DeleteConversationMenuItem] = true;
        page.VisibleSelectors[FacebookSelectors.DeleteConversationConfirm] = true;
        page.Counts[FacebookSelectors.ConversationRow] = conversationCount;
        return page;
    }

    /// <summary>
    /// 缩短所有超时的调参。真实默认值是 15/8/12 秒，一个失败用例要跑 45 秒，测试没法等。
    /// </summary>
    private static MessageDeleteOptions Fast(int limit, string? inboxUrl = null, int maxRounds = 0) => new(
        Limit: limit,
        ActionTimeout: TimeSpan.FromMilliseconds(80),
        ConfirmTimeout: TimeSpan.FromMilliseconds(80),
        RefreshTimeout: TimeSpan.FromMilliseconds(200),
        PollInterval: TimeSpan.FromMilliseconds(5),
        InboxUrl: inboxUrl ?? "file:///mimic/messenger.html",
        MaxRounds: maxRounds);
}
