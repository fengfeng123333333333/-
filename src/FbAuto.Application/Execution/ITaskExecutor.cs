using FbAuto.Browser.Abstractions;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;
using Microsoft.Playwright;

namespace FbAuto.Application.Execution;

/// <summary>
/// 一个任务类型的具体执行体。S4 只把链路打通，视频上传 / 资料修改 / 删消息的页面动作在后续步骤实现。
/// </summary>
public interface ITaskExecutor
{
    TaskKind Kind { get; }

    /// <summary>
    /// 执行一个任务。返回失败结果而不是抛异常是首选（异常里的堆栈对使用者没有意义）；
    /// 抛出来的异常也会被调度器兜住并转成失败，所以这里不必为了「不崩」而写空 try。
    /// </summary>
    Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// 执行一个任务所需的全部上下文。浏览器已经启动并接管好，页面是新的空白页。
/// </summary>
public sealed class TaskExecutionContext
{
    /// <summary>
    /// 截图单调序号。毫秒位并不能保证唯一——同一个任务在同一毫秒内连续失败两次时，
    /// 时间戳、类型、别名、Id 前 6 位全都相同，文件名必然撞车（快的机器上很容易复现）。
    /// </summary>
    private static long _screenshotSequence;

    public required AutomatedTask Task { get; init; }

    public required AccountEntry Account { get; init; }

    public required IBrowserChannel Channel { get; init; }

    public required BrowserSession Session { get; init; }

    /// <summary>本任务专属的新页面。任务结束由调度器关闭浏览器，执行器不要自己关。</summary>
    public required IPage Page { get; init; }

    public required IAppLogger Logger { get; init; }

    /// <summary>失败截图目录。为空则不截图。</summary>
    public string? ScreenshotDirectory { get; init; }

    /// <summary>元素等待与导航的默认超时。</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 失败收尾：截图 + 取当前 URL，并把截图路径写进结果。
    /// 截图本身失败不能让「失败」变成「崩溃」，所以内部吞异常并记日志。
    /// </summary>
    public async Task<TaskExecutionResult> FailAsync(string message)
    {
        string? url = null;
        string? shot = null;

        try
        {
            url = Page.Url;
        }
        catch
        {
            // 页面可能已经崩了，取不到 URL 不影响结论。
        }

        if (!string.IsNullOrWhiteSpace(ScreenshotDirectory))
        {
            try
            {
                Directory.CreateDirectory(ScreenshotDirectory);

                // 文件名必须唯一：原来只到秒，同一秒内两条任务失败（并发下很常见）会互相覆盖，
                // 事后只剩一份截图却显示三条任务都「有截图」。
                // 别名直接拼进文件名也不安全——它来自手工维护的 CSV，可能出现 \ / : 等非法字符，
                // 那种情况下截图会整个抛异常被吞掉，使用者只看到「没截图」。
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
                var sequence = Interlocked.Increment(ref _screenshotSequence);
                var name = $"{stamp}_{Task.Kind}_{SafeFileName(Task.AccountAlias)}_{Task.Id.ToString("N")[..6]}_{sequence:D5}.png";
                shot = Path.Combine(ScreenshotDirectory, name);
                await Page.ScreenshotAsync(new PageScreenshotOptions { Path = shot, FullPage = false });
            }
            catch (Exception ex)
            {
                shot = null;
                Logger.Warn($"失败截图没能保存：{ex.Message}（不影响失败判定）");
            }
        }

        Logger.Warn($"{Task.DisplayName} 失败：{message}｜页面 {url ?? "（取不到）"}");
        return TaskExecutionResult.Fail(message, url, shot);
    }

    /// <summary>把账号别名变成可放进文件名的片段。空别名给一个固定占位，不留双下划线。</summary>
    private static string SafeFileName(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return "unknown";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var buffer = alias.Trim().ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            if (Array.IndexOf(invalid, buffer[i]) >= 0)
            {
                buffer[i] = '_';
            }
        }

        return new string(buffer);
    }
}
