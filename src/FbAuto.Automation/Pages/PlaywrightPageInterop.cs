using FbAuto.Automation.Abstractions;
using Microsoft.Playwright;

namespace FbAuto.Automation.Pages;

/// <summary>
/// <see cref="IPageInterop"/> 的 Playwright 实现。四件事（封面、弹窗、草稿、取图）
/// 真正碰页面的地方全在这里。
/// </summary>
/// <remarks>
/// 放在 Automation 层而不是 Browser 层：依赖方向是 Automation → Browser，
/// 反过来引用会成环（Browser 不认识 Automation）。
///
/// 四个刻意的实现选择，每条都有踩坑理由：
/// <list type="number">
/// <item>
/// **可见性探测不等待**。Playwright 的 <c>Locator.IsVisibleAsync()</c> 是即时求值，
/// 元素不存在直接返回 false 而不抛异常。决策器每轮要靠它做分支，一等待就会把「没有弹窗」
/// 拖成 15 秒一轮。
/// </item>
/// <item>
/// **可用性自己判，不依赖 Playwright 的 IsEnabled**。Facebook 的按钮是 <c>div[role='button']</c>，
/// 禁用态靠 <c>aria-disabled='true'</c> 表达。实测 Playwright 1.41 的 **click 可操作性**是认
/// <c>aria-disabled</c> 的（会一直等「可用」直到超时），但 <c>IsEnabledAsync</c> 面向表单控件。
/// 这里的判定直接读 <c>disabled</c> 属性与 <c>aria-disabled='true'</c>，
/// 目的是与 click 的可操作性**保持一致** —— 不一致的话决策器会挑一个点不动的按钮，
/// 每条任务白卡一个点击超时。
/// </item>
/// <item>
/// **隐藏的 file input 直接注入**。真实页面的 file input 基本都是 <c>display:none</c>
/// （靠外层按钮触发），先等可见是等不到的。<c>SetInputFilesAsync</c> 本身不需要可见。
/// </item>
/// <item>
/// **序号按可见匹配数**。草稿清理要「第 2 个继续按钮」，而页面里关闭的对话框会留下
/// 隐藏的同名按钮；若按全部匹配数序号，就会点到一个永远点不动的隐藏元素。
/// </item>
/// </list>
/// </remarks>
public sealed class PlaywrightPageInterop : IPageInterop
{
    /// <summary>
    /// 判定可用性的脚本。<paramref name="el"/> 是元素本身。
    /// </summary>
    /// <remarks>
    /// 只认两件事：原生 <c>disabled</c> 属性，与 ARIA 的 <c>aria-disabled='true'</c>。
    /// 不要扩展成「读 class 里有没有 disabled」之类 —— 那是在猜样式，误判会把「能点」判成「不能点」，
    /// 于是程序改用 Escape 兜底，反而关掉了不该关的对话框。
    /// </remarks>
    private const string EnabledExpression = """
        el => {
            if (el.hasAttribute('disabled')) {
                return false;
            }
            const aria = (el.getAttribute('aria-disabled') || '').toLowerCase();
            return aria !== 'true';
        }
        """;

    private readonly IPage _page;
    private readonly PageInteropOptions _options;

    public PlaywrightPageInterop(IPage page, PageInteropOptions? options = null)
    {
        _page = page ?? throw new ArgumentNullException(nameof(page));
        _options = options ?? PageInteropOptions.Default;
    }

    /// <summary>底层页面，供导航/截图这类不属于本接口的动作使用。</summary>
    public IPage Page => _page;

    public async Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _page.Locator(selector).First.IsVisibleAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("判断可见性", selector, ex);
        }
    }

    public async Task<bool> IsEnabledAsync(
        string selector,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // 探测不做等待：不存在就是「不可用」，等下去会让每个弹窗规则都拖一个超时。
            var locator = await FindVisibleAsync(selector, index, TimeSpan.Zero, cancellationToken)
                .ConfigureAwait(false);
            if (locator is null)
            {
                return false;
            }

            return await locator.EvaluateAsync<bool>(EnabledExpression).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("判断可用性", selector, ex);
        }
    }

    public async Task ClickAsync(
        string selector,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var timeout = _options.EffectiveActionTimeout;
        ILocator? locator;
        try
        {
            locator = await FindVisibleAsync(selector, index, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("点击", selector, ex);
        }

        if (locator is null)
        {
            throw PageActionException.NotFound("点击", selector, index, (int)timeout.TotalSeconds);
        }

        try
        {
            await locator.ClickAsync(new LocatorClickOptions
            {
                Timeout = (float)timeout.TotalMilliseconds,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            // 找到了节点但点不动（被遮挡、动画中、已从 DOM 移除、或被标为禁用）走这里。
            // 单独包一层是因为「找不到」和「找到了点不动」的处置不同，日志要能区分。
            throw WrapActionFailure("点击", selector, ex);
        }
    }

    public async Task PressEscapeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _page.Keyboard.PressAsync("Escape").ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("按 Escape", "Escape", ex);
        }
    }

    public async Task SetFileInputAsync(
        string selector,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        // 先自己判文件存在：Playwright 的报错是英文且埋在长串里，用户看不懂该改什么。
        if (!File.Exists(filePath))
        {
            throw PageActionException.FileMissing("注入文件", filePath);
        }

        var timeout = _options.EffectiveActionTimeout;
        try
        {
            await _page.Locator(selector).First.SetInputFilesAsync(
                filePath,
                new LocatorSetInputFilesOptions
                {
                    Timeout = (float)timeout.TotalMilliseconds,
                }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw WrapActionFailure("注入文件", selector, ex);
        }
    }

    public async Task<int> CountAsync(string selector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        try
        {
            return await CountVisibleAsync(selector, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("统计元素", selector, ex);
        }
    }

    /// <summary>
    /// 这个异常是不是「页面层面的失败」（而不是取消）。
    /// </summary>
    /// <remarks>
    /// **必须同时认 <see cref="TimeoutException"/>**：Playwright 的**动作超时**抛的是
    /// <see cref="System.TimeoutException"/>，不是 <see cref="PlaywrightException"/>。
    /// 只 catch 后者的话，点击超时会把一句英文的 <c>Timeout 30000ms exceeded</c> 原样冒到任务日志里 ——
    /// 这个坑是集成测试里「点禁用按钮」那条用例抓出来的。
    ///
    /// 取消（<see cref="OperationCanceledException"/>）不在此列：取消要原样往上抛，
    /// 上层据此区分「任务被停」和「页面出错」。
    /// </remarks>
    private static bool IsPageFailure(Exception ex) =>
        ex is PlaywrightException or TimeoutException;

    /// <summary>
    /// 动作失败转中文异常。超时单独给一句能定位原因的话。
    /// </summary>
    private PageActionException WrapActionFailure(string operation, string selector, Exception ex)
    {
        if (ex is TimeoutException)
        {
            return new PageActionException(
                operation,
                selector,
                $"{operation}超时：{selector} 在 {_options.EffectiveActionTimeout.TotalSeconds:0} 秒内没有变为可操作。"
                + "常见原因：元素被遮挡、动画未结束、被 aria-disabled 标为禁用，或页面已切走。",
                ex);
        }

        return PageActionException.EngineFailed(operation, selector, ex);
    }

    /// <summary>
    /// 找第 <paramref name="index"/> 个可见匹配。<paramref name="timeout"/> 为零表示只查一次。
    /// </summary>
    /// <returns>找不到返回 null（不抛异常，由调用方决定怎么报）。</returns>
    private async Task<ILocator?> FindVisibleAsync(
        string selector,
        int index,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var locator = _page.Locator(selector);
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var total = await locator.CountAsync().ConfigureAwait(false);
            var seen = 0;
            for (var i = 0; i < total; i++)
            {
                var item = locator.Nth(i);
                if (!await item.IsVisibleAsync().ConfigureAwait(false))
                {
                    continue;
                }

                if (seen == index)
                {
                    return item;
                }

                seen++;
            }

            if (timeout <= TimeSpan.Zero || DateTime.UtcNow >= deadline)
            {
                return null;
            }

            // 页面可能正在渲染：等一小会儿再重数。这是「等元素出现」，不是「重试动作」——
            // 动作重试会把「点了一次但没反应」变成「点了两次」。
            await Task.Delay(_options.EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> CountVisibleAsync(string selector, CancellationToken cancellationToken)
    {
        var locator = _page.Locator(selector);
        var total = await locator.CountAsync().ConfigureAwait(false);
        var visible = 0;
        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await locator.Nth(i).IsVisibleAsync().ConfigureAwait(false))
            {
                visible++;
            }
        }

        return visible;
    }
}
