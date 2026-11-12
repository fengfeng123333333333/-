using FbAuto.Automation.Media;

namespace FbAuto.Tests;

/// <summary>
/// 封面尺寸判定。重点不是「合格能过」，而是**不合格的每一档都能被准确指出来**：
/// 换图逻辑要知道是「太小」还是「太大」还是「格式不对」，才能给出对的提示。
/// </summary>
public sealed class CoverSizeCheckerTests
{
    private const long OneMb = 1024 * 1024;
    private const long OneKb = 1024;

    private static readonly CoverSizeRule Rule = CoverSizeRule.FacebookPageCover;

    [Fact]
    public void 推荐尺寸且体积合适_直接合格()
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Png, 820, 312), 90 * OneKb);

        Assert.Equal(CoverSizeVerdict.Accepted, result.Verdict);
        Assert.Equal(CoverIssue.None, result.PrimaryIssue);
        Assert.Empty(result.Warnings);
        Assert.True(result.CanUpload);
    }

    [Fact]
    public void 正好等于最小尺寸_可上传但提示低于推荐()
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Jpeg, 400, 150), 50 * OneKb);

        Assert.Equal(CoverSizeVerdict.AcceptedWithWarning, result.Verdict);
        Assert.Equal(CoverIssue.BelowRecommended, result.PrimaryIssue);
        Assert.Contains(CoverIssue.BelowRecommended, result.Warnings);
        Assert.True(result.CanUpload);
    }

    [Theory]
    [InlineData(399, 150)] // 宽度差 1 像素
    [InlineData(400, 149)] // 高度差 1 像素
    [InlineData(200, 100)]
    public void 小于最小尺寸_拒绝并说明差在哪(int width, int height)
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Png, width, height), 50 * OneKb);

        Assert.Equal(CoverSizeVerdict.Rejected, result.Verdict);
        Assert.Equal(CoverIssue.TooSmall, result.PrimaryIssue);
        Assert.Contains($"{width}×{height}", result.Message);
        Assert.Contains("400×150", result.Message);
        Assert.False(result.CanUpload);
    }

    [Fact]
    public void 格式不在白名单_拒绝并列出可用格式()
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Gif, 820, 312), 50 * OneKb);

        Assert.Equal(CoverSizeVerdict.Rejected, result.Verdict);
        Assert.Equal(CoverIssue.UnsupportedFormat, result.PrimaryIssue);
        Assert.Contains("Gif", result.Message);
        Assert.Contains("Png", result.Message);
    }

    [Fact]
    public void 体积超过上限_拒绝()
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Jpeg, 1280, 720), 16 * OneMb);

        Assert.Equal(CoverSizeVerdict.Rejected, result.Verdict);
        Assert.Equal(CoverIssue.FileTooLarge, result.PrimaryIssue);
        Assert.Contains("MB", result.Message);
    }

    [Fact]
    public void 体积正好等于上限_不拒绝()
    {
        // 边界：判据是「超过」而不是「达到」，否则临界值会被误杀。
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Jpeg, 820, 312), 15 * OneMb);

        Assert.NotEqual(CoverSizeVerdict.Rejected, result.Verdict);
    }

    [Fact]
    public void 体积超过建议值但未超上限_只警告()
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Jpeg, 820, 312), 2 * OneMb);

        Assert.Equal(CoverSizeVerdict.AcceptedWithWarning, result.Verdict);
        Assert.Contains(CoverIssue.FileLargerThanRecommended, result.Warnings);
        Assert.True(result.CanUpload);
    }

    [Fact]
    public void 正方形图_提示比例会被裁切()
    {
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Png, 1000, 1000), 50 * OneKb);

        Assert.Equal(CoverSizeVerdict.AcceptedWithWarning, result.Verdict);
        Assert.Contains(CoverIssue.UnusualAspectRatio, result.Warnings);
        Assert.Contains("裁切", result.Message);
    }

    [Fact]
    public void 读不出尺寸_拒绝并带上解析原因()
    {
        var result = CoverSizeChecker.Check(null, 50 * OneKb, readFailure: "JPEG 中未找到 SOF 段");

        Assert.Equal(CoverSizeVerdict.Rejected, result.Verdict);
        Assert.Equal(CoverIssue.UnreadableImage, result.PrimaryIssue);
        Assert.Contains("JPEG 中未找到 SOF 段", result.Message);
    }

    [Fact]
    public void 读不出尺寸且没给原因_也有兜底文案()
    {
        var result = CoverSizeChecker.Check(null, 50 * OneKb);

        Assert.Equal(CoverIssue.UnreadableImage, result.PrimaryIssue);
        Assert.Contains("无法读取封面图片尺寸", result.Message);
    }

    [Fact]
    public void 多个问题并存_警告全部收集()
    {
        // 低于推荐 + 体积超建议 + 比例偏离，三种警告应同时出现。
        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Png, 500, 500), 500 * OneKb);

        Assert.Equal(CoverSizeVerdict.AcceptedWithWarning, result.Verdict);
        Assert.Equal(3, result.Warnings.Count);
        Assert.Contains(CoverIssue.BelowRecommended, result.Warnings);
        Assert.Contains(CoverIssue.FileLargerThanRecommended, result.Warnings);
        Assert.Contains(CoverIssue.UnusualAspectRatio, result.Warnings);
    }

    [Fact]
    public void 自定义阈值真的生效_证明没写死()
    {
        // 把最小尺寸调高到 1000×500 后，820×312 应该被拒 —— 若判定里写死了 400×150，这条会红。
        var strict = Rule with { MinWidth = 1000, MinHeight = 500 };

        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Png, 820, 312), 50 * OneKb, strict);

        Assert.Equal(CoverSizeVerdict.Rejected, result.Verdict);
        Assert.Equal(CoverIssue.TooSmall, result.PrimaryIssue);
    }

    [Fact]
    public void 自定义允许格式生效()
    {
        var strict = Rule with { AllowedFormats = new HashSet<ImageFormat> { ImageFormat.Png } };

        var result = CoverSizeChecker.Check(new ImageInfo(ImageFormat.Jpeg, 820, 312), 50 * OneKb, strict);

        Assert.Equal(CoverSizeVerdict.Rejected, result.Verdict);
        Assert.Equal(CoverIssue.UnsupportedFormat, result.PrimaryIssue);
    }

    [Fact]
    public void 默认规则的最小尺寸是400乘150()
    {
        // 阈值来自平台规定（查证于 2026-11-12，多来源一致）。这条用例是防止有人顺手改数字。
        Assert.Equal(400, Rule.MinWidth);
        Assert.Equal(150, Rule.MinHeight);
        Assert.Equal(820, Rule.RecommendedWidth);
        Assert.Equal(312, Rule.RecommendedHeight);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(-1, "0 B")]
    public void 体积格式化(long bytes, string expected)
    {
        Assert.Equal(expected, CoverSizeChecker.FormatBytes(bytes));
    }
}
