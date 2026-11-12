namespace FbAuto.Automation.Media;

/// <summary>
/// 封面尺寸的判定结论。分三档而不是「合格/不合格」，因为「尺寸偏小但 Facebook 会拉伸接受」
/// 和「小到被直接拒绝」是两种完全不同的处理：前者照常上传并记一条提醒，后者才需要换图。
/// </summary>
public enum CoverSizeVerdict
{
    /// <summary>合格，可直接上传。</summary>
    Accepted = 0,

    /// <summary>可以上传，但成品效果可能打折扣（偏小、偏大、比例偏离）。</summary>
    AcceptedWithWarning = 1,

    /// <summary>不合格，上传后被拒或必然变形，应换图。</summary>
    Rejected = 2,
}

/// <summary>判定原因。用枚举而不是字符串，测试才能精确断言到「因为哪一条」。</summary>
public enum CoverIssue
{
    None = 0,

    /// <summary>读不出像素尺寸（文件损坏 / 头部截断 / 不是图片）。</summary>
    UnreadableImage = 1,

    /// <summary>格式不在白名单内。</summary>
    UnsupportedFormat = 2,

    /// <summary>宽或高低于最小可接受尺寸。</summary>
    TooSmall = 3,

    /// <summary>文件体积超过上限。</summary>
    FileTooLarge = 4,

    /// <summary>尺寸低于推荐值（Facebook 会拉伸，可能发虚）。</summary>
    BelowRecommended = 5,

    /// <summary>体积超过推荐值（会被二次压缩）。</summary>
    FileLargerThanRecommended = 6,

    /// <summary>宽高比明显偏离封面比例，会被裁切。</summary>
    UnusualAspectRatio = 7,
}

/// <summary>
/// 一次封面校验的结果。<see cref="Message"/> 直接进日志，必须是能看懂的中文。
/// </summary>
public sealed record CoverSizeCheckResult(
    CoverSizeVerdict Verdict,
    CoverIssue PrimaryIssue,
    string Message,
    IReadOnlyList<CoverIssue> Warnings)
{
    public bool CanUpload => Verdict != CoverSizeVerdict.Rejected;

    public static CoverSizeCheckResult Reject(CoverIssue issue, string message) =>
        new(CoverSizeVerdict.Rejected, issue, message, []);

    public static CoverSizeCheckResult Warn(CoverIssue issue, string message, IReadOnlyList<CoverIssue> warnings) =>
        new(CoverSizeVerdict.AcceptedWithWarning, issue, message, warnings);

    public static CoverSizeCheckResult Accept() =>
        new(CoverSizeVerdict.Accepted, CoverIssue.None, "封面尺寸合格", []);
}

/// <summary>
/// 封面尺寸阈值。做成数据而不是写死在判断里，是因为阈值来源是平台规定（会变），
/// 而判断逻辑不会变 —— 平台改了尺寸，只改这里的默认值，不动代码。
/// </summary>
/// <param name="MinWidth">最小可接受宽度（像素）。</param>
/// <param name="MinHeight">最小可接受高度（像素）。</param>
/// <param name="RecommendedWidth">推荐宽度。</param>
/// <param name="RecommendedHeight">推荐高度。</param>
/// <param name="MaxBytes">文件体积上限。</param>
/// <param name="RecommendedMaxBytes">建议体积上限（超过只提醒）。</param>
/// <param name="AllowedFormats">允许的图片格式。</param>
/// <param name="MinAspectRatio">可接受的宽高比下限。</param>
/// <param name="MaxAspectRatio">可接受的宽高比上限。</param>
public sealed record CoverSizeRule(
    int MinWidth,
    int MinHeight,
    int RecommendedWidth,
    int RecommendedHeight,
    long MaxBytes,
    long RecommendedMaxBytes,
    IReadOnlySet<ImageFormat> AllowedFormats,
    double MinAspectRatio,
    double MaxAspectRatio)
{
    /// <summary>
    /// Facebook 公共主页封面的默认阈值。
    /// </summary>
    /// <remarks>
    /// 数据来源（2026-11-12 查证，多个独立来源一致）：
    /// 桌面显示 820×312，移动端显示 640×360，**最小上传 400×150**，
    /// 官方建议 sRGB JPG、约 851×315、100KB 以内；图片体积上限 15MB。
    ///
    /// 注意：**原版并没有本地像素校验**。它的 <c>CheckCoverSizeErrorAsync</c> 是上传后
    /// 等待页面弹出「请选择其他封面照片」对话框来判定（证据见 CoverRejection.cs 注释）。
    /// 这里的本地预校验是增强项，目的是在上传前就挡掉必然失败的图，省一次往返。
    /// 比例区间取 [2.2, 3.0]：覆盖 820×312(2.63) 与设计画布 820×360(2.28)，
    /// 超出则提醒会被裁切，但不拒绝。
    /// </remarks>
    public static CoverSizeRule FacebookPageCover { get; } = new(
        MinWidth: 400,
        MinHeight: 150,
        RecommendedWidth: 820,
        RecommendedHeight: 312,
        MaxBytes: 15L * 1024 * 1024,
        RecommendedMaxBytes: 100L * 1024,
        AllowedFormats: new HashSet<ImageFormat> { ImageFormat.Png, ImageFormat.Jpeg, ImageFormat.Webp },
        MinAspectRatio: 2.2,
        MaxAspectRatio: 3.0);
}

/// <summary>
/// 封面尺寸判定器。纯函数：同样的输入永远得到同样的结论，不碰文件、不碰网络。
/// </summary>
public static class CoverSizeChecker
{
    /// <summary>
    /// 判定一张封面图。
    /// </summary>
    /// <param name="inspection">头部解析结果。解析失败时传 null。</param>
    /// <param name="byteLength">文件字节数。</param>
    /// <param name="rule">阈值。传 null 用 <see cref="CoverSizeRule.FacebookPageCover"/>。</param>
    /// <param name="readFailure">头部解析失败的中文原因。</param>
    public static CoverSizeCheckResult Check(
        ImageInfo? inspection,
        long byteLength,
        CoverSizeRule? rule = null,
        string? readFailure = null)
    {
        rule ??= CoverSizeRule.FacebookPageCover;

        if (inspection is null)
        {
            return CoverSizeCheckResult.Reject(
                CoverIssue.UnreadableImage,
                string.IsNullOrWhiteSpace(readFailure) ? "无法读取封面图片尺寸" : $"无法读取封面图片尺寸: {readFailure}");
        }

        if (!rule.AllowedFormats.Contains(inspection.Format))
        {
            return CoverSizeCheckResult.Reject(
                CoverIssue.UnsupportedFormat,
                $"封面格式不支持: {inspection.Format}（可用 {string.Join(" / ", rule.AllowedFormats)}）");
        }

        if (byteLength > rule.MaxBytes)
        {
            return CoverSizeCheckResult.Reject(
                CoverIssue.FileTooLarge,
                $"封面体积 {FormatBytes(byteLength)} 超过上限 {FormatBytes(rule.MaxBytes)}");
        }

        if (inspection.Width < rule.MinWidth || inspection.Height < rule.MinHeight)
        {
            return CoverSizeCheckResult.Reject(
                CoverIssue.TooSmall,
                $"封面尺寸 {inspection.Width}×{inspection.Height} 小于最小要求 {rule.MinWidth}×{rule.MinHeight}");
        }

        var warnings = new List<CoverIssue>();
        var messages = new List<string>();

        if (inspection.Width < rule.RecommendedWidth || inspection.Height < rule.RecommendedHeight)
        {
            warnings.Add(CoverIssue.BelowRecommended);
            messages.Add($"尺寸低于推荐值 {rule.RecommendedWidth}×{rule.RecommendedHeight}，可能被拉伸");
        }

        if (byteLength > rule.RecommendedMaxBytes)
        {
            warnings.Add(CoverIssue.FileLargerThanRecommended);
            messages.Add($"体积 {FormatBytes(byteLength)} 超过建议值 {FormatBytes(rule.RecommendedMaxBytes)}，上传后会被二次压缩");
        }

        if (inspection.Height > 0)
        {
            var ratio = inspection.AspectRatio;
            if (ratio < rule.MinAspectRatio || ratio > rule.MaxAspectRatio)
            {
                warnings.Add(CoverIssue.UnusualAspectRatio);
                messages.Add($"宽高比 {ratio:F2}:1 偏离封面比例，展示时会被裁切");
            }
        }

        if (warnings.Count == 0)
        {
            return CoverSizeCheckResult.Accept();
        }

        return CoverSizeCheckResult.Warn(
            warnings[0],
            $"封面可上传，但{string.Join("；", messages)}",
            warnings);
    }

    /// <summary>把字节数格式化成日志里好看的写法。</summary>
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 0 => "0 B",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
        _ => $"{bytes / (1024d * 1024d):F1} MB",
    };
}
