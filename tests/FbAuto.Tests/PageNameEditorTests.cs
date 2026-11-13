using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Profiles;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 主页名字的纯逻辑部分（iframe + 主账号密码二次确认）。
/// </summary>
/// <remarks>
/// 这一项与其余六项最大的差别是**跨 frame**：表单在 cquick 那个 iframe 里，
/// 密码框与确认按钮回到主 frame。所以用例的重点不只是「每条失败分支有没有日志」，
/// 还有「两套原语有没有串用」——串用的表现是「未找到密码输入框」，
/// 流程随即以为没配密码，直接去点确认，日志看着一切正常。
///
/// 等待参数压到百毫秒级，理由同其余资料修改用例。
/// </remarks>
public sealed class PageNameEditorTests
{
    private const string Name = "新主页名";

    private static ProfileEditOptions Fast => new(
        ActionTimeout: TimeSpan.FromMilliseconds(200),
        SaveEnabledTimeout: TimeSpan.FromMilliseconds(200),
        SaveSettleTimeout: TimeSpan.FromMilliseconds(200),
        PollInterval: TimeSpan.FromMilliseconds(5));

    /// <summary>一个「该有的都有」的页面：cquick frame + 表单 + 名字框 + 预览按钮。</summary>
    private static FakePageInterop PageWithFrame()
    {
        var page = new FakePageInterop();
        page.Frames.Add(new FrameRef(0, "https://www.facebook.com/settings?tab=profile&section=name&view"));
        page.Frames.Add(new FrameRef(1, "https://www.facebook.com/cquick/name_change/?ref=settings"));
        page.FrameSelectors.Add(FacebookSelectors.NameForm);
        page.FrameSelectors.Add(FacebookSelectors.NameInput);
        page.FrameSelectors.Add(FacebookSelectors.NamePreviewButton);
        return page;
    }

    [Fact]
    public async Task 没有名字时跳过且不碰页面()
    {
        var page = new FakePageInterop();

        var result = await PageNameEditor.RunAsync(page, "   ", "pw", Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Contains(PageNameEditor.SkipLine, result.LogLines);
        Assert.Empty(page.NavigatedUrls);
        Assert.Empty(page.FrameFilled);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 找不到cquick的frame时失败且把frame清单写进日志()
    {
        var page = new FakePageInterop();
        page.Frames.Add(new FrameRef(0, "https://www.facebook.com/settings?tab=profile"));
        page.Frames.Add(new FrameRef(1, "https://www.facebook.com/other/thing"));

        var result = await PageNameEditor.RunAsync(page, Name, "pw", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains(PageNameEditor.NoFrameLine, result.LogLines);

        // 清单必须打出来：只有一句「未找到」时分不清「iframe 没挂上」和「地址变了」。
        Assert.Contains("当前共有 2 个 frame:", result.LogLines);
        Assert.Contains(result.LogLines, x => x.Contains("frame[1] url=https://www.facebook.com/other/thing"));
    }

    [Fact]
    public async Task frame后挂上来时等一下仍能成功()
    {
        var page = PageWithFrame();
        var stable = page.Frames.ToArray();
        var calls = 0;

        // 第一次读到空清单，之后才含 cquick —— 真实的设置页就是这样，先出壳再挂 iframe。
        page.FramesProvider = () => ++calls == 1 ? [] : stable;

        page.OnFrameClick = (_, _, _) =>
        {
            page.VisibleSelectors[FacebookSelectors.NamePasswordInput] = true;
            page.VisibleSelectors[FacebookSelectors.NameConfirmButton] = true;
        };

        var result = await PageNameEditor.RunAsync(page, Name, "pw", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.True(calls > 1, "应当重新读过 frame 清单；只读一次说明没有等待");
    }

    [Fact]
    public async Task frame里没有表单时失败()
    {
        var page = PageWithFrame();
        page.FrameSelectors.Remove(FacebookSelectors.NameForm);

        var result = await PageNameEditor.RunAsync(page, Name, "pw", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains(PageNameEditor.NoFormLine, result.LogLines);
        Assert.Empty(page.FrameFilled);
    }

    [Fact]
    public async Task frame里有表单但没有名字输入框时失败()
    {
        var page = PageWithFrame();
        page.FrameSelectors.Remove(FacebookSelectors.NameInput);

        var result = await PageNameEditor.RunAsync(page, Name, "pw", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains(PageNameEditor.NoInputLine, result.LogLines);
        Assert.Empty(page.FrameFilled);
    }

    [Fact]
    public async Task 没有预览按钮时失败且不再点确认()
    {
        var page = PageWithFrame();
        page.FrameSelectors.Remove(FacebookSelectors.NamePreviewButton);
        page.VisibleSelectors[FacebookSelectors.NameConfirmButton] = true;

        var result = await PageNameEditor.RunAsync(page, Name, "pw", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到【预览修改】按钮", result.LogLines);

        // 表单没提交就没有确认框可点。这里若照点，日志会变成「已点击【申请更改】」，
        // 把「预览这步就没成」这个真实原因藏起来。
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 成功路径_先填名字再点预览最后输密码确认()
    {
        var page = PageWithFrame();

        // 密码框与确认按钮只在预览提交之后才出现 —— 顺序错了就抓不到它们。
        page.OnFrameClick = (_, _, _) =>
        {
            page.VisibleSelectors[FacebookSelectors.NamePasswordInput] = true;
            page.VisibleSelectors[FacebookSelectors.NameConfirmButton] = true;
        };

        var result = await PageNameEditor.RunAsync(page, "  " + Name + "  ", "s3cret", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains(Name, result.Message);

        // 名字被裁掉首尾空白再填。
        var filled = Assert.Single(page.FrameFilled);
        Assert.Equal(FacebookSelectors.NameInput, filled.Selector);
        Assert.Equal(Name, filled.Text);

        // 预览按钮点的是 frame 内的那个，不是主 frame。
        var preview = Assert.Single(page.FrameClicked);
        Assert.Equal(FacebookSelectors.NamePreviewButton, preview.Selector);

        // 密码在**主 frame** 里填：用 Frame* 原语会填不进去。
        var password = Assert.Single(page.Filled);
        Assert.Equal(FacebookSelectors.NamePasswordInput, password.Selector);
        Assert.Equal("s3cret", password.Text);

        Assert.Equal(new[] { FacebookSelectors.NameConfirmButton }, page.Clicked);
        Assert.Contains("名字已填入", result.LogLines);
        Assert.Contains("已点击【预览修改】", result.LogLines);
        Assert.Contains("输入密码...", result.LogLines);
        Assert.Contains("已点击【申请更改】", result.LogLines);
    }

    [Fact]
    public async Task 未配密码时跳过输入但结论要说清可能没生效()
    {
        var page = PageWithFrame();
        page.VisibleSelectors[FacebookSelectors.NamePasswordInput] = true;
        page.VisibleSelectors[FacebookSelectors.NameConfirmButton] = true;

        var result = await PageNameEditor.RunAsync(page, Name, string.Empty, Fast);

        Assert.Contains("未配置密码，跳过", result.LogLines);
        Assert.Empty(page.Filled);
        Assert.Equal(new[] { FacebookSelectors.NameConfirmButton }, page.Clicked);

        // 不报成干净的「已修改」：没有密码，FB 侧多半不会真的改，
        // 结果列必须让人看到「需人工确认」。
        Assert.Contains("需人工确认", result.Message);
    }

    [Fact]
    public async Task 没有密码框时只记一行并继续点确认()
    {
        var page = PageWithFrame();
        page.VisibleSelectors[FacebookSelectors.NameConfirmButton] = true;

        var result = await PageNameEditor.RunAsync(page, Name, "s3cret", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains("未找到密码输入框", result.LogLines);
        Assert.Empty(page.Filled);
        Assert.Equal(new[] { FacebookSelectors.NameConfirmButton }, page.Clicked);
    }

    [Fact]
    public async Task 没有确认按钮时失败()
    {
        var page = PageWithFrame();
        page.VisibleSelectors[FacebookSelectors.NamePasswordInput] = true;

        var result = await PageNameEditor.RunAsync(page, Name, "s3cret", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到【申请更改】按钮", result.LogLines);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 取消时抛出而不是当成失败()
    {
        var page = PageWithFrame();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => PageNameEditor.RunAsync(page, Name, "pw", Fast, cts.Token));
    }

    [Fact]
    public void frame匹配按URL片段且忽略大小写()
    {
        var frame = new FrameRef(3, "https://www.facebook.com/CQuick/name/?a=1");

        Assert.True(frame.Matches("cquick"));
        Assert.False(frame.Matches("name_change_preview"));
        Assert.False(frame.Matches(string.Empty));
    }
}
