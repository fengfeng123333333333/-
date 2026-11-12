namespace FbAuto.Automation.Pages;

/// <summary>
/// 页面动作失败。消息一律中文、带选择器与序号，因为这类错会被写进任务结果给用户看。
/// </summary>
/// <remarks>
/// 为什么不直接用 Playwright 的原生异常：它的消息是英文的，且丢掉了「第几个」这个关键信息。
/// 序号丢了以后，「点了但没点对」和「根本没点到」在日志里长得一模一样。
/// </remarks>
public sealed class PageActionException : Exception
{
    public PageActionException(string operation, string? selector, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        Selector = selector;
    }

    /// <summary>失败的动作名，进日志。</summary>
    public string Operation { get; }

    /// <summary>出错的选择器。</summary>
    public string? Selector { get; }

    public static PageActionException NotFound(string operation, string selector, int index, int seconds)
        => new(
            operation,
            selector,
            $"{operation}失败：找不到第 {index + 1} 个可见元素（{selector}），{seconds} 秒内没有出现。"
            + "页面可能改版、该按钮被隐藏，或登录态已失效。");

    public static PageActionException FileMissing(string operation, string filePath)
        => new(operation, null, $"{operation}失败：文件不存在（{filePath}）。请检查路径是否仍然有效。");

    public static PageActionException EngineFailed(string operation, string selector, Exception inner)
        => new(
            operation,
            selector,
            $"{operation}失败：选择器 {selector} 无法执行。原始信息：{inner.Message}",
            inner);
}

/// <summary>页面动作的调参。默认值面向「后台自动化」：宁可等，不要因为页面慢判失败。</summary>
/// <param name="ActionTimeout">
/// 单个动作（点击、注入文件）的最长等待。取 15 秒 —— Facebook 弹窗动画与首屏渲染都在这量级以内，
/// 再长就说明真出问题了，应该让任务失败并写日志。
/// </param>
/// <param name="PollInterval">探测可见性的轮询间隔。</param>
public sealed record PageInteropOptions(
    TimeSpan? ActionTimeout = null,
    TimeSpan? PollInterval = null)
{
    public static PageInteropOptions Default { get; } = new();

    public TimeSpan EffectiveActionTimeout => ActionTimeout ?? TimeSpan.FromSeconds(15);

    public TimeSpan EffectivePollInterval => PollInterval ?? TimeSpan.FromMilliseconds(100);
}
