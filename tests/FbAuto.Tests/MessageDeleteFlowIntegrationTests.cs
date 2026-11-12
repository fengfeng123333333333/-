using FbAuto.Automation.Messages;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 删除消息在**真 Chrome** 里跑：四步序列能不能落地、退出条件在真 DOM 上是不是同样成立。
/// </summary>
/// <remarks>
/// 和 <see cref="MessageDeleteTests"/> 的分工：那边证明「什么时候必须停」（纯逻辑），
/// 这边证明「停之前那几步真能点下去」（选择器 + 动作 + 时序）。
///
/// **这一组证明不了「FB 的 Messenger 现在就是这个 DOM」** —— 原版没有这条流程的页面串，
/// 仿真页是我们自己按 FB 惯例搭的。真机验收要重新确认四条选择器。
///
/// 必须同属 <c>[Collection("chrome")]</c>：漏一个就会出现「单独跑全绿、全量跑零输出挂死」。
/// </remarks>
[Collection("chrome")]
public sealed class MessageDeleteFlowIntegrationTests : ChromePageTestBase
{
    [Fact]
    public async Task 删满上限就停在真页面上同样成立()
    {
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);
        var url = UrlOf(FacebookMimicPage.MessengerInbox(conversationCount: 4));

        var result = await MessageDeleteFlow.RunAsync(interop, OptionsFor(url, limit: 2));

        Assert.True(result.Success);
        Assert.Equal(2, result.Deleted);
        Assert.Equal(MessageDeleteStop.LimitReached, result.Stop);

        // 页面自己记的删除次数与剩余行数都要对得上 —— 只看流程返回值会被「假成功」骗过去。
        Assert.Equal(2, await ReadAsync<int>(page, "() => window.__deleted || 0"));
        Assert.Equal(2, await page.Locator(FacebookSelectors.ConversationRow).CountAsync());
    }

    [Fact]
    public async Task 列表满是空的时候一次都不点()
    {
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);
        var url = UrlOf(FacebookMimicPage.MessengerInbox(conversationCount: 0));

        var result = await MessageDeleteFlow.RunAsync(interop, OptionsFor(url, limit: 20));

        Assert.True(result.Success);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(MessageDeleteStop.ListEmpty, result.Stop);
        Assert.Equal(0, await ReadAsync<int>(page, "() => window.__confirmed || 0"));
    }

    /// <summary>
    /// 详情入口找不到时，连续尝试 3 次后中止，且判为失败。
    /// </summary>
    /// <remarks>
    /// 这条盯的是「页面改版」这个最常见的故障：此时报成功比报失败危险得多，
    /// 因为使用者看到「删除完成 0 条」会以为号里本来就没有消息。
    /// </remarks>
    [Fact]
    public async Task 详情入口缺失时连续失败后中止()
    {
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);
        var url = UrlOf(FacebookMimicPage.MessengerInbox(conversationCount: 3, withInfoButton: false));

        var result = await MessageDeleteFlow.RunAsync(interop, OptionsFor(url, limit: 20));

        Assert.False(result.Success);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(3, result.Failed);
        Assert.Equal(MessageDeleteStop.TooManyFailures, result.Stop);
        Assert.Contains(result.LogLines, x => x.Contains("打开会话详情失败", StringComparison.Ordinal));
    }

    /// <summary>
    /// 确认点了但列表没变：不算失败，但日志必须留痕。
    /// </summary>
    /// <remarks>
    /// 真机上这对应「对方已经删了这段对话」或「点了没生效」。
    /// 不能当失败 —— 虚拟滚动列表下数量本来就不降；也不能当成功 —— 那也是没删掉。
    /// 所以选择「记成功 + 写一句未下降」，让人能查到。
    /// </remarks>
    [Fact]
    public async Task 确认后列表没变时日志写明未下降()
    {
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);
        var url = UrlOf(FacebookMimicPage.MessengerInbox(conversationCount: 3, confirmRemoves: false));

        var result = await MessageDeleteFlow.RunAsync(interop, OptionsFor(url, limit: 1));

        Assert.True(result.Success);
        Assert.Equal(1, result.Deleted);
        Assert.Contains(result.LogLines, x => x.Contains("未下降", StringComparison.Ordinal));
        Assert.Equal(1, await ReadAsync<int>(page, "() => window.__confirmed || 0"));
    }

    private static MessageDeleteOptions OptionsFor(string url, int limit) => new(
        Limit: limit,
        ActionTimeout: TimeSpan.FromSeconds(4),
        ConfirmTimeout: TimeSpan.FromSeconds(4),
        RefreshTimeout: TimeSpan.FromSeconds(3),
        PollInterval: TimeSpan.FromMilliseconds(40),
        InboxUrl: url);

    private string UrlOf(string html)
    {
        var path = FacebookMimicPage.Write(Dir.Path, "messenger.html", html);
        return new Uri(path).AbsoluteUri;
    }
}
