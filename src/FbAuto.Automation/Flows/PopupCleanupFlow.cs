using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Popups;

namespace FbAuto.Automation.Flows;

/// <summary>一轮弹窗清理的结果。</summary>
/// <param name="Steps">实际执行的步骤（空操作不入账）。</param>
/// <param name="LogLines">按发生顺序排列的日志行。</param>
/// <param name="Summary">一轮总结。</param>
/// <param name="StillVisible">关完（或到上限）后是否还有弹窗没收掉。</param>
public sealed record PopupCleanupResult(
    IReadOnlyList<PopupDismissStep> Steps,
    IReadOnlyList<string> LogLines,
    string Summary,
    bool StillVisible)
{
    public bool DismissedAny => Steps.Count > 0;
}

/// <summary>
/// 「观测 → 决策 → 执行 → 确认消失」的弹窗清理循环。
/// </summary>
/// <remarks>
/// 决策器本身早就能单测，这一层的价值只有一句话：**每点完一个必须重新观测**。
/// 一次性排好所有步骤等于假设页面不变；而点掉一个弹窗后页面上原来的坐标、序号、可见集合全变了。
///
/// 另一个必须做的检查：点完之后要确认弹窗真的消失了。如果点了没反应（按钮被遮挡、
/// 点到了装饰层），不确认就会对同一个弹窗反复点，每轮再叠加一次点击超时，
/// 最后任务失败在一个与真正原因无关的地方。
/// </remarks>
public static class PopupCleanupFlow
{
    /// <summary>点完之后等弹窗消失的上限。</summary>
    public static readonly TimeSpan DefaultDismissTimeout = TimeSpan.FromSeconds(3);

    /// <summary>点击后轮询的间隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>关完之后弹窗还在时的日志（这句出现即说明点击没生效）。</summary>
    public const string StillVisibleLine = "弹窗在关闭后仍然存在，停止本轮（继续点同一个控件只会叠加失败）";

    /// <summary>
    /// 跑一轮弹窗清理。
    /// </summary>
    /// <param name="page">页面原语。</param>
    /// <param name="rules">规则表，null 用默认表。</param>
    /// <param name="maxSteps">单轮最多关几个，防止「关一个冒一个」死循环。</param>
    /// <param name="dismissTimeout">每个弹窗关闭后等它消失的上限。</param>
    public static async Task<PopupCleanupResult> RunAsync(
        IPageInterop page,
        IReadOnlyList<PopupDismissRule>? rules = null,
        int maxSteps = 3,
        TimeSpan? dismissTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        rules ??= PopupDismissCatalog.Default;

        var settle = dismissTimeout ?? DefaultDismissTimeout;
        var pass = new PopupDismissPass(maxSteps);
        var lines = new List<string>();

        while (pass.CanContinue)
        {
            var observations = await page.ObservePopupsAsync(rules, cancellationToken).ConfigureAwait(false);
            var step = PopupDismissPlanner.Decide(observations, rules);

            if (step.IsNoOp)
            {
                lines.Add(step.LogLine);
                break;
            }

            lines.Add(await page.ExecuteAsync(step, cancellationToken).ConfigureAwait(false));
            pass.Record(step);

            var watched = observations
                .Where(o => o.Visible)
                .Select(o => rules.FirstOrDefault(r => r.Id == o.RuleId)?.Selector)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .ToList();

            if (watched.Count == 0)
            {
                // 没在规则表里的弹窗：按 Escape 兜底后无从观测，只能交给下一轮重新看。
                continue;
            }

            if (!await WaitUntilGoneAsync(page, watched, settle, cancellationToken).ConfigureAwait(false))
            {
                lines.Add(StillVisibleLine);
                return new PopupCleanupResult(pass.Steps, lines, pass.BuildSummary(), StillVisible: true);
            }
        }

        var stillVisible = pass.ReachedLimit
            && !(await page.ObservePopupsAsync(rules, cancellationToken).ConfigureAwait(false))
                .All(o => !o.Visible);

        return new PopupCleanupResult(pass.Steps, lines, pass.BuildSummary(), stillVisible);
    }

    private static async Task<bool> WaitUntilGoneAsync(
        IPageInterop page,
        IReadOnlyList<string> selectors,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var anyVisible = false;
            foreach (var selector in selectors)
            {
                if (await page.IsVisibleAsync(selector, cancellationToken).ConfigureAwait(false))
                {
                    anyVisible = true;
                    break;
                }
            }

            if (!anyVisible)
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
