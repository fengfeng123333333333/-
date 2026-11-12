using FbAuto.Automation.Flows;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Uploads;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 视频上传（谷歌版）执行器：把配置、账号、页面串成一次批量上传。
/// </summary>
/// <remarks>
/// 这一层只做「取参数 → 调流程 → 转结果」，不写任何页面判断。
/// 页面怎么点、什么算卡住，全在 <see cref="VideoUploadFlow"/> 里 ——
/// 那些逻辑要能脱离浏览器单测，塞进执行器就测不了了。
///
/// 三步的顺序不能换：
/// <list type="number">
/// <item>先扫视频目录。目录空就直接失败，不必白启一次主页列表导航。</item>
/// <item>再取 asset_id。取不到就没有上传页地址，后面全都无从谈起。</item>
/// <item>最后才上传。上传失败要带截图和页面 URL，这两样由 <c>FailAsync</c> 负责。</item>
/// </list>
/// </remarks>
public sealed class VideoUploadExecutor : ITaskExecutor
{
    private readonly AppConfig _config;
    private readonly TaskKind _kind;
    private readonly VideoUploadOptions? _options;

    /// <param name="config">全局配置。</param>
    /// <param name="kind">任务类型，决定读哪一套业务参数（谷歌版 / 比特版）。</param>
    /// <param name="options">调参覆盖，仅测试用。</param>
    public VideoUploadExecutor(
        AppConfig config,
        TaskKind kind = TaskKind.VideoUploadGoogle,
        VideoUploadOptions? options = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _kind = kind;
        _options = options;
    }

    public TaskKind Kind => _kind;

    /// <summary>
    /// 本执行器实际使用的那套业务参数。
    /// </summary>
    /// <remarks>
    /// 公开出来是为了能被用例钉住：「比特版执行器读到了比特版目录」这件事，
    /// 只靠 `Kind` 正确是证明不了的 —— 两边都指向同一份配置时，任务照样能跑，
    /// 但会用错目录、传错视频，而且日志里看不出异常。
    /// </remarks>
    public ChannelProfile Profile => _config.ForChannel(TaskTypeCatalog.ChannelOf(_kind));

    public async Task<TaskExecutionResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.Logger;

        // ── 1. 批次清单 ────────────────────────────────────────────────────
        var profile = Profile;
        logger.Info($"按 {profile.DisplayName} 取配置：视频目录 {profile.VideoDirectory}");

        var plan = VideoBatchPlanner.Plan(profile.VideoDirectory, profile.DescriptionFile);
        foreach (var line in plan.LogLines)
        {
            logger.Info(line);
        }

        if (!plan.IsValid)
        {
            // 这里不截图：还没碰过浏览器页面，截出来只是空白页，反而误导。
            return TaskExecutionResult.Fail(plan.Failure!);
        }

        var page = new PlaywrightPageInterop(context.Page);

        // ── 2. asset_id ────────────────────────────────────────────────────
        AssetIdExtraction asset;
        try
        {
            asset = await AssetIdResolver.ResolveAsync(page, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PageActionException ex)
        {
            return await context.FailAsync($"打开主页列表失败：{ex.Message}").ConfigureAwait(false);
        }

        logger.Info(asset.LogLine);
        if (!asset.Found)
        {
            return await context.FailAsync(asset.LogLine).ConfigureAwait(false);
        }

        // ── 3. 上传 ────────────────────────────────────────────────────────
        var request = new VideoUploadRequest(
            asset.AssetId!,
            plan.Videos,
            plan.Description,
            Scheduled: _config.ScheduledStartHour > 0,
            ScheduleDate: _config.ScheduledDate is { } scheduled
                ? DateOnly.FromDateTime(scheduled)
                : null,
            ScheduleHour: _config.ScheduledStartHour);

        VideoUploadResult result;
        try
        {
            result = await VideoUploadFlow.RunAsync(page, request, _options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PageActionException ex)
        {
            // 流程内部包住了导航与注入，但填文本、点按钮仍可能抛。
            // 这里兜住是为了拿到「截图 + 当前 URL」，直接往上抛就只有一行堆栈。
            return await context.FailAsync($"页面动作失败：{ex.Message}").ConfigureAwait(false);
        }

        foreach (var line in result.LogLines)
        {
            logger.Info(line);
        }

        return result.Published
            ? TaskExecutionResult.Ok(result.Summary)
            : await context.FailAsync(result.Summary).ConfigureAwait(false);
    }
}
