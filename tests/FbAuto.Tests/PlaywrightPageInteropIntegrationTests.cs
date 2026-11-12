using FbAuto.Automation.Pages;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S6 集测：<see cref="PlaywrightPageInterop"/> 的六个原语在真 Chrome 里的行为。
/// </summary>
/// <remarks>
/// 全用本地仿真页，不依赖外网。这些用例锁的是「实现细节层面的正确性」：
/// 可见性探的是即时值而不是等待、可用性认 aria-disabled、隐藏的 file input 能注入、
/// 序号按可见匹配数。每一条都对应一个会静默出错的写法。
/// </remarks>
[Collection("chrome")]
public sealed class PlaywrightPageInteropIntegrationTests : ChromePageTestBase
{
    private static PlaywrightPageInterop Interop(Microsoft.Playwright.IPage page, TimeSpan? timeout = null) =>
        new(page, new PageInteropOptions(ActionTimeout: timeout ?? TimeSpan.FromSeconds(5)));

    [Fact]
    public async Task 可见性区分存在_隐藏_不存在()
    {
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page);

        Assert.True(await interop.IsVisibleAsync("#native-enabled"));
        Assert.False(await interop.IsVisibleAsync("[data-id='hidden']"), "display:none 的元素不能算可见");
        Assert.False(await interop.IsVisibleAsync("#never-existed"));
    }

    [Fact]
    public async Task 可用性认_aria_disabled_也认原生_disabled()
    {
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page);

        // 可见顺序：a(0) / b(1, aria-disabled) / c(2)
        Assert.True(await interop.IsEnabledAsync(".pick", 0));
        Assert.False(await interop.IsEnabledAsync(".pick", 1), "aria-disabled='true' 必须判为不可用");
        Assert.True(await interop.IsEnabledAsync(".pick", 2));

        Assert.False(await interop.IsEnabledAsync("#native-disabled"));
        Assert.True(await interop.IsEnabledAsync("#native-enabled"));

        // 元素不存在时返回 false，而不是抛异常 —— 决策器靠它做分支。
        Assert.False(await interop.IsEnabledAsync("#never-existed"));
    }

    [Fact]
    public async Task 序号按可见匹配数_隐藏元素不占位()
    {
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page);

        Assert.Equal(3, await interop.CountAsync(".pick"));

        // 只点 a 与 c：b 是 aria-disabled，Playwright 的点击会一直等「可用」并超时，
        // 这本身说明它的可点击性与我们自己的可用性判定是一致的（见下一条用例）。
        await interop.ClickAsync(".pick", 0);
        await interop.ClickAsync(".pick", 2);

        Assert.Equal(["a", "c"], await ReadAsync<string[]>(page, "() => window.__picked"));
    }

    [Fact]
    public async Task 禁用的元素点不动_与可用性判定一致()
    {
        // 这条锁的是「决策器说可用」和「Playwright 真能点」之间不能有分歧。
        // 若两者不一致，决策器会挑一个点不动的按钮，然后每条任务都卡满一个点击超时。
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page, TimeSpan.FromSeconds(1));

        Assert.False(await interop.IsEnabledAsync(".pick", 1));

        var ex = await Assert.ThrowsAsync<PageActionException>(() => interop.ClickAsync(".pick", 1));
        Assert.Contains("点击超时", ex.Message);
        Assert.Empty(await ReadAsync<string[]>(page, "() => window.__picked"));
    }

    [Fact]
    public async Task 隐藏的_file_input_能直接注入()
    {
        var png = ImageFixtures.WritePng(Dir.Path, "cover.png", 820, 312);
        var page = await OpenPageAsync(FacebookMimicPage.CoverEditor());
        var interop = Interop(page);

        Assert.False(
            await interop.IsVisibleAsync(FacebookSelectors.CoverPhotoFileInput),
            "真页面的 file input 是隐藏的，前提没成立这条用例就没意义");

        await interop.SetFileInputAsync(FacebookSelectors.CoverPhotoFileInput, png);

        Assert.Equal(["cover.png"], await ReadAsync<string[]>(page, "() => window.__uploads"));
        Assert.Equal("cover.png", await ReadAsync<string>(page, "() => window.__accepted"));
    }

    [Fact]
    public async Task 注入不存在的文件时报中文错误()
    {
        var page = await OpenPageAsync(FacebookMimicPage.CoverEditor());
        var interop = Interop(page);

        var ex = await Assert.ThrowsAsync<PageActionException>(() =>
            interop.SetFileInputAsync(FacebookSelectors.CoverPhotoFileInput, Dir.Combine("nope.png")));

        Assert.Contains("文件不存在", ex.Message);
    }

    [Fact]
    public async Task 无效选择器报中文错误而不是英文原始异常()
    {
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page);

        var ex = await Assert.ThrowsAsync<PageActionException>(() => interop.IsVisibleAsync("div[[["));
        Assert.Contains("选择器", ex.Message);
        Assert.Equal("div[[[", ex.Selector);
    }

    [Fact]
    public async Task 点不到元素时按秒数报错并带上序号()
    {
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page, TimeSpan.FromSeconds(1));

        var ex = await Assert.ThrowsAsync<PageActionException>(() => interop.ClickAsync("#never-existed", 1));

        Assert.Contains("找不到第 2 个可见元素", ex.Message);
        Assert.Contains("1 秒", ex.Message);
    }

    [Fact]
    public async Task 按_Escape_会真的落到页面上()
    {
        var page = await OpenPageAsync(FacebookMimicPage.ButtonGrid());
        var interop = Interop(page);

        await interop.PressEscapeAsync();

        Assert.True(await ReadAsync<bool>(page, "() => window.__escaped"));
    }

    [Fact]
    public async Task 真实选择器能命中封面的_file_input()
    {
        // 这条单独存在的原因：选择器是从反编译串里还原的，写错一个属性和顺序就永远命不中。
        // 用「能不能真的注入成功」来验证，而不是用字符串相等来验证。
        var png = ImageFixtures.WritePng(Dir.Path, "probe.png", 820, 312);
        var page = await OpenPageAsync(FacebookMimicPage.CoverEditor());
        var interop = Interop(page);

        await interop.SetFileInputAsync(FacebookSelectors.CoverPhotoFileInput, png);

        Assert.Single(await ReadAsync<string[]>(page, "() => window.__uploads"));
    }
}
