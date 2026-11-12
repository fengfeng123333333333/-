using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;
using Microsoft.Playwright;

namespace FbAuto.Application.Execution;

/// <summary>
/// 探针执行器：打开指定地址、读标题、可选截图。不承载任何业务语义。
///
/// 存在的理由：S4 要证明「调度器 + 浏览器通道 + 页面对象」这条链路真的通，
/// 而业务页面（facebook.com）在本机不可达。用本地页面当靶子，链路问题与网络问题就能分开定位。
/// 集成测试与排障用它，正式任务不用。
/// </summary>
public sealed class PageProbeExecutor : ITaskExecutor
{
    private readonly string _url;

    public PageProbeExecutor(TaskKind kind, string url)
    {
        Kind = kind;
        _url = url ?? throw new ArgumentNullException(nameof(url));
    }

    public TaskKind Kind { get; }

    public async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            await context.Page.GotoAsync(_url, new PageGotoOptions
            {
                Timeout = (float)context.DefaultTimeout.TotalMilliseconds,
                WaitUntil = WaitUntilState.DOMContentLoaded,
            });

            var title = await context.Page.TitleAsync();
            context.Logger.Info($"探针页面已打开：{_url}，标题「{title}」");
            return TaskExecutionResult.Ok($"页面已打开，标题「{title}」");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await context.FailAsync($"打开页面失败：{ex.Message}");
        }
    }
}
