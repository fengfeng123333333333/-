using FbAuto.Automation.Abstractions;

namespace FbAuto.Automation.Messages;

/// <summary>删除消息的调参。</summary>
/// <param name="Limit">每个账号最多删几条，0 表示不限。默认 20。</param>
/// <param name="ActionTimeout">等入口/菜单项出现的最长时间。</param>
/// <param name="ConfirmTimeout">等二次确认框出现的最长时间。</param>
/// <param name="RefreshTimeout">等会话列表刷新的最长时间。</param>
/// <param name="PollInterval">等待时的轮询间隔。</param>
/// <param name="InboxUrl">
/// 收件箱地址。默认 <see cref="Selectors.FacebookUrls.MessengerInbox"/>，
/// 测试用它指向本地仿真页 —— 本机打不开 facebook.com，地址写死的话
/// 「导航之后能不能操作」这段就只能在测试里被跳过，而它恰恰最容易出错。
/// </param>
/// <param name="MaxRounds">
/// 轮次硬上限，0 表示自动（上限 × 2 + 10；不限条数时 500）。见
/// <see cref="MessageDeletePlanner.MaxRounds"/> —— 它是「上限没生效 + 列表数量不降」时唯一的刹车。
/// </param>
/// <param name="EnableStallDetection">
/// 是否启用「删了但数量不降就停」的兜底判断。**默认关**：
/// Messenger 列表是虚拟滚动，删一条会补一条，行数本来就不变，
/// 开着会表现为「删两条就停」。详见 <see cref="MessageDeletePlanner.DefaultStallThreshold"/>。
/// </param>
public sealed record MessageDeleteOptions(
    int Limit = 20,
    TimeSpan? ActionTimeout = null,
    TimeSpan? ConfirmTimeout = null,
    TimeSpan? RefreshTimeout = null,
    TimeSpan? PollInterval = null,
    string? InboxUrl = null,
    bool EnableStallDetection = false,
    int MaxRounds = 0)
{
    public static MessageDeleteOptions Default { get; } = new();

    public TimeSpan EffectiveActionTimeout => ActionTimeout ?? TimeSpan.FromSeconds(15);

    public TimeSpan EffectiveConfirmTimeout => ConfirmTimeout ?? TimeSpan.FromSeconds(8);

    public TimeSpan EffectiveRefreshTimeout => RefreshTimeout ?? TimeSpan.FromSeconds(12);

    public TimeSpan EffectivePollInterval => PollInterval ?? TimeSpan.FromMilliseconds(150);

    /// <summary>收件箱地址。</summary>
    public string EffectiveInboxUrl =>
        string.IsNullOrWhiteSpace(InboxUrl) ? Selectors.FacebookUrls.MessengerInbox : InboxUrl;

    /// <summary>等元素出现。</summary>
    public async Task<bool> WaitVisibleAsync(
        IPageInterop page,
        string selector,
        CancellationToken cancellationToken)
        => await WaitVisibleAsync(page, selector, EffectiveActionTimeout, cancellationToken).ConfigureAwait(false);

    /// <summary>按指定超时等元素出现。</summary>
    public async Task<bool> WaitVisibleAsync(
        IPageInterop page,
        string selector,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await page.IsVisibleAsync(selector, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 数当前可见的会话行，**连续两次采样一致才采信**。
    /// </summary>
    /// <remarks>
    /// 删完一条之后列表要重排，立刻数会数到旧值或中间态。S7 踩过一次同类的坑
    /// （进度条消失与失败条目渲染之间隔几帧，立刻数会数到 0，于是把「有失败」当成「全成功」），
    /// 那次是「带着失败条目发布」，这次是「以为没进展就停」—— 前者错在动了不该动的，
    /// 后者错在该动没动，但都是同一个根因：拿单次采样当结论。
    ///
    /// 不用固定 sleep 的理由一样：快的时候白等，慢的时候等不够，而两种都不报错。
    /// </remarks>
    public async Task<int> CountStableAsync(
        IPageInterop page,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EffectiveRefreshTimeout;
        var last = await page.CountAsync(selector, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DateTime.UtcNow >= deadline)
            {
                // 超时也返回最后读数：让上层按「没变化」处理，比抛异常更接近事实 ——
                // 列表确实停在那儿了，只是没变成我们期待的样子。
                return last;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);

            var current = await page.CountAsync(selector, cancellationToken).ConfigureAwait(false);
            if (current == last)
            {
                return current;
            }

            last = current;
        }
    }

    /// <summary>
    /// 等会话行数降到 <paramref name="below"/> 以下；超时则返回最后读数。
    /// </summary>
    /// <remarks>
    /// 删完一条之后**必须**先等这个，不能立刻读数：页面还在重排，读到的是旧值，
    /// 于是日志里写着「剩余 8 条」而实际上已经 7 条，看上去像没删成。
    ///
    /// 超时返回读数而不抛异常，是因为「列表没变短」和「刷新慢」在自动化里无法区分，
    /// 抛异常会把「慢」判成「坏」。就让上层按实际读数继续，日志里能看出来。
    /// </remarks>
    public async Task<int> WaitCountBelowAsync(
        IPageInterop page,
        string selector,
        int below,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + EffectiveRefreshTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = await page.CountAsync(selector, cancellationToken).ConfigureAwait(false);
            if (current < below)
            {
                return current;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return current;
            }

            await Task.Delay(EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
