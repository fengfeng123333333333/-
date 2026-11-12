using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Messages;

/// <summary>删除消息的整体结果。</summary>
/// <param name="Success">是否正常收尾（达上限或列表空）。</param>
/// <param name="Summary">一句话汇总，写进任务结果列。</param>
/// <param name="Deleted">实际删掉的条数。</param>
/// <param name="Failed">失败条数。</param>
/// <param name="Stop">停下时的原因。</param>
/// <param name="LogLines">按发生顺序的日志行。</param>
public sealed record MessageDeleteResult(
    bool Success,
    string Summary,
    int Deleted,
    int Failed,
    MessageDeleteStop Stop,
    IReadOnlyList<string> LogLines);

/// <summary>
/// 删除消息（Messenger 会话）的编排：开收件箱 → 一轮一轮删 → 按规划器收尾。
/// </summary>
/// <remarks>
/// **这一条不是仿制出来的**，是按需求自建的。原版 DLL 里「删除消息」只有任务名和三句
/// 生命周期日志（任务添加 / 开始 / 完成），没有任何页面地址或选择器，所以无从仿。
/// 需求口径是：**Messenger 会话记录，每账号上限 N 条（默认 20），删到上限或列表空即停**。
///
/// 为什么逐条删而不是全选：删掉的对话在**对方那边也会消失**，不可恢复。
/// 逐条删的最坏情况是「多删了几条」，全选删的最坏情况是「整仓没了」。
///
/// 一条失败不中断（继续下一条），但连续失败到阈值就停 —— 页面改版时不该在一堆账号上乱点。
/// 这与资料修改的「单条失败继续」是同一套语义，区别在资料修改没有「连续失败」这个刹车，
/// 因为它最多就三项。
/// </remarks>
public static class MessageDeleteFlow
{
    /// <summary>
    /// 跑一遍删除消息。
    /// </summary>
    /// <param name="page">页面原语。</param>
    /// <param name="options">调参；为空用默认（上限 20 条）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<MessageDeleteResult> RunAsync(
        IPageInterop page,
        MessageDeleteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        options ??= MessageDeleteOptions.Default;
        var lines = new List<string>();
        var failed = 0;

        var planner = new MessageDeletePlanner(
            options.Limit,
            stallThreshold: options.EnableStallDetection ? MessageDeletePlanner.DefaultStallThreshold : 0,
            maxRounds: options.MaxRounds);

        var limitText = planner.Limit > 0 ? $"上限 {planner.Limit} 条" : "不限条数";
        lines.Add($"开始删除消息（{limitText}）");

        try
        {
            await page.NavigateAsync(options.EffectiveInboxUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PageActionException ex)
        {
            return Fail($"打开消息收件箱失败：{ex.Message}", lines, planner, failed);
        }

        lines.Add($"已打开消息收件箱：{options.EffectiveInboxUrl}");

        // 先探一次列表，让日志能区分「本来就没会话」和「页面没加载出来」。
        var appeared = await options.WaitVisibleAsync(page, FacebookSelectors.ConversationRow, cancellationToken)
            .ConfigureAwait(false);
        lines.Add(appeared
            ? "检测到会话列表"
            : $"{(int)options.EffectiveActionTimeout.TotalSeconds} 秒内没有出现会话行，按空列表处理");

        while (true)
        {
            var remaining = await options
                .CountStableAsync(page, FacebookSelectors.ConversationRow, cancellationToken)
                .ConfigureAwait(false);

            var decision = planner.Plan(remaining);
            lines.Add(decision.LogLine);

            if (!decision.ShouldContinue)
            {
                return new MessageDeleteResult(
                    MessageDeletePlanner.IsNormalStop(decision.Stop),
                    planner.Summarize(decision.Stop),
                    planner.Deleted,
                    failed,
                    decision.Stop,
                    lines);
            }

            var before = remaining;
            try
            {
                await ConversationDeleter.DeleteFirstAsync(page, options, lines, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is PageActionException or InvalidOperationException)
            {
                failed++;
                planner.RecordFailure(ex.Message);
                lines.Add($"删除失败(继续下一条): {ex.Message}");
                continue;
            }

            var after = await options
                .WaitCountBelowAsync(page, FacebookSelectors.ConversationRow, before, cancellationToken)
                .ConfigureAwait(false);

            planner.RecordSuccess(before, after);

            // 数量没降不当失败（虚拟滚动列表本来就不降），但必须让日志看得出异常。
            // 否则「点了但其实没删掉」会被记成成功，而这种事只有翻聊天记录才会发现。
            lines.Add(after < before
                ? $"第 {planner.Deleted} 条已删除，剩余 {after} 条"
                : $"第 {planner.Deleted} 条已执行删除，剩余 {after} 条（未下降：可能是虚拟滚动，也可能是没删掉）");
        }
    }

    private static MessageDeleteResult Fail(
        string message,
        List<string> lines,
        MessageDeletePlanner planner,
        int failed)
    {
        lines.Add(message);
        return new MessageDeleteResult(false, message, planner.Deleted, failed, MessageDeleteStop.Continue, lines);
    }
}
