namespace FbAuto.Automation.Media;

/// <summary>
/// 封面被页面拒绝时的识别信号与日志文案。
/// </summary>
/// <remarks>
/// 整段规则来自原版 DLL 的字符串顺序（提取见 docs/evidence/extracted，2026-11-12 复核）：
/// <code>
/// .webp / .jpg / .jpeg / .png
/// div[aria-label='请选择其他封面照片'][aria-modal='true'][role='dialog']
/// : 检测到 / 请选择其他封面照片 / 提示，关闭提示...
/// : 已点击关闭按钮
/// : 已按 Escape 关闭提示
/// : 检测封面尺寸提示时异常:
/// </code>
/// 也就是说原版 <c>CheckCoverSizeErrorAsync</c> 的做法是：注入图片后等这个对话框出现，
/// 出现即判定「尺寸不合格」，然后点关闭（找不到关闭按钮则按 Escape），再换下一张图重试。
/// 文案一字不改地沿用，真机对比日志时才能逐行对上。
/// </remarks>
public static class CoverRejectionSignals
{
    /// <summary>尺寸不合格对话框。aria-label 是 Facebook 自己的文案，不能改。</summary>
    public const string RejectedDialogSelector =
        "div[aria-label='请选择其他封面照片'][aria-modal='true'][role='dialog']";

    /// <summary>对话框的展示名，用于拼日志。</summary>
    public const string RejectedDialogName = "请选择其他封面照片";

    /// <summary>关闭按钮。原版同一条串也用在通知弹窗上，见 Popups/NotificationPopup.cs。</summary>
    public const string CloseButtonSelector = "div[aria-label='关闭']";

    /// <summary>「检测到 XX 提示，关闭提示...」</summary>
    public static string DetectionLine(string name = RejectedDialogName) => $"检测到 {name} 提示，关闭提示...";

    /// <summary>「已点击关闭按钮」</summary>
    public const string ClickedCloseLine = "已点击关闭按钮";

    /// <summary>「已按 Escape 关闭提示」</summary>
    public const string PressedEscapeLine = "已按 Escape 关闭提示";

    /// <summary>「检测封面尺寸提示时异常: {原因}」</summary>
    public static string ExceptionLine(string message) => $"检测封面尺寸提示时异常: {message}";

    /// <summary>「背景图尺寸不符合要求（太大或太小），将尝试其他图片」</summary>
    public const string RetryLine = "背景图尺寸不符合要求（太大或太小），将尝试其他图片";
}

/// <summary>换图重试的当前状态。</summary>
public enum CoverAttemptStatus
{
    /// <summary>还没开始，或还有图可试。</summary>
    Pending = 0,

    /// <summary>某一张已被接受。</summary>
    Accepted = 1,

    /// <summary>候选图都试过了，全部被拒。</summary>
    Exhausted = 2,

    /// <summary>一张候选图都没有。</summary>
    NoCandidate = 3,
}

/// <summary>
/// 封面换图重试的状态机。纯逻辑，不碰页面也不碰文件 —— 页面动作由调用方按下发的路径执行。
/// </summary>
/// <remarks>
/// 区分两种「用掉一张图」：
/// <list type="bullet">
/// <item><see cref="SkipLocally"/>：本地预校验就判定不合格，**没有上传**，不占用尝试次数。</item>
/// <item><see cref="MarkRejected"/>：已上传并被页面拒绝，占用一次尝试。</item>
/// </list>
/// 混在一起会算错重试预算：3 张图里 2 张本地就被挡掉时，真正的上传尝试只剩 1 次，
/// 若按「跳过也算一次」计，程序会在还能试的时候提前放弃。
/// </remarks>
public sealed class CoverAttemptPlanner
{
    private readonly List<string> _candidates;
    private readonly List<string> _lines = [];
    private readonly List<string> _localSkips = [];
    private readonly List<string> _rejections = [];

    public CoverAttemptPlanner(IEnumerable<string>? candidates, int maxAttempts = 3)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "最大尝试次数至少为 1");
        }

        _candidates = (candidates ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        MaxAttempts = maxAttempts;
        Status = _candidates.Count == 0 ? CoverAttemptStatus.NoCandidate : CoverAttemptStatus.Pending;
    }

    /// <summary>最多允许上传几次。</summary>
    public int MaxAttempts { get; }

    /// <summary>实际已经上传并等待判定的次数（本地跳过不计）。</summary>
    public int UploadAttempts { get; private set; }

    /// <summary>当前正在尝试的图片路径。</summary>
    public string? Current { get; private set; }

    public CoverAttemptStatus Status { get; private set; }

    /// <summary>已试过的图片路径，按尝试顺序。</summary>
    public IReadOnlyList<string> Tried => _rejections;

    /// <summary>可以直接照着写的日志行。</summary>
    public IReadOnlyList<string> LogLines => _lines;

    /// <summary>还能不能再拿一张。尝试次数用尽或已成功即为 false。</summary>
    public bool HasNext => Status == CoverAttemptStatus.Pending && _candidates.Count > 0;

    /// <summary>
    /// 取下一张待上传的图。返回 null 表示没有可试的了。
    /// </summary>
    public string? TakeNext()
    {
        if (!HasNext)
        {
            return null;
        }

        if (UploadAttempts >= MaxAttempts)
        {
            Status = CoverAttemptStatus.Exhausted;
            return null;
        }

        var next = _candidates[0];
        _candidates.RemoveAt(0);
        Current = next;
        UploadAttempts++;
        return next;
    }

    /// <summary>本地预校验判定不合格，跳过这一张，不占上传次数。</summary>
    public void SkipLocally(string path, string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // 必须真的从候选里移除：只记一笔日志的话，下一次 TakeNext() 还会把这张
        // 本地已经判死的图再上传一遍，白跑一趟还把失败次数算在页面上。
        _candidates.RemoveAll(c => string.Equals(c, path, StringComparison.OrdinalIgnoreCase));

        if (Current is not null && string.Equals(Current, path, StringComparison.OrdinalIgnoreCase))
        {
            // 调用方先取再校验的流程：这次取出没有真的上传，把次数退回去。
            Current = null;
            if (UploadAttempts > 0)
            {
                UploadAttempts--;
            }
        }

        _localSkips.Add(path);
        _lines.Add($"封面图本地预校验未通过，跳过: {Path.GetFileName(path)}（{reason}）");

        if (_candidates.Count == 0 && Status == CoverAttemptStatus.Pending && Current is null)
        {
            Status = CoverAttemptStatus.Exhausted;
        }
    }

    /// <summary>页面判定不合格：记一次失败，并提示准备换图。</summary>
    public void MarkRejected(string? reason = null)
    {
        if (Current is not null)
        {
            _rejections.Add(Current);
        }

        var detail = string.IsNullOrWhiteSpace(reason) ? CoverRejectionSignals.RetryLine : $"{reason}，{CoverRejectionSignals.RetryLine}";
        _lines.Add(detail);
        Current = null;

        if (UploadAttempts >= MaxAttempts || _candidates.Count == 0)
        {
            Status = CoverAttemptStatus.Exhausted;
        }
    }

    /// <summary>页面接受了这一张。</summary>
    public void MarkAccepted()
    {
        Status = CoverAttemptStatus.Accepted;
        Current = null;
    }

    /// <summary>
    /// 失败时的中文总结，直接进任务结果。
    /// </summary>
    public string BuildFailureSummary()
    {
        if (Status == CoverAttemptStatus.NoCandidate)
        {
            return "没有可用的封面图片";
        }

        var skipped = _localSkips.Count == 0 ? string.Empty : $"，其中 {_localSkips.Count} 张未通过本地预校验";
        return $"{_rejections.Count} 张封面图被页面拒绝{skipped}（已尝试 {UploadAttempts} 次，上限 {MaxAttempts} 次）";
    }
}
