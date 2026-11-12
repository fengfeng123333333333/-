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
                var name = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Task.Kind}_{Task.AccountAlias}.png";
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
}
