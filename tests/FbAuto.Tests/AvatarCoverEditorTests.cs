using FbAuto.Automation.Media;
using FbAuto.Automation.Profiles;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 头像与背景图两项的纯逻辑部分。
/// </summary>
/// <remarks>
/// 用 <see cref="FakePageInterop"/> 跑，不起浏览器。它能证明的是**分支与顺序**：
/// 该跳过的有没有跳过、菜单没弹出来时有没有停手、被拒之后有没有重开入口再注第二张、
/// 保存按钮点不动时有没有报失败而不是当成功。
///
/// 它证明不了「选择器在真页面上能命中」—— 那是 <c>ProfileEditFlowIntegrationTests</c> 的事。
///
/// 等待参数一律压到几十毫秒：默认 15 秒的话，一条「元素缺失」的用例要跑满 15 秒。
/// </remarks>
public sealed class AvatarCoverEditorTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private static ProfileEditOptions Fast => new(
        ActionTimeout: TimeSpan.FromMilliseconds(150),
        SaveEnabledTimeout: TimeSpan.FromMilliseconds(150),
        SaveSettleTimeout: TimeSpan.FromMilliseconds(150),
        PollInterval: TimeSpan.FromMilliseconds(5),
        // 换图流程的「等拒绝提示」窗口，与上面几个同量级，否则被拒用例要白等 5 秒。
        ImageRejectionWindow: TimeSpan.FromMilliseconds(60));

    private const string ProfileId = "100012345678";

    public void Dispose() => _dir.Dispose();

    /// <summary>造一张合格封面图（820×312，刚好是公共主页封面的下限）。</summary>
    private string GoodCover() => ImageFixtures.WritePng(_dir.Path, $"cover-{Guid.NewGuid():N}.png", 820, 312);

    /// <summary>
    /// 让页面处于「头像入口齐全」的状态：头像区域可见、菜单可见、input 存在、保存按钮可见。
    /// </summary>
    private static FakePageInterop PageWithAvatarEntry(bool saveEnabled = true)
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.AvatarArea] = true;
        page.VisibleSelectors[FacebookSelectors.ChooseAvatarMenuItem] = true;
        page.HiddenButExisting.Add(FacebookSelectors.AvatarFileInput);
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = saveEnabled;

        // 点了保存，编辑面板就关掉 —— 「保存完成」在页面上唯一的可观测信号。
        page.OnClick = (selector, _) =>
        {
            if (selector == FacebookSelectors.SaveButton)
            {
                page.VisibleSelectors[FacebookSelectors.SaveButton] = false;
            }
        };

        return page;
    }

    // ── 头像 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task 头像_没有候选图时跳过且不碰页面()
    {
        var page = new FakePageInterop();

        var result = await AvatarEditor.RunAsync(page, ProfileId, [], Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Equal("已跳过【修改头像】", result.Message);
        Assert.Empty(page.NavigatedUrls);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 头像_找不到头像操作按钮时失败且不注入()
    {
        var page = new FakePageInterop();

        var result = await AvatarEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到【头像操作】按钮", result.Message);
        Assert.Empty(page.FileInjections);
    }

    [Fact]
    public async Task 头像_菜单没弹出来时失败且不注入()
    {
        var page = PageWithAvatarEntry();
        page.VisibleSelectors[FacebookSelectors.ChooseAvatarMenuItem] = false;

        var result = await AvatarEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到【选择头像】菜单项", result.Message);
        Assert.Empty(page.FileInjections);

        // 点了头像区域之后才发现菜单没弹 —— 这一行必须留在日志里，
        // 否则看日志的人分不清「没点」还是「点了没反应」。
        Assert.Contains(result.LogLines, x => x.Contains("点击头像区域"));
    }

    [Fact]
    public async Task 头像_上传照片的input没出现时失败()
    {
        var page = PageWithAvatarEntry();
        page.HiddenButExisting.Remove(FacebookSelectors.AvatarFileInput);

        var result = await AvatarEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到头像 file input", result.Message);
        Assert.Empty(page.FileInjections);
    }

    [Fact]
    public async Task 头像_正常流程按证据顺序点击并保存()
    {
        var page = PageWithAvatarEntry();
        var image = GoodCover();

        var result = await AvatarEditor.RunAsync(page, ProfileId, [image], Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);

        // 顺序就是证据里的顺序：先点头像区域，再点「选择头像」。
        Assert.Equal(
            [FacebookSelectors.AvatarArea, FacebookSelectors.ChooseAvatarMenuItem, FacebookSelectors.SaveButton],
            page.Clicked);

        var injection = Assert.Single(page.FileInjections);
        Assert.Equal(FacebookSelectors.AvatarFileInput, injection.Selector);
        Assert.Equal(image, injection.FilePath);

        Assert.Contains(result.LogLines, x => x.Contains("点击【保存】保存头像"));
        Assert.Contains(result.LogLines, x => x.Contains("头像已保存"));
    }

    [Fact]
    public async Task 头像_保存按钮一直不可用时报失败而不是当成功()
    {
        var page = PageWithAvatarEntry(saveEnabled: false);

        var result = await AvatarEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("保存按钮一直不可用", result.Message);

        // 图注进去了但没保存 —— 这种半成品状态最容易被误报成成功。
        Assert.Single(page.FileInjections);
    }

    /// <summary>
    /// 被页面拒一次之后，必须**重开头像菜单**再注第二张。
    /// </summary>
    /// <remarks>
    /// 这是这一步最容易做错的地方：头像是「点开对话框才有 file input」，
    /// 对话框在注入被拒后已经关了。若把「打开入口」写在循环外面，只会打开一次，
    /// 第二张图注进一个不存在的 input，表现为「重试了但每次都是同一个超时」。
    /// 断言「点了两次头像区域」才能钉住这件事 —— 只数注入次数是看不出来的。
    /// </remarks>
    [Fact]
    public async Task 头像_被拒之后重开菜单再注第二张()
    {
        var page = PageWithAvatarEntry();
        var first = GoodCover();
        var second = GoodCover();

        // 每次注入都弹拒绝提示；点「关闭」让它消失。
        page.VisibleSelectors[CoverRejectionSignals.CloseButtonSelector] = true;
        page.OnInject = (_, _) => page.VisibleSelectors[CoverRejectionSignals.RejectedDialogSelector] = true;
        page.OnClick = (selector, _) =>
        {
            if (selector == FacebookSelectors.SaveButton)
            {
                page.VisibleSelectors[FacebookSelectors.SaveButton] = false;
            }
            else if (selector == CoverRejectionSignals.CloseButtonSelector)
            {
                page.VisibleSelectors[CoverRejectionSignals.RejectedDialogSelector] = false;
            }
        };

        var result = await AvatarEditor.RunAsync(page, ProfileId, [first, second], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("2 张封面图被页面拒绝", result.Message);

        // 两张都注入了，且每次注入前都重新点开了头像区域。
        Assert.Equal(2, page.FileInjections.Count);
        Assert.Equal(2, page.Clicked.Count(x => x == FacebookSelectors.AvatarArea));
    }

    [Fact]
    public async Task 头像_已取消时抛取消异常()
    {
        var page = PageWithAvatarEntry();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AvatarEditor.RunAsync(page, ProfileId, [GoodCover()], Fast, cts.Token));

        Assert.Empty(page.FileInjections);
    }

    // ── 背景图 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task 背景图_没有候选图时跳过且不碰页面()
    {
        var page = new FakePageInterop();

        var result = await CoverPhotoEditor.RunAsync(page, ProfileId, null, Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Equal("已跳过【修改背景图】", result.Message);
        Assert.Empty(page.NavigatedUrls);
    }

    [Fact]
    public async Task 背景图_找不到file_input时失败且不注入()
    {
        var page = new FakePageInterop();

        var result = await CoverPhotoEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到背景图 file input", result.Message);
        Assert.Empty(page.FileInjections);
    }

    [Fact]
    public async Task 背景图_全部被拒时不点保存更改()
    {
        var page = new FakePageInterop();
        page.HiddenButExisting.Add(FacebookSelectors.CoverPhotoFileInput);
        page.VisibleSelectors[FacebookSelectors.SaveChangesButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveChangesButton] = true;
        page.VisibleSelectors[CoverRejectionSignals.CloseButtonSelector] = true;
        page.OnInject = (_, _) => page.VisibleSelectors[CoverRejectionSignals.RejectedDialogSelector] = true;
        page.OnClick = (selector, _) =>
        {
            if (selector == CoverRejectionSignals.CloseButtonSelector)
            {
                page.VisibleSelectors[CoverRejectionSignals.RejectedDialogSelector] = false;
            }
        };

        var result = await CoverPhotoEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);

        // 一张都没被接受就点「保存更改」，等于把旧图重新保存一遍 —— 不允许。
        Assert.DoesNotContain(FacebookSelectors.SaveChangesButton, page.Clicked);
    }

    [Fact]
    public async Task 背景图_正常流程注入后点保存更改()
    {
        var page = new FakePageInterop();
        page.HiddenButExisting.Add(FacebookSelectors.CoverPhotoFileInput);
        page.VisibleSelectors[FacebookSelectors.SaveChangesButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveChangesButton] = true;
        page.OnClick = (selector, _) =>
        {
            if (selector == FacebookSelectors.SaveChangesButton)
            {
                page.VisibleSelectors[FacebookSelectors.SaveChangesButton] = false;
            }
        };

        var image = GoodCover();
        var result = await CoverPhotoEditor.RunAsync(page, ProfileId, [image], Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal([FacebookSelectors.SaveChangesButton], page.Clicked);

        var injection = Assert.Single(page.FileInjections);
        Assert.Equal(FacebookSelectors.CoverPhotoFileInput, injection.Selector);
        Assert.Equal(image, injection.FilePath);

        Assert.Contains(result.LogLines, x => x.Contains("点击【保存更改】保存背景图"));
        Assert.Contains(result.LogLines, x => x.Contains("背景图已保存"));
    }

    [Fact]
    public async Task 背景图_保存更改按钮一直不可用时报失败()
    {
        var page = new FakePageInterop();
        page.HiddenButExisting.Add(FacebookSelectors.CoverPhotoFileInput);
        page.VisibleSelectors[FacebookSelectors.SaveChangesButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveChangesButton] = false;

        var result = await CoverPhotoEditor.RunAsync(page, ProfileId, [GoodCover()], Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("保存更改按钮一直不可用", result.Message);
    }

    [Fact]
    public async Task 背景图_已取消时抛取消异常()
    {
        var page = new FakePageInterop();
        page.HiddenButExisting.Add(FacebookSelectors.CoverPhotoFileInput);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CoverPhotoEditor.RunAsync(page, ProfileId, [GoodCover()], Fast, cts.Token));

        Assert.Empty(page.FileInjections);
    }

    // ── 编排 ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task 编排_五项顺序与原版日志一致()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(string.Empty, string.Empty, UpdatePostingSettings: false),
            profileId: ProfileId,
            options: Fast);

        Assert.Equal(
            ["头像", "背景图", "发帖设置", "个性签名", "类别"],
            result.Fields.Select(f => f.Field).ToArray());

        // 跳过也要留一行日志，否则日志里从「改完了」直接跳到结果，看不出这两项被跳过了。
        Assert.Contains(result.LogLines, x => x == AvatarEditor.SkipLine);
        Assert.Contains(result.LogLines, x => x == CoverPhotoEditor.SkipLine);
    }

    [Fact]
    public void 请求_没传图片时Images为空集合而不是null()
    {
        Assert.Empty(new ProfileEditRequest().Images);
        Assert.Empty(new ProfileEditRequest("签名").Images);
        Assert.Single(new ProfileEditRequest(ImageCandidates: ["a.png"]).Images);
    }
}
