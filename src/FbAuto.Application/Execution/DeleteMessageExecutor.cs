using FbAuto.Automation.Messages;
using FbAuto.Automation.Pages;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 删除消息执行器：读上限与 Cookie 配置 → 交给流程 → 汇总。
/// </summary>
/// <remarks>
/// 这一层只做「取参数 → 调流程 → 转结果」，不写任何页面判断。
///
/// **登录态用的是浏览器已有的登录态**，不是原版那条 cookie 路线。
/// 原版这条任务的唯一专属参数就是 <c>DeleteMsgCookieRootPath</c>，说明它靠 cookie 起会话；
/// 我们沿用其他四个流程的同一套假设（浏览器配置目录已登录），cookie 目录只读出来写日志。
/// 这样做的代价是「配了 cookie 目录并不生效」，所以配了就必须在日志里说明 ——
/// 否则使用者会以为自己用的是 cookie。
/// </remarks>
public sealed class DeleteMessageExecutor : ITaskExecutor
{
    private readonly AppConfig _config;
    private readonly MessageDeleteOptions? _options;

    public DeleteMessageExecutor(AppConfig config, MessageDeleteOptions? options = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _options = options;
    }

    public TaskKind Kind => TaskKind.DeleteMessage;

    public async Task<TaskExecutionResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.Logger;

        // 上限**只认配置**，不被测试注入的 options 覆盖 —— 否则「配置里的上限」这条
        // 在真机上是否生效就没人验了。注入的 options 只用来改地址与超时。
        var options = (_options ?? MessageDeleteOptions.Default) with { Limit = _config.DeleteMessageLimit };

        var cookieRoot = _config.DeleteMessageCookieRootPath;
        if (!string.IsNullOrWhiteSpace(cookieRoot))
        {
            logger.Info(
                $"已配置删除消息 Cookie 目录：{cookieRoot}。当前版本仍沿用浏览器已登录态，"
                + "这个目录只记录、不注入。");
        }

        var page = new PlaywrightPageInterop(context.Page);

        MessageDeleteResult result;
        try
        {
            result = await MessageDeleteFlow.RunAsync(page, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PageActionException ex)
        {
            return await context.FailAsync($"页面动作失败：{ex.Message}").ConfigureAwait(false);
        }

        foreach (var line in result.LogLines)
        {
            logger.Info(line);
        }

        return result.Success
            ? TaskExecutionResult.Ok(result.Summary)
            : await context.FailAsync(result.Summary).ConfigureAwait(false);
    }
}
