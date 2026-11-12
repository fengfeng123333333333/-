using FbAuto.Automation.Pages;
using FbAuto.Automation.Profiles;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 资料修改（谷歌版）执行器：读签名文件 → 交给流程 → 汇总。
/// </summary>
/// <remarks>
/// 这一版只做三项：个性签名、类别、发帖设置。
/// 主页名称要走 iframe + 主账号密码二次确认，头像与背景图要复用 S5/S6 的换图流程，
/// 两者单独做 —— 硬塞进来会让这一步的验收标准变成「六项都过」，
/// 而其中两项的失败原因和前三项完全不同（一个是权限/审核，一个是图片尺寸）。
/// </remarks>
public sealed class ProfileEditExecutor : ITaskExecutor
{
    private readonly AppConfig _config;

    public ProfileEditExecutor(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public TaskKind Kind => TaskKind.ProfileEditGoogle;

    public async Task<TaskExecutionResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.Logger;

        var (signature, sourceLines) = SignatureSource.Read(_config.SignatureFile);
        foreach (var line in sourceLines)
        {
            logger.Info(line);
        }

        var request = new ProfileEditRequest(
            signature,
            _config.ProfileCategory,
            _config.UpdatePostingSettings);

        var page = new PlaywrightPageInterop(context.Page);

        ProfileEditResult result;
        try
        {
            result = await ProfileEditFlow.RunAsync(
                page,
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
