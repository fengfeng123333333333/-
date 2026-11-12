using FbAuto.Automation.Media;
using FbAuto.Automation.Popups;

namespace FbAuto.Automation.Abstractions;

/// <summary>
/// 页面操作原语。这是纯逻辑层与真实浏览器之间**唯一**的接缝。
/// </summary>
/// <remarks>
/// 为什么单独抽一个接口：封面校验、弹窗关闭、草稿清理的决策逻辑都能在没有任何浏览器的情况下
/// 单测完；真正碰页面的只有这六个动作。等 facebook.com 出口通了，实现一个 Playwright 版本
/// （<c>FbAuto.Browser</c> 里的 <c>PlaywrightInterop</c>）就能把四件套整体接上，逻辑层一行不用改。
///
/// 刻意保持窄：只放这四件事真正用到的原语。宽接口会诱使业务逻辑往实现里渗。
/// </remarks>
public interface IPageInterop
{
    /// <summary>元素是否存在且可见。</summary>
    Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>元素是否处于可用状态（未 disabled）。元素不存在时返回 false。</summary>
    Task<bool> IsEnabledAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>点击元素。</summary>
    Task ClickAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>按 Escape。</summary>
    Task PressEscapeAsync(CancellationToken cancellationToken = default);

    /// <summary>给 file input 注入文件路径（不弹系统对话框）。</summary>
    Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default);

    /// <summary>匹配选择器的元素个数。用于「第 n 个继续按钮」这类按序号定位的场景。</summary>
    Task<int> CountAsync(string selector, CancellationToken cancellationToken = default);
}

/// <summary>
/// 什么都没接的占位实现：所有查询返回「不存在」，所有动作直接抛出。
/// </summary>
/// <remarks>
/// 用在「页面流程还没接上」的路径里。动作抛异常而不是静默返回，
/// 是为了避免「以为点了其实没点」—— 静默失败在自动化程序里最难查。
/// </remarks>
public sealed class NotConnectedPageInterop : IPageInterop
{
    public static NotConnectedPageInterop Instance { get; } = new();

    public Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> IsEnabledAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task ClickAsync(string selector, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能执行点击");

    public Task PressEscapeAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能按键");

    public Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能注入文件");

    public Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

/// <summary>
/// 把「规则表 + 页面」变成「观测结果 + 动作」的适配层。
/// </summary>
/// <remarks>
/// 这层刻意做成扩展方法而不是塞进 <see cref="IPageInterop"/>：
/// 决策逻辑不该因为换了个浏览器实现就重写一遍。
/// </remarks>
public static class PageInteropExtensions
{
    /// <summary>
    /// 按规则表逐条观测当前页面，产出决策器需要的输入。
    /// </summary>
    public static async Task<IReadOnlyList<PopupObservation>> ObservePopupsAsync(
        this IPageInterop page,
        IReadOnlyList<PopupDismissRule>? rules = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        rules ??= PopupDismissCatalog.Default;

        var observations = new List<PopupObservation>(rules.Count);
        foreach (var rule in rules)
        {
            var visible = await page.IsVisibleAsync(rule.Selector, cancellationToken).ConfigureAwait(false);
            if (!visible)
            {
                observations.Add(new PopupObservation(rule.Id, Visible: false));
                continue;
            }

            var enabled = await page.IsEnabledAsync(rule.Selector, cancellationToken).ConfigureAwait(false);
            observations.Add(new PopupObservation(rule.Id, Visible: true, Enabled: enabled));
        }

        return observations;
    }

    /// <summary>
    /// 执行决策出来的一步，返回应当写进日志的行。
    /// </summary>
    public static async Task<string> ExecuteAsync(
        this IPageInterop page,
        PopupDismissStep step,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(step);

        switch (step.Action)
        {
            case PopupDismissAction.Click when !string.IsNullOrEmpty(step.Selector):
                await page.ClickAsync(step.Selector, cancellationToken).ConfigureAwait(false);
                return step.LogLine;

            case PopupDismissAction.PressEscape:
                await page.PressEscapeAsync(cancellationToken).ConfigureAwait(false);
                return step.LogLine;

            default:
                return step.LogLine;
        }
    }

    /// <summary>
    /// 封面是否被页面拒绝（弹出「请选择其他封面照片」）。
    /// </summary>
    public static Task<bool> IsCoverRejectedAsync(
        this IPageInterop page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.IsVisibleAsync(CoverRejectionSignals.RejectedDialogSelector, cancellationToken);
    }

    /// <summary>
    /// 关掉封面被拒的提示：优先点关闭按钮，点不到就按 Escape。返回日志行。
    /// </summary>
    public static async Task<string> DismissCoverRejectionAsync(
        this IPageInterop page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var hasClose = await page.IsVisibleAsync(CoverRejectionSignals.CloseButtonSelector, cancellationToken)
            .ConfigureAwait(false);
        if (hasClose)
        {
            await page.ClickAsync(CoverRejectionSignals.CloseButtonSelector, cancellationToken).ConfigureAwait(false);
            return CoverRejectionSignals.ClickedCloseLine;
        }

        await page.PressEscapeAsync(cancellationToken).ConfigureAwait(false);
        return CoverRejectionSignals.PressedEscapeLine;
    }
}
