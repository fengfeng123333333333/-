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

    public async Task NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _page.GotoAsync(url, new PageGotoOptions
            {
                // 等 DOM 可操作，不等资源全部加载完。
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)_options.EffectiveNavigationTimeout.TotalMilliseconds,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw WrapActionFailure("导航", url, ex);
        }
    }

    public async Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // **不能只看第一个匹配**。真页面上「关闭了但没销毁」的元素会留在 DOM 里，
            // 于是一组同名元素里排在最前面的是隐藏的那个 —— 用 <c>First</c> 判定会得到
            // 「不可见」，而后面明明有一个可见的同名按钮。表现是：点得到，却先报「没找到」，
            // 或者决策器认为弹窗没弹出来，转去做别的分支。
            // S8 的资料页里就踩到了：三个视图共用一个页面，每个视图都有一个 [aria-label='保存']，
            // 排在前面的是隐藏的那个。
            var locator = _page.Locator(selector);
            var count = await locator.CountAsync().ConfigureAwait(false);

            // 绝大多数选择器只会命中一个，先把最常见的情况走短路径。
            if (count == 1)
            {
                return await locator.First.IsVisibleAsync().ConfigureAwait(false);
            }

            for (var i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await locator.Nth(i).IsVisibleAsync().ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("判断可见性", selector, ex);
        }
    }

    public async Task<bool> ExistsAsync(string selector, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _page.Locator(selector).CountAsync().ConfigureAwait(false) > 0;
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("判断元素存在", selector, ex);
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
            var locator = await FindVisibleAsync(_page.Locator(selector), index, TimeSpan.Zero, cancellationToken)
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
            locator = await FindVisibleAsync(_page.Locator(selector), index, timeout, cancellationToken).ConfigureAwait(false);
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

    public async Task PressKeyAsync(
        string selector,
        string key,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        cancellationToken.ThrowIfCancellationRequested();

        var timeout = _options.EffectiveActionTimeout;
        ILocator? locator;
        try
        {
            locator = await FindVisibleAsync(_page.Locator(selector), index, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("按键", selector, ex);
        }

        if (locator is null)
        {
            throw PageActionException.NotFound("按键", selector, index, (int)timeout.TotalSeconds);
        }

        try
        {
            // PressAsync 自己会先聚焦目标元素再按键，所以不用单独 Focus 一次。
            // 这也正是不能拿页面级 Keyboard.PressAsync 顶替的原因：那个打在没有焦点的页面上。
            await locator.PressAsync(key, new LocatorPressOptions
            {
                Timeout = (float)timeout.TotalMilliseconds,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw WrapActionFailure($"按键 {key}", selector, ex);
        }
    }

    public async Task FillAsync(
        string selector,
        string text,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        cancellationToken.ThrowIfCancellationRequested();

        var timeout = _options.EffectiveActionTimeout;
        ILocator? locator;
        try
        {
            locator = await FindVisibleAsync(_page.Locator(selector), index, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("填写文本", selector, ex);
        }

        if (locator is null)
        {
            throw PageActionException.NotFound("填写文本", selector, index, (int)timeout.TotalSeconds);
        }

        try
        {
            await locator.FillAsync(text, new LocatorFillOptions
            {
                Timeout = (float)timeout.TotalMilliseconds,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw WrapActionFailure("填写文本", selector, ex);
        }
    }

    public async Task<IReadOnlyList<string>> ReadAttributesAsync(
        string selector,
        string attribute,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        cancellationToken.ThrowIfCancellationRequested();

        return await ReadVisibleAsync(
            selector,
            item => item.GetAttributeAsync(
                attribute,
                new LocatorGetAttributeOptions { Timeout = 0 }),
            "读取属性",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadTextsAsync(
        string selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        cancellationToken.ThrowIfCancellationRequested();

        return await ReadVisibleAsync(
            selector,
            async item => await item.InnerTextAsync(
                new LocatorInnerTextOptions { Timeout = 0 }).ConfigureAwait(false),
            "读取文本",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ReadContentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _page.ContentAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("读取页面内容", "*", ex);
        }
    }

    /// <summary>
    /// 逐个读可见匹配，跳过不可见与读不到值的。
    /// </summary>
    /// <remarks>
    /// 没有用 <c>EvaluateAllAsync</c> 一把梭：那样得在 JS 里重写一遍「可见」的定义，
    /// 而这里的可见性必须与 <see cref="IsVisibleAsync"/> 完全同口径 ——
    /// 口径一旦分叉，进度条数量会与实际能操作的节点数不符，卡死判定就会误判。
    /// 元素个数在个位数到十几个之间，多几次往返换来口径一致是值得的。
    /// </remarks>
    private async Task<IReadOnlyList<string>> ReadVisibleAsync(
        string selector,
        Func<ILocator, Task<string?>> read,
        string operation,
        CancellationToken cancellationToken)
    {
        var locator = _page.Locator(selector);
        int count;
        try
        {
            count = await locator.CountAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed(operation, selector, ex);
        }

        if (count == 0)
        {
            return [];
        }

        var values = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var item = locator.Nth(i);
                if (!await item.IsVisibleAsync().ConfigureAwait(false))
                {
                    continue;
                }

                var value = await read(item).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(value))
                {
                    values.Add(value);
                }
            }
            catch (TimeoutException)
            {
                // 「判断可见」和「真正读取」之间元素被移除了 —— 轮询进度条时这是常态
                // （读完这一轮的下一秒进度条就到 100 消失了）。跳过这一个即可，
                // 让整条任务失败是错的：其余元素明明还能正常读。
                // 读操作已设成不等待（Timeout = 0），所以这里不会白等 30 秒。
                continue;
            }
            catch (Exception ex) when (IsPageFailure(ex))
            {
                throw PageActionException.EngineFailed(operation, selector, ex);
            }
        }

        return values;
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

    public async Task SetFileInputsAsync(
        string selector,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(filePaths);
        cancellationToken.ThrowIfCancellationRequested();

        if (filePaths.Count == 0)
        {
            throw new ArgumentException("没有要注入的文件", nameof(filePaths));
        }

        // 先把不存在的文件挑出来一次性报全：逐个报会让用户改一个重跑一次。
        var missing = filePaths.Where(p => !File.Exists(p)).ToArray();
        if (missing.Length > 0)
        {
            throw PageActionException.FilesMissing("注入文件", missing);
        }

        var timeout = _options.EffectiveActionTimeout;
        try
        {
            await _page.Locator(selector).First.SetInputFilesAsync(
                filePaths,
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

    public async Task<IReadOnlyList<FrameRef>> GetFramesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<IFrame> frames;
        try
        {
            frames = _page.Frames;
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("读取 frame 列表", "frames", ex);
        }

        var result = new List<FrameRef>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 逐个 try：frame 可能在遍历途中被移除（导航或组件卸载），读它的 URL 会抛。
            // 一个 frame 掉了不该让整份清单读不出来 —— 清单本来就是用来排查问题的一手信息。
            string url;
            try
            {
                url = frames[i].Url ?? string.Empty;
            }
            catch (Exception ex) when (IsPageFailure(ex))
            {
                continue;
            }

            result.Add(new FrameRef(i, url));
        }

        return result;
    }

    public async Task<bool> FrameExistsAsync(
        string frameUrlContains,
        string selector,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameUrlContains);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        cancellationToken.ThrowIfCancellationRequested();

        return await WaitFrameElementAsync(frameUrlContains, selector, cancellationToken).ConfigureAwait(false)
            is not null;
    }

    public async Task FrameFillAsync(
        string frameUrlContains,
        string selector,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameUrlContains);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(text);

        var timeout = _options.EffectiveActionTimeout;
        IFrame? frame;
        try
        {
            frame = await WaitFrameElementAsync(frameUrlContains, selector, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("在 frame 内填写文本", selector, ex);
        }

        if (frame is null)
        {
            throw PageActionException.NotFound(
                "在 frame 内填写文本",
                selector,
                index: 0,
                (int)timeout.TotalSeconds);
        }

        try
        {
            await frame.Locator(selector).First.FillAsync(text, new LocatorFillOptions
            {
                Timeout = (float)timeout.TotalMilliseconds,
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw WrapActionFailure("在 frame 内填写文本", selector, ex);
        }
    }

    public async Task FrameClickAsync(
        string frameUrlContains,
        string selector,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(frameUrlContains);
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        var timeout = _options.EffectiveActionTimeout;
        IFrame? frame;
        try
        {
            frame = await WaitFrameElementAsync(frameUrlContains, selector, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("在 frame 内点击", selector, ex);
        }

        if (frame is null)
        {
            throw PageActionException.NotFound("在 frame 内点击", selector, index, (int)timeout.TotalSeconds);
        }

        ILocator? locator;
        try
        {
            // 与主 frame 同一套「按可见序号定位」的实现（见 FindVisibleAsync 的注释）。
            locator = await FindVisibleAsync(frame.Locator(selector), index, timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("在 frame 内点击", selector, ex);
        }

        if (locator is null)
        {
            throw PageActionException.NotFound("在 frame 内点击", selector, index, (int)timeout.TotalSeconds);
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
            throw WrapActionFailure("在 frame 内点击", selector, ex);
        }
    }

    /// <summary>
    /// 等「URL 含片段的 frame」里出现某个元素，返回那个 frame。
    /// </summary>
    /// <remarks>
    /// 两个条件一起等：frame 可能还没建出来（FB 的设置页是先出壳再挂 iframe），
    /// 元素也可能已建好但还没渲染进 DOM。分开等会让「frame 在、元素还没到」
    /// 这种情况白等一个超时。
    ///
    /// 找不到返回 null 而不抛异常：调用方要按原版日志分别记「未找到包含 cquick 的 frame」
    /// 与「未找到表单」，抛异常就把这两种情况压成一句了。
    /// </remarks>
    private async Task<IFrame?> WaitFrameElementAsync(
        string frameUrlContains,
        string selector,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _options.EffectiveActionTimeout;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = FindFrame(frameUrlContains);
                if (frame is not null
                    && await frame.Locator(selector).CountAsync().ConfigureAwait(false) > 0)
                {
                    return frame;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                await Task.Delay(_options.EffectivePollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (IsPageFailure(ex))
        {
            throw PageActionException.EngineFailed("等待 frame 内元素", selector, ex);
        }
    }

    /// <summary>按 URL 片段找 frame；找不到返回 null。</summary>
    private IFrame? FindFrame(string frameUrlContains)
    {
        foreach (var frame in _page.Frames)
        {
            string url;
            try
            {
                url = frame.Url ?? string.Empty;
            }
            catch (Exception ex) when (IsPageFailure(ex))
            {
                continue;
            }

            if (url.Contains(frameUrlContains, StringComparison.OrdinalIgnoreCase))
            {
                return frame;
            }
        }

        return null;
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
    /// <remarks>
    /// 参数是**已经解析好的 locator** 而不是选择器字符串：frame 内操作要复用同一套
    /// 「按可见序号定位」的口径，若各自写一份，主 frame 与 iframe 的序号语义迟早会分叉。
    /// </remarks>
    /// <returns>找不到返回 null（不抛异常，由调用方决定怎么报）。</returns>
    private async Task<ILocator?> FindVisibleAsync(
        ILocator locator,
        int index,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
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
