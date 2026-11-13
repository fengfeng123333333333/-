using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Media;

namespace FbAuto.Automation.Flows;

/// <summary>换图流程的结果。</summary>
/// <param name="Status">终态（被接受 / 用尽 / 无候选）。</param>
/// <param name="AcceptedPath">被接受的那张图，未接受为 null。</param>
/// <param name="UploadAttempts">真正上传了几次（本地预检挡掉的不计）。</param>
/// <param name="LogLines">按发生顺序排列的日志行。</param>
/// <param name="Summary">失败时的中文总结。</param>
public sealed record CoverAttemptResult(
    CoverAttemptStatus Status,
    string? AcceptedPath,
    int UploadAttempts,
    IReadOnlyList<string> LogLines,
    string Summary)
{
    public bool Accepted => Status == CoverAttemptStatus.Accepted;
}

/// <summary>
/// 封面/头像换图流程：本地预校验 → 注入 → 等页面的拒绝提示 → 关闭提示 → 换下一张。
/// </summary>
/// <remarks>
/// 这是 S5 那套纯逻辑第一次真正接到页面上。三件事必须先说清：
/// <list type="number">
/// <item>
/// **本地预校验是增强项，不是复刻**。原版没有像素校验，全靠上传后等
/// <c>div[aria-label='请选择其他封面照片']</c> 弹出来；本地这层能省掉一次注定失败的上传。
/// 所以预检挡掉的图**不算一次上传尝试**（<see cref="CoverAttemptPlanner.SkipLocally"/> 的语义）。
/// </item>
/// <item>
/// **「等多久没弹提示就认为通过」是个经验值**。真机上传有网络与转码延迟，
/// 窗口太短会把「慢但会被拒」判成「通过」。这里默认 5 秒并做成参数，
/// 真机验收时必须按实际网络调，别把它当常数。
/// </item>
/// <item>
/// **头像不做尺寸预检**。头像被裁成方形，公共主页封面的 820×312 阈值对它无意义，
/// 传 <c>rule: null</c> 即跳过本地判定，只走页面提示这条路。
/// </item>
/// </list>
/// </remarks>
public static class CoverAttemptFlow
{
    /// <summary>等页面弹出拒绝提示的默认窗口。</summary>
    public static readonly TimeSpan DefaultRejectionWindow = TimeSpan.FromSeconds(5);

    /// <summary>等提示消失的上限（关掉之后要确认真的没了，否则下一张会被误判为「刚上传就被拒」）。</summary>
    public static readonly TimeSpan DefaultDismissTimeout = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(150);

    /// <summary>「检测到 / 请选择其他封面照片 / 提示，关闭提示...」用的是原版文案。</summary>
    public const string DetectedLinePrefix = "检测到";

    /// <summary>
    /// 换公共主页封面：**默认开启**本地尺寸预校验。
    /// </summary>
    /// <param name="page">页面原语。</param>
    /// <param name="candidates">候选图片路径，按尝试顺序。</param>
    /// <param name="fileInputSelector">目标 file input，默认用证据里的封面 input。</param>
    /// <param name="rule">尺寸阈值，默认 <see cref="CoverSizeRule.FacebookPageCover"/>。</param>
    /// <param name="maxAttempts">最多上传几次。</param>
    /// <param name="rejectionWindow">注入后等拒绝提示的窗口。</param>
    /// <param name="dismissTimeout">关闭提示后等它消失的上限。</param>
    public static Task<CoverAttemptResult> RunForCoverAsync(
        IPageInterop page,
        IEnumerable<string>? candidates,
        string fileInputSelector = Selectors.FacebookSelectors.CoverPhotoFileInput,
        CoverSizeRule? rule = null,
        int maxAttempts = 3,
        TimeSpan? rejectionWindow = null,
        TimeSpan? dismissTimeout = null,
        CancellationToken cancellationToken = default)
        => RunAsync(
            page,
            candidates,
            fileInputSelector,
            rule ?? CoverSizeRule.FacebookPageCover,
            maxAttempts,
            rejectionWindow,
            dismissTimeout,
            cancellationToken);

    /// <summary>
    /// 换头像：**不做**本地尺寸预校验。
    /// </summary>
    /// <remarks>
    /// 头像会被裁成方形，公共主页封面的 820×312 / 最小 400×150 阈值对它没有意义，
    /// 套上去只会把正常头像全部判为不合格。头像是否合格完全交给页面提示。
    ///
    /// **已知缺口**：原版提取串里只找到封面那个「请选择其他封面照片」对话框，
    /// 没找到头像被拒时页面弹的是什么。所以头像目前复用同一条判定信号 ——
    /// 若真机上头像被拒时弹的是别的文案，这条流程会把它当「通过」。真机验收必须确认这一点。
    /// </remarks>
    /// <param name="prepareAsync">
    /// 每一轮注入之前要做的事。头像必须传：它的 file input 只在「点头像区域 → 点选择头像」
    /// 打开的对话框里才有，被拒一次对话框就关了，不重开就注不进第二张。
    /// </param>
    public static Task<CoverAttemptResult> RunForAvatarAsync(
        IPageInterop page,
        IEnumerable<string>? candidates,
        string fileInputSelector = Selectors.FacebookSelectors.AvatarFileInput,
        int maxAttempts = 3,
        TimeSpan? rejectionWindow = null,
        TimeSpan? dismissTimeout = null,
        CancellationToken cancellationToken = default,
        Func<CancellationToken, Task>? prepareAsync = null)
        => RunAsync(
            page,
            candidates,
            fileInputSelector,
            rule: null,
            maxAttempts,
            rejectionWindow,
            dismissTimeout,
            cancellationToken,
            prepareAsync);

    /// <summary>
    /// 核心实现。<paramref name="rule"/> 为 null 表示跳过本地预校验。
    /// </summary>
    /// <remarks>
    /// 刻意不把这个方法做成 public 并给 <paramref name="rule"/> 一个 null 默认值：
    /// 那样调用方漏传一个参数，本地预校验就**静默失效**，而日志上看不出任何异常。
    /// 这正是第一次跑集测被抓住的问题（100×100 的图被当成封面直接上传了）。
    /// </remarks>
    private static async Task<CoverAttemptResult> RunAsync(
        IPageInterop page,
        IEnumerable<string>? candidates,
        string fileInputSelector,
        CoverSizeRule? rule,
        int maxAttempts,
        TimeSpan? rejectionWindow,
        TimeSpan? dismissTimeout,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? prepareAsync = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileInputSelector);

        var window = rejectionWindow ?? DefaultRejectionWindow;
        var dismiss = dismissTimeout ?? DefaultDismissTimeout;
        var planner = new CoverAttemptPlanner(candidates, maxAttempts);
        var lines = new List<string>();
        string? acceptedPath = null;

        if (planner.Status == CoverAttemptStatus.NoCandidate)
        {
            return new CoverAttemptResult(
                CoverAttemptStatus.NoCandidate,
                null,
                0,
                lines,
                planner.BuildFailureSummary());
        }

        while (planner.HasNext)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = planner.TakeNext();
            if (path is null)
            {
                break;
            }

            if (!TryPrecheck(path, rule, out var precheckReason))
            {
                planner.SkipLocally(path, precheckReason);
                lines.Add(planner.LogLines[^1]);
                continue;
            }

            // 上一张的提示框还没关干净就注新图，会立刻被判成「刚上传就被拒」，
            // 白白消耗一次尝试。宁可在这里停手。
            if (await page.IsCoverRejectedAsync(cancellationToken).ConfigureAwait(false))
            {
                lines.Add("上一张的拒绝提示仍未关闭，停止换图");
                break;
            }

            // 顺序不能反：先确认提示已关，再重开入口。
            // 反了的话，提示没关时重开菜单会点在一个被遮罩挡住的区域上，卡到点击超时。
            if (prepareAsync is not null)
            {
                await prepareAsync(cancellationToken).ConfigureAwait(false);
            }


            await page.SetFileInputAsync(fileInputSelector, path, cancellationToken).ConfigureAwait(false);
            lines.Add($"已注入图片: {Path.GetFileName(path)}");

            var rejected = await WaitForRejectionAsync(page, window, cancellationToken).ConfigureAwait(false);
            if (rejected)
            {
                lines.Add($"{DetectedLinePrefix} {CoverRejectionSignals.RejectedDialogName} 提示，关闭提示...");
                lines.Add(await page.DismissCoverRejectionAsync(cancellationToken).ConfigureAwait(false));
                planner.MarkRejected();
                lines.Add(planner.LogLines[^1]);

                await WaitUntilRejectionGoneAsync(page, dismiss, cancellationToken).ConfigureAwait(false);
                continue;
            }

            planner.MarkAccepted();
            acceptedPath = path;
            lines.Add($"图片已被接受: {Path.GetFileName(path)}");
            break;
        }

        return new CoverAttemptResult(
            planner.Status,
            acceptedPath,
            planner.UploadAttempts,
            lines,
            planner.Status == CoverAttemptStatus.Accepted ? "已换图成功" : planner.BuildFailureSummary());
    }

    /// <summary>
    /// 本地预校验。<paramref name="rule"/> 为 null 时直接放行（头像路径）。
    /// </summary>
    private static bool TryPrecheck(string path, CoverSizeRule? rule, out string reason)
    {
        reason = string.Empty;

        if (rule is null)
        {
            return true;
        }

        if (!File.Exists(path))
        {
            reason = "文件不存在";
            return false;
        }

        if (!ImageHeaderReader.TryReadFile(path, out var info, out var failure))
        {
            reason = failure;
            return false;
        }

        var check = CoverSizeChecker.Check(info, new FileInfo(path).Length, rule, failure);
        if (check.CanUpload)
        {
            return true;
        }

        reason = check.Message;
        return false;
    }

    private static async Task<bool> WaitForRejectionAsync(
        IPageInterop page,
        TimeSpan window,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + window;
        while (true)
        {
            if (await page.IsCoverRejectedAsync(cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WaitUntilRejectionGoneAsync(
        IPageInterop page,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (await page.IsCoverRejectedAsync(cancellationToken).ConfigureAwait(false))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
