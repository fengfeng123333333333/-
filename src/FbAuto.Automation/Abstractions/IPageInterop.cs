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
    /// <summary>
    /// 导航到指定 URL，等文档就绪（不等图片/脚本全部加载完）。
    /// </summary>
    /// <remarks>
    /// 等 <c>DOMContentLoaded</c> 而不是 <c>Load</c>：Facebook 的页面有大量长连接与懒加载资源，
    /// 等 Load 经常直接吃满一个导航超时，而 DOM 早已可操作。
    /// </remarks>
    Task NavigateAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>元素是否存在且可见。</summary>
    Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// 元素是否存在于 DOM 中（**不看可见性**）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="IsVisibleAsync"/> 分开是因为 file input 天生 <c>display:none</c>：
    /// 用可见性判断它，永远得到 false，于是「input 还没出现」和「input 在但看不见」
    /// 这两件完全不同的事会被混成一件。前者要等或点按钮，后者直接注入即可。
    /// </remarks>
    Task<bool> ExistsAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// 第 <paramref name="index"/> 个可见匹配是否处于可用状态。
    /// 元素不存在或不可见时返回 false。
    /// </summary>
    Task<bool> IsEnabledAsync(string selector, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>点击第 <paramref name="index"/> 个可见匹配（0 起）。</summary>
    Task ClickAsync(string selector, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把文本填进第 <paramref name="index"/> 个可见匹配（会先清空原有内容）。
    /// </summary>
    /// <remarks>
    /// 用 fill 而不是逐字符敲：Facebook 的描述框是 <c>contenteditable</c>，
    /// 逐字符输入在真实页面会被富文本逻辑吞掉部分字符（换行、表情尤其明显）。
    /// </remarks>
    Task FillAsync(string selector, string text, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读所有可见匹配的某个属性值，读不到属性的元素跳过。
    /// </summary>
    /// <remarks>
    /// 一次取回全部，而不是「先 Count 再逐个读」：进度监控每轮都要采样，
    /// 逐个读会让一轮的往返次数与进度条数量成正比，轮询间隔被网络拖长。
    /// </remarks>
    Task<IReadOnlyList<string>> ReadAttributesAsync(
        string selector,
        string attribute,
        CancellationToken cancellationToken = default);

    /// <summary>读所有可见匹配的可见文本。</summary>
    Task<IReadOnlyList<string>> ReadTextsAsync(string selector, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读整页 HTML。
    /// </summary>
    /// <remarks>
    /// 只给「从页面里挖一个 id」这类场景用（asset_id 的三种提取路径）。
    /// 不要拿它代替选择器 —— 整页正则判断可见性、是否可点，一律不可靠。
    /// </remarks>
    Task<string> ReadContentAsync(CancellationToken cancellationToken = default);

    /// <summary>按 Escape。</summary>
    Task PressEscapeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 先聚焦第 <paramref name="index"/> 个可见匹配，再按一个键（<c>Enter</c> / <c>Tab</c> 这类 Playwright 键名）。
    /// </summary>
    /// <remarks>
    /// 单独一个原语而不是复用 <see cref="FillAsync"/>：FB 的「屏蔽关键词」是一个输入框加标签列表，
    /// 每敲一个词都要按回车才会变成一个标签，只填文本不按回车，保存后一个词都不会生效 ——
    /// 而日志上「已输入 5 个屏蔽词」看起来一切正常。
    ///
    /// 也不复用 <see cref="PressEscapeAsync"/>：那个是页面级按键（不针对元素），
    /// 拿它去「给输入框回车」会打在一个没有焦点的页面上，什么都不会发生。
    /// </remarks>
    Task PressKeyAsync(
        string selector,
        string key,
        int index = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 给 file input 注入文件路径（不弹系统对话框）。
    /// </summary>
    /// <remarks>
    /// 必须能对**隐藏**的 input 生效 —— 真实页面的 file input 基本都是 <c>display:none</c>，
    /// 先等它可见是等不到的。
    /// </remarks>
    Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一次给 file input 注入多个文件（批量上传）。
    /// </summary>
    /// <remarks>
    /// 必须一次注入，不能循环调单文件版本：真实页面每注入一次就新建一张待上传卡片，
    /// 循环注入会在「上一张还没建好」时叠进去，表现为只上传了第一个视频。
    /// </remarks>
    Task SetFileInputsAsync(
        string selector,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default);

    /// <summary>可见匹配的个数（隐藏元素不计）。用于「第 n 个继续按钮」这类按序号定位的场景。</summary>
    Task<int> CountAsync(string selector, CancellationToken cancellationToken = default);

    // ── frame 内操作 ──────────────────────────────────────────────────────
    //
    // 为什么要单独一组：FB 的主页名称设置页把表单放在**内嵌 iframe** 里
    // （原版串：「未找到包含 cquick 的 frame」→「使用 frame 查找表单...」），
    // 上面所有原语都只作用于主 frame，对 iframe 里的元素一律「找不到」。
    //
    // 定位方式一律用「frame 的 URL 含某个片段」，而不是 frame 的序号或 name：
    // 序号随页面加载顺序变，name 在 FB 上基本为空。URL 片段（cquick）是原版自己用的口径。

    /// <summary>
    /// 当前页面里的所有 frame（含主 frame 本身），按 Playwright 给出的顺序。
    /// </summary>
    /// <remarks>
    /// 列入返回值而不是只提供「按片段找」一个方法，是因为原版日志会打印
    /// 「当前共有 N 个 frame」加每个 frame 的 URL —— 排查「frame 还没加载出来」
    /// 和「加载出来了但地址不对」这两种情况，只看一句「未找到」是分不出来的。
    /// </remarks>
    Task<IReadOnlyList<FrameRef>> GetFramesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// URL 含 <paramref name="frameUrlContains"/> 的那个 frame 里，元素是否存在于 DOM。
    /// </summary>
    /// <remarks>
    /// 与主 frame 的 <see cref="ExistsAsync"/> 同口径：**不看可见性**，并且会等到超时。
    /// 找不到 frame 本身也返回 false —— 对调用方来说「frame 没有」和「元素没有」
    /// 都是「这一步做不下去」，区别由它自己按 frame 清单另记一条日志。
    /// </remarks>
    Task<bool> FrameExistsAsync(
        string frameUrlContains,
        string selector,
        CancellationToken cancellationToken = default);

    /// <summary>在指定 frame 里填文本（会先清空原有内容）。</summary>
    Task FrameFillAsync(
        string frameUrlContains,
        string selector,
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>在指定 frame 里点第 <paramref name="index"/> 个可见匹配（0 起）。</summary>
    Task FrameClickAsync(
        string frameUrlContains,
        string selector,
        int index = 0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 一个 frame 的标识。原版日志就是按「序号 + URL」打印的，所以这两个字段足够。
/// </summary>
/// <param name="Index">序号（0 起），与 Playwright 的 <c>Page.Frames</c> 顺序一致。</param>
/// <param name="Url">frame 当前 URL。跨域 frame 也能读到，但可能滞后一瞬。</param>
public sealed record FrameRef(int Index, string Url)
{
    /// <summary>URL 是否含指定片段（忽略大小写）。</summary>
    public bool Matches(string fragment) =>
        !string.IsNullOrEmpty(fragment)
        && Url.Contains(fragment, StringComparison.OrdinalIgnoreCase);
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

    public Task NavigateAsync(string url, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能导航");

    public Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> ExistsAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<bool> IsEnabledAsync(string selector, int index = 0, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task ClickAsync(string selector, int index = 0, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能执行点击");

    public Task FillAsync(
        string selector,
        string text,
        int index = 0,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能填写文本");

    public Task<IReadOnlyList<string>> ReadAttributesAsync(
        string selector,
        string attribute,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<string>> ReadTextsAsync(
        string selector,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string> ReadContentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Empty);

    public Task PressEscapeAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能按键");

    public Task PressKeyAsync(
        string selector,
        string key,
        int index = 0,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能按键");

    public Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能注入文件");

    public Task SetFileInputsAsync(
        string selector,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能注入文件");

    public Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    public Task<IReadOnlyList<FrameRef>> GetFramesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FrameRef>>([]);

    public Task<bool> FrameExistsAsync(
        string frameUrlContains,
        string selector,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task FrameFillAsync(
        string frameUrlContains,
        string selector,
        string text,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能在 frame 内填写文本");

    public Task FrameClickAsync(
        string frameUrlContains,
        string selector,
        int index = 0,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("页面尚未接通，不能在 frame 内点击");
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
