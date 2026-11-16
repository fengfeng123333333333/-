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

            // 名称文件与密码同理，必须给谷歌版非空值 —— 这一条是注入实验打不红才补上的：
            // 只加断言不加反例，等于写着检查、实际永远绿。
            NamesFile = @"D:\g\names.txt",
            PageNamePassword = "g-pw",

            // 结果保存目录同上：回落的后果是比特版账号的失败写进谷歌版那份清单。
            ProfileEditResultExportDir = @"D:\g\export",
        };

        var bit = config.ForChannel(BrowserChannelKind.Bit);

        // 比特版没配就应该是空，**不能回落到谷歌版** —— 回落的后果是拿错了目录却不报错。
        Assert.Equal(string.Empty, bit.VideoDirectory);
        Assert.Equal(string.Empty, bit.DescriptionFile);
        Assert.Equal(string.Empty, bit.SignatureFile);
        Assert.Equal(string.Empty, bit.ProfileCategory);

        // 图片目录同样是成对的：回落的后果是给比特版账号换上了谷歌版那批头像。
        Assert.Equal(string.Empty, bit.ImageDirectory);

        // 名称文件与密码：回落的后果是比特版账号改成谷歌版那份名字清单里的名字，
        // 而名字一改就要走 FB 审核，比换错头像严重得多。
        Assert.Equal(string.Empty, bit.NamesFile);
        Assert.Equal(string.Empty, bit.PageNamePassword);

        // 结果保存目录：回落的后果是比特版的失败混进谷歌版那份 AccountErrors 文件。
        Assert.Equal(string.Empty, bit.ProfileEditResultExportDir);
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
            BitNamesFile = @"D:\b\names.txt",
            BitPageNamePassword = "b-pw",
            BitProfileEditResultExportDir = @"D:\b\export",
        };

        var google = config.ForChannel(BrowserChannelKind.Google);

        Assert.Equal(string.Empty, google.VideoDirectory);
        Assert.Equal(string.Empty, google.DescriptionFile);
        Assert.Equal(string.Empty, google.SignatureFile);
        Assert.Equal(string.Empty, google.ProfileCategory);
        Assert.Equal(string.Empty, google.ImageDirectory);
        Assert.Equal(string.Empty, google.NamesFile);
        Assert.Equal(string.Empty, google.PageNamePassword);
        Assert.Equal(string.Empty, google.ProfileEditResultExportDir);
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
            NamesFile = @"D:\g\names.txt",
            BitNamesFile = @"D:\b\names.txt",
            PageNamePassword = "g-pw",
            BitPageNamePassword = "b-pw",
            ProfileEditResultExportDir = @"D:\g\export",
            BitProfileEditResultExportDir = @"D:\b\export",
            UpdatePostingSettings = false,
            BitUpdatePostingSettings = true,
            CommentsBlockWords = ["g1", "g2"],
            BitCommentsBlockWords = ["b1"],

            // 类别两侧各填一个不同的值：只断「比特版为空」看不出「比特版拿到了谷歌版的值」，
            // 而后者才是回落缺陷的真实表现。
            ProfileCategory = "教育",
            BitProfileCategory = "戏剧",
        };

        var google = config.ForChannel(BrowserChannelKind.Google);
        var bit = config.ForChannel(BrowserChannelKind.Bit);

        Assert.Equal(@"D:\g", google.VideoDirectory);
        Assert.Equal(@"D:\b", bit.VideoDirectory);
        Assert.Equal(@"D:\g\images", google.ImageDirectory);
        Assert.Equal(@"D:\b\images", bit.ImageDirectory);
        Assert.Equal(@"D:\g\names.txt", google.NamesFile);
        Assert.Equal(@"D:\b\names.txt", bit.NamesFile);
        Assert.Equal("g-pw", google.PageNamePassword);
        Assert.Equal("b-pw", bit.PageNamePassword);
        Assert.Equal(@"D:\g\export", google.ProfileEditResultExportDir);
        Assert.Equal(@"D:\b\export", bit.ProfileEditResultExportDir);
        Assert.False(google.UpdatePostingSettings);
        Assert.True(bit.UpdatePostingSettings);
        Assert.Equal(["g1", "g2"], google.CommentsBlockWords);
        Assert.Equal(["b1"], bit.CommentsBlockWords);
        Assert.Equal("教育", google.ProfileCategory);
        Assert.Equal("戏剧", bit.ProfileCategory);
    }

    /// <summary>
    /// 七项开关也必须成对 —— 谷歌版关掉的一项不能让比特版跟着关。
    /// </summary>
    /// <remarks>
    /// **两侧都显式赋成不同的值**，不靠默认值。这是 S11 与 S13 各栽过一次的地方：
    /// 只写「谷歌版是 false」的断言，在两边都落到默认值（同为 true 或同为空串）时也成立，
    /// 往 ForChannel 里注入「比特版回落谷歌版」这类错，用例照样绿。
    ///
    /// 发帖设置那一项用的是 <c>UpdatePostingSettings</c>（原版键名 EditPagePostSettings），
    /// 它本来就在这条链路上，这里一并钉住。
    /// </remarks>
    [Fact]
    public void 七项开关各自独立不互相回落()
    {
        var config = new AppConfig
        {
            EditCoverImage = false,
            BitEditCoverImage = true,
            EditAvatar = true,
            BitEditAvatar = false,
            UpdatePostingSettings = false,
            BitUpdatePostingSettings = true,
            EditFollowersContent = true,
            BitEditFollowersContent = false,
            EditSignature = false,
            BitEditSignature = true,
            EditCategory = true,
            BitEditCategory = false,
            EditPageName = false,
            BitEditPageName = true,
        };

        var google = config.ForChannel(BrowserChannelKind.Google);
        var bit = config.ForChannel(BrowserChannelKind.Bit);

        Assert.False(google.EditCoverImage);
        Assert.True(bit.EditCoverImage);
        Assert.True(google.EditAvatar);
        Assert.False(bit.EditAvatar);
        Assert.False(google.UpdatePostingSettings);
        Assert.True(bit.UpdatePostingSettings);
        Assert.True(google.EditFollowersContent);
        Assert.False(bit.EditFollowersContent);
        Assert.False(google.EditSignature);
        Assert.True(bit.EditSignature);
        Assert.True(google.EditCategory);
        Assert.False(bit.EditCategory);
        Assert.False(google.EditPageName);
        Assert.True(bit.EditPageName);
    }

    /// <summary>
    /// 缺键的老配置必须仍然「七项照改」。
    /// </summary>
    /// <remarks>
    /// 原版两份 settings.json 里都没有 <c>EditXxx</c> 这七个键，所以老配置反序列化时走的是初值。
    /// 初值一旦写成 false，升级后的使用者会看到任务全部报成功、实际一项没改 ——
    /// 而且没有任何失败记录可查，比直接报错难查得多。
    ///
    /// 这条用例盯的就是初值本身：把 <c>AppConfig</c> 里任意一个 <c>= true</c> 改成 <c>false</c> 就会红。
    /// </remarks>
    [Fact]
    public void 老配置缺开关键时七项照改()
    {
        var profile = new AppConfig().ForChannel(BrowserChannelKind.Google);
        var bitProfile = new AppConfig().ForChannel(BrowserChannelKind.Bit);

        Assert.True(profile.EditCoverImage);
        Assert.True(profile.EditAvatar);
        Assert.True(profile.UpdatePostingSettings);
        Assert.True(profile.EditFollowersContent);
        Assert.True(profile.EditSignature);
        Assert.True(profile.EditCategory);
        Assert.True(profile.EditPageName);

        Assert.True(bitProfile.EditCoverImage);
        Assert.True(bitProfile.EditAvatar);
        Assert.True(bitProfile.UpdatePostingSettings);
        Assert.True(bitProfile.EditFollowersContent);
        Assert.True(bitProfile.EditSignature);
        Assert.True(bitProfile.EditCategory);
        Assert.True(bitProfile.EditPageName);

        // 七项全开时，清单必须是完整七项且按原版分发顺序。
        Assert.Equal(
            ["背景图", "头像", "发帖设置", "粉丝公开内容", "个性签名", "类别", "主页名字"],
            profile.EnabledEditItems);
    }

    /// <summary>关掉的项不出现在「本次要改」清单里，且顺序不因缺项而变。</summary>
    [Fact]
    public void 关掉的项不进入待改清单()
    {
        var config = new AppConfig
        {
            EditCoverImage = false,
            EditCategory = false,
            EditPageName = false,
        };

        var profile = config.ForChannel(BrowserChannelKind.Google);

        Assert.Equal(["头像", "发帖设置", "粉丝公开内容", "个性签名"], profile.EnabledEditItems);
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
