using FbAuto.Automation.Uploads;

namespace FbAuto.Tests;

/// <summary>
/// asset_id 提取。没有它拼不出批量上传页的地址，整个流程第一步就走不动。
/// 原版留了三条路：直接链接 + 两种兜底，兜底命中时日志会写「通过 fallback 方式提取成功」。
/// </summary>
public sealed class AssetIdExtractorTests
{
    [Fact]
    public void 从leads_center链接直接取到()
    {
        const string content = """
            <a href="https://business.facebook.com/latest/leads_center?asset_id=1234567890&ref=x">主页</a>
            """;

        var result = AssetIdExtractor.ExtractDetailed(content);

        Assert.True(result.Found);
        Assert.Equal("1234567890", result.AssetId);
        Assert.Equal(AssetIdSource.LeadsCenter, result.Source);
        Assert.Contains("提取成功", result.LogLine);
    }

    [Fact]
    public void 链接里的asset_id被URL编码过也能取到()
    {
        var result = AssetIdExtractor.ExtractDetailed("...asset_id%3D9876543210...");

        Assert.Equal("9876543210", result.AssetId);
        Assert.Equal(AssetIdSource.LeadsCenter, result.Source);
    }

    [Fact]
    public void LEGACY前缀兜底()
    {
        var result = AssetIdExtractor.ExtractDetailed("window.__x = {id:'LEGACY_555666777'}");

        Assert.Equal("555666777", result.AssetId);
        Assert.Equal(AssetIdSource.Legacy, result.Source);
        Assert.Contains("fallback", result.LogLine);
    }

    [Fact]
    public void boosted_post前缀兜底()
    {
        var result = AssetIdExtractor.ExtractDetailed("page/888999000_boosted_post_111222333");

        Assert.Equal("111222333", result.AssetId);
        Assert.Equal(AssetIdSource.BoostedPost, result.Source);
        Assert.Contains("fallback", result.LogLine);
    }

    [Fact]
    public void 直接链接优先于兜底()
    {
        const string content = "LEGACY_222222 && asset_id=111111";

        var result = AssetIdExtractor.ExtractDetailed(content);

        // 兜底是猜的，直接链接是页面自己给的。两者都在时必须用后者。
        Assert.Equal("111111", result.AssetId);
        Assert.Equal(AssetIdSource.LeadsCenter, result.Source);
    }

    [Fact]
    public void 找不到时明确返回未命中()
    {
        var result = AssetIdExtractor.ExtractDetailed("<html>no id here</html>");

        Assert.False(result.Found);
        Assert.Equal(AssetIdSource.None, result.Source);
        Assert.Contains("没有找到任何 asset_id", result.LogLine);
    }

    [Fact]
    public void 太短的数字不当成asset_id()
    {
        // 短数字多半是别的 ID（帖子序号、分页参数），当成 asset_id 会拼出错误的地址。
        var result = AssetIdExtractor.ExtractDetailed("asset_id=12345");

        Assert.False(result.Found);
    }

    [Fact]
    public void 空内容不抛异常()
    {
        Assert.False(AssetIdExtractor.ExtractDetailed(null).Found);
        Assert.False(AssetIdExtractor.ExtractDetailed(string.Empty).Found);
        Assert.False(AssetIdExtractor.ExtractDetailed("   ").Found);
    }

    [Fact]
    public void 简化入口只返回id()
    {
        Assert.Equal("1234567890", AssetIdExtractor.Extract("asset_id=1234567890"));
        Assert.Null(AssetIdExtractor.Extract("nothing"));
    }
}
