using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Tests;

/// <summary>
/// 按通道取业务参数。
/// </summary>
/// <remarks>
/// 原版 settings.json 里业务路径是**成对**的：<c>GoogleVideoPath</c> / <c>BitVideoPath</c>、
/// <c>GoogleSignatureFilePath</c> / <c>BitSignatureFilePath</c>，连评论屏蔽词都两份。
/// 这一层就是把「读哪一份」收敛到一个地方，让「比特版读成了谷歌版」只可能错一次，且能被测出来。
/// </remarks>
public sealed class ChannelProfileTests
{
    [Fact]
    public void 默认配置下两套参数都为空且互不干扰()
    {
        var config = new AppConfig();

        var google = config.ForChannel(BrowserChannelKind.Google);
        var bit = config.ForChannel(BrowserChannelKind.Bit);

        Assert.Equal(BrowserChannelKind.Google, google.Channel);
        Assert.Equal(BrowserChannelKind.Bit, bit.Channel);
        Assert.Equal(string.Empty, google.VideoDirectory);
        Assert.Equal(string.Empty, bit.VideoDirectory);
        Assert.Equal(5, google.CommentsBlockWords.Count);
        Assert.Equal(5, bit.CommentsBlockWords.Count);
    }

    [Fact]
    public void 比特通道不会读到谷歌版的路径()
    {
        var config = new AppConfig
        {
            VideoDirectory = @"D:\g\videos",
            DescriptionFile = @"D:\g\desc.txt",
            SignatureFile = @"D:\g\sig.txt",
            ProfileCategory = "教育",

            // 图片目录也要给谷歌版填上非空值：留空的话「比特版回落谷歌版」这条规则
            // 在断言上完全看不出来（两边都是空串），注入实验打不红，等于没有守卫。
            ImageDirectory = @"D:\g\images",
        };

        var bit = config.ForChannel(BrowserChannelKind.Bit);

        // 比特版没配就应该是空，**不能回落到谷歌版** —— 回落的后果是拿错了目录却不报错。
        Assert.Equal(string.Empty, bit.VideoDirectory);
        Assert.Equal(string.Empty, bit.DescriptionFile);
        Assert.Equal(string.Empty, bit.SignatureFile);
        Assert.Equal(string.Empty, bit.ProfileCategory);

        // 图片目录同样是成对的：回落的后果是给比特版账号换上了谷歌版那批头像。
        Assert.Equal(string.Empty, bit.ImageDirectory);
    }

    [Fact]
    public void 谷歌通道不会读到比特版的路径()
    {
        var config = new AppConfig
        {
            BitVideoDirectory = @"D:\b\videos",
            BitDescriptionFile = @"D:\b\desc.txt",
            BitSignatureFile = @"D:\b\sig.txt",
            BitProfileCategory = "戏剧",
            BitImageDirectory = @"D:\b\images",
        };

        var google = config.ForChannel(BrowserChannelKind.Google);

        Assert.Equal(string.Empty, google.VideoDirectory);
        Assert.Equal(string.Empty, google.DescriptionFile);
        Assert.Equal(string.Empty, google.SignatureFile);
        Assert.Equal(string.Empty, google.ProfileCategory);
        Assert.Equal(string.Empty, google.ImageDirectory);
    }

    [Fact]
    public void 两套参数各自独立赋值()
    {
        var config = new AppConfig
        {
            VideoDirectory = @"D:\g",
            BitVideoDirectory = @"D:\b",
            ImageDirectory = @"D:\g\images",
            BitImageDirectory = @"D:\b\images",
            UpdatePostingSettings = false,
            BitUpdatePostingSettings = true,
            CommentsBlockWords = ["g1", "g2"],
            BitCommentsBlockWords = ["b1"],
        };

        var google = config.ForChannel(BrowserChannelKind.Google);
        var bit = config.ForChannel(BrowserChannelKind.Bit);

        Assert.Equal(@"D:\g", google.VideoDirectory);
        Assert.Equal(@"D:\b", bit.VideoDirectory);
        Assert.Equal(@"D:\g\images", google.ImageDirectory);
        Assert.Equal(@"D:\b\images", bit.ImageDirectory);
        Assert.False(google.UpdatePostingSettings);
        Assert.True(bit.UpdatePostingSettings);
        Assert.Equal(["g1", "g2"], google.CommentsBlockWords);
        Assert.Equal(["b1"], bit.CommentsBlockWords);
    }

    /// <summary>
    /// 每次取都反映当前配置，不是缓存的旧快照。
    /// </summary>
    /// <remarks>
    /// 这条防的是「把 profile 缓存在字段里」：界面上改了路径、任务还在用旧值，
    /// 表现是「怎么改都不生效」，而日志里打印的路径看起来又是对的（那是旧值）。
    /// </remarks>
    [Fact]
    public void 取到的参数跟随配置变化()
    {
        var config = new AppConfig { BitVideoDirectory = @"D:\before" };
        var first = config.ForChannel(BrowserChannelKind.Bit);

        config.BitVideoDirectory = @"D:\after";
        var second = config.ForChannel(BrowserChannelKind.Bit);

        Assert.Equal(@"D:\before", first.VideoDirectory);
        Assert.Equal(@"D:\after", second.VideoDirectory);
    }

    /// <summary>
    /// 未知通道值落谷歌版，不抛异常。
    /// </summary>
    /// <remarks>
    /// 配置里的通道值可能来自旧版本或被手改。这里抛异常会让整个任务失败在「读配置」这一步，
    /// 而失败原因只会是一句枚举转换错误 —— 使用者完全看不懂。宁可回落到默认通道。
    /// </remarks>
    [Fact]
    public void 未知通道值落谷歌版()
    {
        var config = new AppConfig { VideoDirectory = @"D:\g" };

        var profile = config.ForChannel((BrowserChannelKind)99);

        Assert.Equal(BrowserChannelKind.Google, profile.Channel);
        Assert.Equal(@"D:\g", profile.VideoDirectory);
    }

    [Theory]
    [InlineData(BrowserChannelKind.Google, "谷歌版")]
    [InlineData(BrowserChannelKind.Bit, "比特版")]
    public void 通道显示名与界面一致(BrowserChannelKind channel, string expected)
    {
        var profile = new AppConfig().ForChannel(channel);

        Assert.Equal(expected, profile.DisplayName);
    }
}
