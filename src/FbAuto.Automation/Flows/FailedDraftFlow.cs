using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Drafts;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Flows;

/// <summary>失败草稿清理的结果。</summary>
/// <param name="Plan">探测后生成的倒序计划。</param>
/// <param name="LogLines">按发生顺序排列的日志行。</param>
/// <param name="ClickedCount">真正点了移除的次数。</param>
/// <param name="ConfirmedCount">走完二次确认的次数。</param>
public sealed record FailedDraftResult(
    DraftCleanupPlan Plan,
    IReadOnlyList<string> LogLines,
    int ClickedCount,
    int ConfirmedCount);

/// <summary>
/// 失败草稿清理流程：按位置探测 → 生成倒序计划 → 逐个移除 → 处理二次确认。
/// </summary>
/// <remarks>
/// 三处容易被忽略的地方：
/// <list type="number">
/// <item>
/// **必须倒序点**。移除一条后后面的条目整体前移一位；正序执行时「点完第 1 个再点第 2 个」
/// 实际点的已经是原来的第 3 个。这条由 <see cref="FailedDraftCleanupPlanner"/> 保证，
/// 本流程只是照着 <see cref="DraftCleanupPlan.ClickOrder"/> 的顺序执行。
/// </item>
/// <item>
/// **序号按可见匹配数**。关掉的对话框会留下隐藏的「移除」按钮，按全部匹配数取序号会点到隐藏元素，
/// 表现是「点击超时」，很容易误判成页面改版。所以探测和点击都用可见序号。
/// </item>
/// <item>
/// **二次确认可能不存在**。有些路径点「移除」直接生效，此时对话框不会出现。
/// 把「两种按钮都数不到」当作「已直接移除」，不能当作失败 —— 否则每条都会记一条假错误。
/// </item>
/// </list>
/// </remarks>
public static class FailedDraftFlow
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>点完之后等二级确认出现/消失的上限。</summary>
    public static readonly TimeSpan DefaultDialogTimeout = TimeSpan.FromSeconds(3);

    /// <summary>「未出现二次确认，按已移除处理」。</summary>
    public const string NoConfirmLine = "未出现二次确认，按已移除处理";

    /// <summary>
    /// 按位置清理失败草稿。
    /// </summary>
    /// <param name="page">页面原语。</param>
    /// <param name="positions">要清理的位置，1 起（页面上失败条目从 1 数）。</param>
    /// <param name="removeSelector">移除按钮选择器，默认用证据里的原文。</param>
    /// <param name="dialogTimeout">等二级确认的上限。</param>
    public static async Task<FailedDraftResult> RunAsync(
        IPageInterop page,
        IEnumerable<int> positions,
        string removeSelector = FacebookSelectors.RemoveButton,
        TimeSpan? dialogTimeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentException.ThrowIfNullOrWhiteSpace(removeSelector);

        var settle = dialogTimeout ?? DefaultDialogTimeout;
        var lines = new List<string>();

        var requested = positions.ToList();
        var total = await page.CountAsync(removeSelector, cancellationToken).ConfigureAwait(false);
        lines.Add($"页面上共找到 {total} 个可移除条目");

        var targets = new List<DraftTarget>(requested.Count);
        foreach (var position in requested)
        {
            if (position <= 0)
            {
                // 非法位置交给计划器出日志，不在这里吞掉。
                targets.Add(new DraftTarget(position, DraftPositionState.NotFound));
                continue;
            }

            if (position > total)
            {
                targets.Add(new DraftTarget(position, DraftPositionState.NotFound));
                continue;
            }

            var enabled = await page
                .IsEnabledAsync(removeSelector, position - 1, cancellationToken)
                .ConfigureAwait(false);

            targets.Add(new DraftTarget(
                position,
                enabled ? DraftPositionState.Removable : DraftPositionState.Disabled));
        }

        var plan = FailedDraftCleanupPlanner.Plan(targets);

        // 计划里已经按倒序排好，执行顺序照抄即可 —— 不要在这里再 OrderBy 一次。
        var clicked = 0;
        var confirmed = 0;

        foreach (var step in plan.Steps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!step.ShouldClick)
            {
                lines.Add(step.LogLine);
                continue;
            }

            await page.ClickAsync(removeSelector, step.Position - 1, cancellationToken).ConfigureAwait(false);
            clicked++;
            lines.Add(step.LogLine);

            var optionCount = await page.CountAsync(FacebookSelectors.RemoveVideoOption, cancellationToken)
                .ConfigureAwait(false);

            if (optionCount == 0)
            {
                lines.Add(NoConfirmLine);
                continue;
            }

            var continueCount = await page.CountAsync(FacebookSelectors.ContinueButton, cancellationToken)
                .ConfigureAwait(false);

            var decision = DraftRemovalConfirmPlanner.Decide(optionCount, continueCount);
            lines.Add(decision.LogLine);
            if (!decision.CanProceed)
            {
                continue;
            }

            await page.ClickAsync(FacebookSelectors.RemoveVideoOption, decision.OptionIndex, cancellationToken)
                .ConfigureAwait(false);
            await page.ClickAsync(FacebookSelectors.ContinueButton, decision.ConfirmIndex, cancellationToken)
                .ConfigureAwait(false);

            confirmed++;
            lines.Add($"已点击第 {decision.ConfirmIndex + 1} 个继续按钮");

            await WaitUntilGoneAsync(page, FacebookSelectors.RemovalConfirmDialog, settle, cancellationToken)
                .ConfigureAwait(false);
        }

        lines.Add(plan.Summary);
        return new FailedDraftResult(plan, lines, clicked, confirmed);
    }

    private static async Task WaitUntilGoneAsync(
        IPageInterop page,
        string selector,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (await page.IsVisibleAsync(selector, cancellationToken).ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
