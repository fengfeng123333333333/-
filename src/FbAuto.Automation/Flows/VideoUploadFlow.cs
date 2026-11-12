using System.Diagnostics;
using System.Globalization;
using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Selectors;
using FbAuto.Automation.Uploads;

namespace FbAuto.Automation.Flows;

/// <summary>
/// 一次批量上传的输入。
/// </summary>
/// <param name="AssetId">业务主页的 asset_id，用来拼批量上传页地址。</param>
/// <param name="Videos">要上传的视频，按上传顺序。</param>
/// <param name="Description">整段描述，可以为空。</param>
/// <param name="Scheduled">是否定时发布。</param>
/// <param name="ScheduleDate">定时日期，<paramref name="Scheduled"/> 为 true 且为空时取今天。</param>
/// <param name="ScheduleHour">定时小时（0-23）。</param>
public sealed record VideoUploadRequest(
    string AssetId,
    IReadOnlyList<string> Videos,
    string Description,
    bool Scheduled = false,
    DateOnly? ScheduleDate = null,
    int ScheduleHour = 0);

/// <summary>一次批量上传的结局。</summary>
public enum VideoUploadStatus
{
    /// <summary>已经点过发布并看到完成提示。</summary>
    Published,

    /// <summary>上传阶段就判定不该发布（零成功）。</summary>
    Aborted,

    /// <summary>中途出错。</summary>
    Failed,
}

/// <summary>流程结果。</summary>
/// <param name="Status">结局。</param>
/// <param name="Summary">一句话结论，写进任务结果列。</param>
/// <param name="LogLines">按发生顺序排列的日志行。</param>
public sealed record VideoUploadResult(
    VideoUploadStatus Status,
    string Summary,
    IReadOnlyList<string> LogLines)
{
    public bool Published => Status == VideoUploadStatus.Published;
}

/// <summary>流程调参。</summary>
/// <param name="PollInterval">进度采样间隔。</param>
/// <param name="FormFillDelay">表单填完到开始监控之间的等待，复刻原版的 3 秒。</param>
/// <param name="InputWaitTimeout">点「添加视频」之后等 file input 出现的上限。</param>
/// <param name="SchedulePanelTimeout">点「定时」之后等日期输入框出现的上限。</param>
/// <param name="PublishTimeout">点发布之后等「完成」出现的上限。</param>
/// <param name="Monitor">进度监控阈值。</param>
/// <param name="ComposerUrlTemplate">
/// 批量上传页地址模板，<c>{0}</c> 会被替换成转义后的 asset_id。
/// 做成参数不是为了「灵活」，是因为**本地仿真页没法测**：域名写死时，集成测试
/// 只能去连真实站点，而这条链路恰好在没出口的机器上跑不起来。
/// 模板里不含 <c>{0}</c> 时原样使用，这样测试可以指向 <c>file://</c>。
/// </param>
/// <param name="OutcomeSettleInterval">观察窗口之后，两次数失败条目的间隔。</param>
/// <param name="OutcomeSettleRounds">最多数几次，连续两次相同即认为页面稳定。</param>
public sealed record VideoUploadOptions(
    TimeSpan? PollInterval = null,
    TimeSpan? FormFillDelay = null,
    TimeSpan? InputWaitTimeout = null,
    TimeSpan? SchedulePanelTimeout = null,
    TimeSpan? PublishTimeout = null,
    UploadMonitorOptions? Monitor = null,
    string? ComposerUrlTemplate = null,
    TimeSpan? OutcomeSettleInterval = null,
    int? OutcomeSettleRounds = null)
{
    public static VideoUploadOptions Default { get; } = new();

    /// <summary>证据里的地址：<c>//business.facebook.com/latest/bulk_upload_composer?asset_id=</c></summary>
    public const string DefaultComposerUrlTemplate =
        "https://business.facebook.com/latest/bulk_upload_composer?asset_id={0}";

    public TimeSpan EffectivePollInterval => PollInterval ?? TimeSpan.FromSeconds(1);

    public TimeSpan EffectiveFormFillDelay => FormFillDelay ?? TimeSpan.FromSeconds(3);

    public TimeSpan EffectiveInputWaitTimeout => InputWaitTimeout ?? TimeSpan.FromSeconds(10);

    public TimeSpan EffectiveSchedulePanelTimeout => SchedulePanelTimeout ?? TimeSpan.FromSeconds(5);

    public TimeSpan EffectivePublishTimeout => PublishTimeout ?? TimeSpan.FromSeconds(60);

    public string EffectiveComposerUrlTemplate => ComposerUrlTemplate ?? DefaultComposerUrlTemplate;

    public TimeSpan EffectiveOutcomeSettleInterval => OutcomeSettleInterval ?? TimeSpan.FromMilliseconds(200);

    public int EffectiveOutcomeSettleRounds => OutcomeSettleRounds ?? 3;
}

/// <summary>
/// 视频上传（谷歌版）：导航到批量上传页 → 注入整批视频 → 填描述 → 排期 → 盯进度 → 清理失败项 → 发布。
/// </summary>
/// <remarks>
/// 流程顺序是照着原版串还原的，几处必须说清：
/// <list type="number">
/// <item>
/// **一个账号一次传一批**，不是一条任务一个视频。原版进的是
/// <c>bulk_upload_composer?asset_id=</c>，把目录里所有 MP4 一次注入。
/// 所以「失败」的粒度是「这一批里哪些条目失败」，不是「这条任务成功/失败」。
/// </item>
/// <item>
/// **发布前必须先判批次**。零成功时点发布会发出一条没有视频的空帖，
/// 这比「任务失败」严重得多 —— 前者会消耗账号，后者只是重跑。
/// </item>
/// <item>
/// **失败条目数用「可见的移除按钮」估算**。原版在发布前删掉失败视频，
/// 靠的就是这些「移除」按钮；页面上不该出现这个按钮的成功条目也就不会被误删。
/// 但这条是**推断**，不是从串里直接读到的：如果真实页面给成功条目也配了「移除」，
/// 这里会把成功数算少。真机验收时必须先确认这一点（见 S7 文档的未完成项）。
/// </item>
/// <item>
/// **「完成」按钮点不到不算失败**。它只是收尾，发布成功与否由它是否出现决定，
/// 点它失败只记一条警告。把收尾动作当判据是自动化程序里很常见的误判来源。
/// </item>
/// </list>
/// </remarks>
public static class VideoUploadFlow
{
    /// <summary>版权审核的文案片段。原版串：<c>所有进度条均为版权审核，上传已完成</c></summary>
    public const string CopyrightReviewText = "版权审核";

    /// <summary>两处等待的轮询间隔。</summary>
    private static readonly TimeSpan WaitPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 跑一遍批量上传。
    /// </summary>
    public static async Task<VideoUploadResult> RunAsync(
        IPageInterop page,
        VideoUploadRequest request,
        VideoUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(request);

        options ??= VideoUploadOptions.Default;
        var lines = new List<string>();

        if (request.Videos.Count == 0)
        {
            return Fail("没有要上传的视频", lines);
        }

        if (string.IsNullOrWhiteSpace(request.AssetId))
        {
            return Fail("asset_id 为空，无法拼出上传页地址", lines);
        }

        // ── 1. 导航 ────────────────────────────────────────────────────────
        var url = string.Format(
            CultureInfo.InvariantCulture,
            options.EffectiveComposerUrlTemplate,
            Uri.EscapeDataString(request.AssetId));

        lines.Add($"跳转到批量上传页面: {url}");
        try
        {
            await page.NavigateAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (PageActionException ex)
        {
            return Fail($"跳转上传页面失败：{ex.Message}", lines);
        }

        // ── 2. 找上传入口 ──────────────────────────────────────────────────
        // file input 天生 display:none，所以判存在而不是判可见。
        if (!await page.ExistsAsync(FacebookSelectors.VideoFileInput, cancellationToken).ConfigureAwait(false))
        {
            if (!await page.IsVisibleAsync(FacebookSelectors.AddVideoButton, cancellationToken).ConfigureAwait(false))
            {
                return Fail("没有找到添加视频按钮，跳过该账号", lines);
            }

            await page.ClickAsync(FacebookSelectors.AddVideoButton, 0, cancellationToken).ConfigureAwait(false);
            lines.Add("已点击添加视频按钮");

            var appeared = await WaitForExistsAsync(
                page,
                FacebookSelectors.VideoFileInput,
                options.EffectiveInputWaitTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!appeared)
            {
                return Fail("点击添加视频后仍未出现文件输入框，跳过该账号", lines);
            }
        }

        // ── 3. 注入整批视频 ────────────────────────────────────────────────
        lines.Add("开始注入视频文件...");
        try
        {
            await page.SetFileInputsAsync(
                FacebookSelectors.VideoFileInput,
                request.Videos,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PageActionException ex)
        {
            return Fail($"文件注入失败：{ex.Message}", lines);
        }

        lines.Add($"已注入 {request.Videos.Count} 个视频");

        // ── 4. 描述 ────────────────────────────────────────────────────────
        // 原版即使不填描述也会找这个框，找不到就跳过账号。这里保持同样的判据：
        // 描述框不在，说明页面结构变了，继续往下填排期大概率也会点错地方。
        if (!await page.IsVisibleAsync(FacebookSelectors.DescriptionInput, cancellationToken).ConfigureAwait(false))
        {
            return Fail("没有找到描述输入框，跳过该账号", lines);
        }

        if (string.IsNullOrEmpty(request.Description))
        {
            lines.Add("描述为空，跳过填写");
        }
        else
        {
            await page.FillAsync(
                FacebookSelectors.DescriptionInput,
                request.Description,
                0,
                cancellationToken).ConfigureAwait(false);
            lines.Add("描述已填写");
        }

        // ── 5. 排期 ────────────────────────────────────────────────────────
        if (request.Scheduled)
        {
            if (!await page.IsVisibleAsync(FacebookSelectors.ScheduleOptionButton, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Fail("没有找到定时发布选项，本批不上传", lines);
            }

            await page.ClickAsync(FacebookSelectors.ScheduleOptionButton, 0, cancellationToken)
                .ConfigureAwait(false);

            var panel = await WaitForVisibleAsync(
                page,
                FacebookSelectors.ScheduleDateInput,
                options.EffectiveSchedulePanelTimeout,
                cancellationToken).ConfigureAwait(false);

            if (!panel)
            {
                return Fail("定时发布面板未出现，本批不上传", lines);
            }

            var date = request.ScheduleDate ?? DateOnly.FromDateTime(DateTime.Today);
            await page.FillAsync(
                FacebookSelectors.ScheduleDateInput,
                date.ToString("yyyy-M-d", CultureInfo.InvariantCulture),
                0,
                cancellationToken).ConfigureAwait(false);

            await page.FillAsync(
                FacebookSelectors.ScheduleHourInput,
                request.ScheduleHour.ToString(CultureInfo.InvariantCulture),
                0,
                cancellationToken).ConfigureAwait(false);

            lines.Add($"已设置定时发布：{date:yyyy-M-d} {request.ScheduleHour} 时");
        }
        else
        {
            if (await page.IsVisibleAsync(FacebookSelectors.ScheduleImmediateButton, cancellationToken)
                    .ConfigureAwait(false))
            {
                await page.ClickAsync(FacebookSelectors.ScheduleImmediateButton, 0, cancellationToken)
                    .ConfigureAwait(false);
                lines.Add("已选择立即发布");
            }
            else
            {
                lines.Add("未发现排期选项，按页面默认处理");
            }
        }

        // ── 6. 盯进度 ──────────────────────────────────────────────────────
        var delay = options.EffectiveFormFillDelay;
        if (delay > TimeSpan.Zero)
        {
            lines.Add($"表单填写完成，等待 {delay.TotalSeconds:0} 秒后开始监控上传进度...");
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        var progress = await WatchProgressAsync(page, options, lines, cancellationToken).ConfigureAwait(false);
        if (!progress.Completed)
        {
            return Fail(progress.Message, lines);
        }

        // ── 7. 判批次 ──────────────────────────────────────────────────────
        var failedCount = await CountFailedStableAsync(page, options, cancellationToken).ConfigureAwait(false);
        failedCount = Math.Min(failedCount, request.Videos.Count);

        var successCount = request.Videos.Count - failedCount;
        var batch = UploadBatchJudge.Judge(successCount, failedCount);
        lines.Add(batch.LogLine);

        if (!batch.CanPublish)
        {
            return new VideoUploadResult(VideoUploadStatus.Aborted, batch.LogLine, lines);
        }

        if (batch.NeedsCleanup)
        {
            lines.Add($"开始清理 {failedCount} 个失败条目（倒序移除）");
            var cleanup = await FailedDraftFlow.RunAsync(
                page,
                Enumerable.Range(1, failedCount),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            lines.AddRange(cleanup.LogLines);
        }

        // ── 8. 发布 ────────────────────────────────────────────────────────
        if (!await page.IsVisibleAsync(FacebookSelectors.PublishButton, cancellationToken).ConfigureAwait(false))
        {
            return Fail("没有找到发布按钮", lines);
        }

        await page.ClickAsync(FacebookSelectors.PublishButton, 0, cancellationToken).ConfigureAwait(false);
        lines.Add("已点击发布");

        var published = await WaitForVisibleAsync(
            page,
            FacebookSelectors.DoneButton,
            options.EffectivePublishTimeout,
            cancellationToken).ConfigureAwait(false);

        if (!published)
        {
            return Fail("发布失败：没有等到发布完成的提示", lines);
        }

        lines.Add("发布成功");

        // 收尾按钮点不到不影响结论：发布是否成功由「完成」出现与否决定。
        try
        {
            await page.ClickAsync(FacebookSelectors.DoneButton, 0, cancellationToken).ConfigureAwait(false);
            lines.Add("已点击完成");
        }
        catch (PageActionException ex)
        {
            lines.Add($"点击完成失败（不影响发布结果）：{ex.Message}");
        }

        return new VideoUploadResult(
            VideoUploadStatus.Published,
            $"上传并发布成功（{successCount} 个视频）",
            lines);
    }

    /// <summary>
    /// 数失败条目，连续两次结果一致才采信。
    /// </summary>
    /// <remarks>
    /// 为什么不能数一次就下结论：进度条消失和失败条目渲染完之间有几帧时间差。
    /// 数早了会得到 0，于是判成「全部成功」→ 跳过清理 → 把失败条目一起发布出去。
    /// 这个缺陷在集成测试里以「偶发失败」的形式暴露过一次 —— 偶发是最难查的，
    /// 所以这里刻意等到稳定，而不是加一个固定 sleep（固定 sleep 只是把概率调低，
    /// 机器一忙还会犯）。
    /// </remarks>
    private static async Task<int> CountFailedStableAsync(
        IPageInterop page,
        VideoUploadOptions options,
        CancellationToken cancellationToken)
    {
        var previous = await page.CountAsync(FacebookSelectors.RemoveButton, cancellationToken)
            .ConfigureAwait(false);

        for (var round = 0; round < options.EffectiveOutcomeSettleRounds; round++)
        {
            await Task.Delay(options.EffectiveOutcomeSettleInterval, cancellationToken).ConfigureAwait(false);

            var current = await page.CountAsync(FacebookSelectors.RemoveButton, cancellationToken)
                .ConfigureAwait(false);

            if (current == previous)
            {
                return current;
            }

            previous = current;
        }

        return previous;
    }

    /// <summary>进度监控的收口结果。</summary>
    private readonly record struct ProgressOutcome(bool Completed, string Message);

    private static async Task<ProgressOutcome> WatchProgressAsync(
        IPageInterop page,
        VideoUploadOptions options,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        var monitor = new UploadProgressMonitor(options.Monitor);
        var stopwatch = Stopwatch.StartNew();
        var lastLine = string.Empty;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var barCount = await page.CountAsync(FacebookSelectors.UploadProgressBar, cancellationToken)
                .ConfigureAwait(false);
            var raw = await page.ReadAttributesAsync(
                FacebookSelectors.UploadProgressBar,
                "aria-valuenow",
                cancellationToken).ConfigureAwait(false);
            var texts = await page.ReadTextsAsync(FacebookSelectors.UploadProgressBar, cancellationToken)
                .ConfigureAwait(false);

            var copyrightOnly = texts.Count > 0
                && texts.All(t => t.Contains(CopyrightReviewText, StringComparison.Ordinal));

            // 只在进度条全没了的时候才去数失败条目：
            // 有进度条在跑说明批次还没结束，这个数字既没用又白花一次查询。
            var observedFailed = barCount == 0
                ? await page.CountAsync(FacebookSelectors.RemoveButton, cancellationToken).ConfigureAwait(false)
                : 0;

            var verdict = monitor.Observe(new UploadSample(
                barCount,
                ParseProgress(raw),
                copyrightOnly,
                stopwatch.Elapsed,
                observedFailed));

            // 只在文字变化时记一行：轮询一秒一轮，每轮都记会把任务日志刷满，
            // 真正需要看的「停在 45%」反而被埋掉。
            if (!string.Equals(verdict.LogLine, lastLine, StringComparison.Ordinal))
            {
                lastLine = verdict.LogLine;
                lines.Add(verdict.LogLine);
            }

            switch (verdict.Tick)
            {
                case UploadTick.Completed:
                    return new ProgressOutcome(true, verdict.LogLine);

                case UploadTick.Stalled:
                    return new ProgressOutcome(false, $"上传卡住：{verdict.LogLine}");

                default:
                    await Task.Delay(options.EffectivePollInterval, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>
    /// 把 <c>aria-valuenow</c> 的字符串解析成百分比。解析不了的跳过，并夹在 0-100。
    /// </summary>
    /// <remarks>
    /// 不把解析失败当错误：真实页面在审核阶段的 <c>aria-valuenow</c> 可能是空串或不存在，
    /// 这时该由「版权审核」那条规则接管，而不是让整个流程抛异常。
    /// </remarks>
    private static int[] ParseProgress(IReadOnlyList<string> raw)
    {
        var values = new List<int>(raw.Count);
        foreach (var text in raw)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(Math.Clamp(value, 0, 100));
            }
        }

        return [.. values];
    }

    private static async Task<bool> WaitForVisibleAsync(
        IPageInterop page,
        string selector,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await page.IsVisibleAsync(selector, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(WaitPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitForExistsAsync(
        IPageInterop page,
        string selector,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await page.ExistsAsync(selector, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(WaitPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    private static VideoUploadResult Fail(string message, List<string> lines)
    {
        lines.Add(message);
        return new VideoUploadResult(VideoUploadStatus.Failed, message, lines);
    }
}
