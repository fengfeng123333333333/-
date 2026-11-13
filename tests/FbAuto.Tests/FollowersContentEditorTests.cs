using FbAuto.Automation.Profiles;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 粉丝公开内容（屏蔽关键词）的纯逻辑部分。
/// </summary>
/// <remarks>
/// 关键点只有一个，但它不写用例就一定会漏：**每个词都要按一次回车**。
/// 标签框只填不回车，词会留在输入框里、保存后一起丢掉，而日志上「已添加屏蔽词: x」照样打得出来 ——
/// 所以这里不断言日志，直接数 <c>PressedKeys</c>。
///
/// 等待参数压到几十毫秒，理由同其余资料修改用例。
/// </remarks>
public sealed class FollowersContentEditorTests
{
    private static ProfileEditOptions Fast => new(
        ActionTimeout: TimeSpan.FromMilliseconds(150),
        SaveEnabledTimeout: TimeSpan.FromMilliseconds(150),
        SaveSettleTimeout: TimeSpan.FromMilliseconds(150),
        PollInterval: TimeSpan.FromMilliseconds(5));

    private static FakePageInterop PageWithInput(bool saveButton = true)
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.FollowersKeywordInput] = true;
        page.EnabledSelectors[FacebookSelectors.FollowersKeywordInput] = true;
        if (saveButton)
        {
            page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
            page.EnabledSelectors[FacebookSelectors.SaveButton] = true;
        }

        return page;
    }

    [Fact]
    public async Task 没有屏蔽词时跳过且不碰页面()
    {
        var page = new FakePageInterop();

        var result = await FollowersContentEditor.RunAsync(page, [], Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Empty(page.NavigatedUrls);
        Assert.Empty(page.Filled);
        Assert.Empty(page.PressedKeys);
    }

    [Fact]
    public async Task 空白词不算数()
    {
        var page = new FakePageInterop();

        var result = await FollowersContentEditor.RunAsync(page, ["", "   ", null!], Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Empty(page.NavigatedUrls);
    }

    /// <summary>
    /// 同一个词配了两遍只敲一次 —— 重复敲会在页面上生成两个一样的标签。
    /// </summary>
    [Fact]
    public async Task 重复词只敲一次()
    {
        var page = PageWithInput();

        var result = await FollowersContentEditor.RunAsync(page, ["spam", "SPAM", "spam"], Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Single(page.Filled);
    }

    [Fact]
    public async Task 先导航到粉丝公开内容页()
    {
        var page = PageWithInput();

        await FollowersContentEditor.RunAsync(page, ["spam"], Fast);

        Assert.Equal(FacebookUrls.FollowersContent, Assert.Single(page.NavigatedUrls));
    }

    /// <summary>
    /// 每个词一次回车。少按一次不会报错，只是少一个词生效，所以必须精确数。
    /// </summary>
    [Fact]
    public async Task 每个词都填一次并按一次回车()
    {
        var page = PageWithInput();
        string[] words = [".com", "https", "pn"];

        var result = await FollowersContentEditor.RunAsync(page, words, Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal(words, page.Filled.Select(f => f.Text));
        Assert.Equal(words.Length, page.PressedKeys.Count);
        Assert.All(page.PressedKeys, k => Assert.Equal("Enter", k.Key));
        Assert.All(page.PressedKeys, k => Assert.Equal(FacebookSelectors.FollowersKeywordInput, k.Selector));

        // 先后也要对：必须是「填一个 → 回车 → 填下一个 → 回车」。
        // 先回车后填的话，填进去的那个词留在框里没变成标签，而两个表的长度一模一样 ——
        // 光数次数看不出来，必须看交错。
        Assert.Equal(
            ["fill:.com", "press:Enter", "fill:https", "press:Enter", "fill:pn", "press:Enter"],
            page.Timeline);
    }

    /// <summary>
    /// 输入框找不到是「跳过关键词设置」，不是整项失败。
    /// </summary>
    /// <remarks>
    /// 原版串就是「未找到关键词输入框，跳过关键词设置」，而且它之后还能看到
    /// 「粉丝公开内容设置完成」—— 说明原版没把它当失败。
    /// 若这里判失败，整条资料修改会汇总成「有 1 项失败」，实际上只是少配了几个词。
    /// </remarks>
    [Fact]
    public async Task 找不到关键词输入框时跳过而不是失败()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;

        var result = await FollowersContentEditor.RunAsync(page, ["spam"], Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Equal(FollowersContentEditor.NoInputLine, result.Message);
        Assert.Empty(page.PressedKeys);
        Assert.DoesNotContain(FacebookSelectors.SaveButton, page.Clicked);
    }

    [Fact]
    public async Task 保存按钮存在时点它()
    {
        var page = PageWithInput();

        var result = await FollowersContentEditor.RunAsync(page, ["spam"], Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains(FacebookSelectors.SaveButton, page.Clicked);
        Assert.Equal(0, page.EscapeCount);
        Assert.Contains("已点击保存", result.LogLines);
    }

    /// <summary>
    /// 没有保存按钮就按 Escape 关面板，**不当失败**。
    /// </summary>
    /// <remarks>
    /// 依据是原版串的顺序：「未找到保存按钮，尝试按 Escape 关闭...」后面紧跟
    /// 「粉丝公开内容设置完成」。标签是即时生效的，保存按钮只关面板。
    /// </remarks>
    [Fact]
    public async Task 没有保存按钮时按Escape并仍算成功()
    {
        var page = PageWithInput(saveButton: false);

        var result = await FollowersContentEditor.RunAsync(page, ["spam"], Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal(1, page.EscapeCount);
        Assert.Contains("未找到保存按钮，尝试按 Escape 关闭...", result.LogLines);
        Assert.Contains("粉丝公开内容设置完成", result.LogLines);
    }

    [Fact]
    public async Task 已取消时抛取消异常()
    {
        var page = PageWithInput();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FollowersContentEditor.RunAsync(page, ["spam"], Fast, cts.Token));
    }
}
