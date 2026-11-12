using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Messages;

/// <summary>
/// 删掉**当前列表里的第一条会话**。
/// </summary>
/// <remarks>
/// 一次只删一条，不做「全选后批量删」。两个理由：
/// <list type="bullet">
/// <item>全选删除点错一次就是整仓清空，而且删掉的对话在**对方那边也会消失**，没有回头路。</item>
/// <item>逐条删时每一步都能记日志，出问题能追到「第几条、卡在哪一步」。</item>
/// </list>
///
/// 四步序列：点开第一条会话 → 打开会话详情 → 点「删除对话」→ 二次确认。
/// 这四步的每条选择器都**没有原版证据**（见 <see cref="FacebookSelectors"/> 末尾的说明）。
/// 任何一步等不到就抛 <see cref="PageActionException"/>，由编排层记一次失败并继续，
/// 连续失败多了规划器会叫停 —— 不会因为页面改版而在一堆账号上乱点。
/// </remarks>
public static class ConversationDeleter
{
    /// <summary>
    /// 删一条。成功则返回，失败则抛 <see cref="PageActionException"/>。
    /// </summary>
    /// <param name="page">页面原语。</param>
    /// <param name="options">调参。</param>
    /// <param name="lines">日志行，按发生顺序追加。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task DeleteFirstAsync(
        IPageInterop page,
        MessageDeleteOptions options,
        ICollection<string> lines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lines);

        await WaitAndClickAsync(
            page,
            "打开会话",
            FacebookSelectors.ConversationRow,
            options.EffectiveActionTimeout,
            options,
            lines,
            cancellationToken).ConfigureAwait(false);

        await WaitAndClickAsync(
            page,
            "打开会话详情",
            FacebookSelectors.ConversationInfoButton,
            options.EffectiveActionTimeout,
            options,
            lines,
            cancellationToken).ConfigureAwait(false);

        await WaitAndClickAsync(
            page,
            "点击删除对话",
            FacebookSelectors.DeleteConversationMenuItem,
            options.EffectiveActionTimeout,
            options,
            lines,
            cancellationToken).ConfigureAwait(false);

        // 确认框是独立的超时：它属于「点完菜单之后弹出来的东西」，
        // 弹不出来说明菜单没点上，和页面首屏慢不是一回事，用同一个超时会把两种原因混成一句。
        // 确认框是独立的超时：它属于「点完菜单之后弹出来的东西」，
        // 弹不出来说明菜单没点上，和页面首屏慢不是一回事，用同一个超时会把两种原因混成一句。
        await WaitAndClickAsync(
            page,
            "确认删除",
            FacebookSelectors.DeleteConversationConfirm,
            options.EffectiveConfirmTimeout,
            options,
            lines,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task WaitAndClickAsync(
        IPageInterop page,
        string operation,
        string selector,
        TimeSpan timeout,
        MessageDeleteOptions options,
        ICollection<string> lines,
        CancellationToken cancellationToken)
    {
        var visible = await options.WaitVisibleAsync(page, selector, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (!visible)
        {
            throw PageActionException.NotFound(
                operation,
                selector,
                0,
                (int)Math.Round(timeout.TotalSeconds));
        }

        await page.ClickAsync(selector, 0, cancellationToken).ConfigureAwait(false);
        lines.Add($"{operation}：已点击");
    }
}
