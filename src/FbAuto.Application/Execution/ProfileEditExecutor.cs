using FbAuto.Automation.Media;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Profiles;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 资料修改（谷歌版）执行器：读签名文件 + 扫图片目录 → 交给流程 → 汇总。
/// </summary>
/// <remarks>
/// 五项：头像、背景图、发帖设置、个性签名、类别（顺序与原版日志块一致）。
/// 还剩两项单独做：<b>粉丝公开内容</b>（关键词屏蔽）与<b>主页名字</b>（iframe + 主账号密码二次确认）。
/// 不硬塞进来的理由：这两项的失败原因和前五项完全不同（一项是权限/审核，一项要跨 frame 与二次验证），
/// 混在一起会让「这一步算不算过」变得说不清。
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
        logger.Info($"按 {profile.DisplayName} 取配置：签名文件 {profile.SignatureFile}，图片目录 {profile.ImageDirectory}");

        var (signature, sourceLines) = SignatureSource.Read(profile.SignatureFile);
        foreach (var line in sourceLines)
        {
            logger.Info(line);
        }

        var images = BuildImageCandidates(context.Account.Alias, profile.ImageDirectory, logger);

        var request = new ProfileEditRequest(
            signature,
            profile.ProfileCategory,
            profile.UpdatePostingSettings,
            images,
            profile.CommentsBlockWords);

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

    /// <summary>
    /// 把图片目录变成「本账号那张图在最前」的候选列表。
    /// </summary>
    /// <remarks>
    /// 选图规则在 <see cref="AccountImageCandidates"/> 里（可单测，不碰页面也不碰浏览器）。
    /// 这里只负责把扫描结果与告警写进日志 —— 「配了目录但一张能用的都没有」必须让人看见，
    /// 否则使用者会以为头像换了，实际两项都被跳过。
    /// </remarks>
    private static IReadOnlyList<string> BuildImageCandidates(string alias, string directory, IAppLogger logger)
    {
        var scan = ImageDirectoryScanner.Scan(directory);
        if (!scan.IsUsable)
        {
            logger.Info(scan.Failure ?? "图片目录不可用，头像与背景图两项将跳过");
            return [];
        }

        logger.Info($"图片目录可用 {scan.Count} 张：{directory}");
        foreach (var skip in scan.Skipped)
        {
            logger.Warn($"跳过图片 {Path.GetFileName(skip.Path)}：{skip.Reason}");
        }

        var selection = AccountImageCandidates.Build(alias, scan);
        if (selection.IsEmpty)
        {
            logger.Warn($"账号 {alias} 没有分到图片，头像与背景图两项将跳过");
            return [];
        }

        // 「目录里只有一张图」这类情况只在同一批账号一起跑时才看得出来，
        // 而执行器是逐任务跑的 —— 所以计划自带的告警在这里必须照抄一遍。
        foreach (var warning in selection.Warnings)
        {
            logger.Warn(warning);
        }

        logger.Info(
            $"账号 {alias} 使用图片：{Path.GetFileName(selection.Primary!)}（备选 {selection.AlternateCount} 张）");
        return selection.Candidates;
    }
}
