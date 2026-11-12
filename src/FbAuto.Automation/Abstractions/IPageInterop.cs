using FbAuto.Automation.Media;
using FbAuto.Automation.Popups;

namespace FbAuto.Automation.Abstractions;

/// <summary>
/// 页面操作原语。这是纯逻辑层与真实浏览器之间**唯一**的接缝。
/// </summary>
/// <remarks>
/// 为什么单独抽一个接口：封面校验、弹窗关闭、草稿清理的决策逻辑都能在没有任何浏览器的情况下
/// 单测完；真正碰页面的只有这几个动作。实现见
/// <c>FbAuto.Automation/Pages/PlaywrightPageInterop.cs</c>
/// （放在 Automation 层而不是 Browser 层，因为依赖方向是 Automation → Browser，反向引用会成环）。
///
/// 刻意保持窄：只放这几件事真正用到的原语。宽接口会诱使业务逻辑往实现里渗。
///
/// **关于序号**：草稿清理与二级确认天生是按位置的（原版串：`第 {n} 个继续按钮`），
/// 所以 <see cref="ClickAsync"/> 与 <see cref="IsEnabledAsync"/> 都带 0 起的 <c>index</c>。
/// 序号一律按**可见匹配**的顺序数：隐藏元素（关掉的对话框里的残留按钮）不占位，
/// 否则「第 2 个继续按钮」会点到隐藏的那个，Playwright 会一直等到超时。
/// </remarks>
public interface IPageInterop
{
    /// <summary>元素是否存在且可见。</summary>
    Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// 第 <paramref name="index"/> 个可见匹配是否处于可用状态。
    /// 元素不存在或不可见时返回 false。
    /// </summary>
    Task<bool> IsEnabledAsync(string selector, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>点击第 <paramref name="index"/> 个可见匹配（0 起）。</summary>
    Task ClickAsync(string selector, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>按 Escape。</summary>
    Task PressEscapeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 给 file input 注入文件路径（不弹系统对话框）。
    /// </summary>
    /// <remarks>
    /// 必须能对**隐藏**的 input 生效 —— 真实页面的 file input 基本都是 <c>display:none</c>，
    /// 先等它可见是等不到的。
    /// </remarks>
    Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default);

    /// <summary>可见匹配的个数（隐藏元素不计）。用于「第 n 个继续按钮」这类按序号定位的场景。</summary>
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

    public Task<bool> IsEnabledAsync(string selector, int index = 0, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task ClickAsync(string selector, int index = 0, CancellationToken cancellationToken = default) =>
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

            var enabled = await page.IsEnabledAsync(rule.Selector, 0, cancellationToken).ConfigureAwait(false);
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
                await page.ClickAsync(step.Selector, 0, cancellationToken).ConfigureAwait(false);
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
            await page.ClickAsync(CoverRejectionSignals.CloseButtonSelector, 0, cancellationToken)
                .ConfigureAwait(false);
            return CoverRejectionSignals.ClickedCloseLine;
        }

        await page.PressEscapeAsync(cancellationToken).ConfigureAwait(false);
        return CoverRejectionSignals.PressedEscapeLine;
    }
}
