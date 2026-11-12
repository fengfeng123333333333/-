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
    private readonly TaskKind _kind;
    private readonly ProfileEditOptions? _options;

    /// <param name="config">全局配置。</param>
    /// <param name="kind">任务类型，决定读哪一套业务参数（谷歌版 / 比特版）。</param>
    /// <param name="options">调参覆盖，仅测试用。</param>
    public ProfileEditExecutor(
        AppConfig config,
        TaskKind kind = TaskKind.ProfileEditGoogle,
        ProfileEditOptions? options = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _kind = kind;
        _options = options;
    }

    public TaskKind Kind => _kind;

    /// <summary>本执行器实际使用的那套业务参数。公开出来给用例钉住「没读错通道」。</summary>
    public ChannelProfile Profile => _config.ForChannel(TaskTypeCatalog.ChannelOf(_kind));

    public async Task<TaskExecutionResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.Logger;

        var profile = Profile;
        logger.Info($"按 {profile.DisplayName} 取配置：签名文件 {profile.SignatureFile}");

        var (signature, sourceLines) = SignatureSource.Read(profile.SignatureFile);
        foreach (var line in sourceLines)
        {
            logger.Info(line);
        }

        var request = new ProfileEditRequest(
            signature,
            profile.ProfileCategory,
            profile.UpdatePostingSettings);

        var page = new PlaywrightPageInterop(context.Page);

        ProfileEditResult result;
        try
        {
            result = await ProfileEditFlow.RunAsync(
                page,
                request,
                options: _options,
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
