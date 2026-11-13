using FbAuto.Automation.Pages;
using FbAuto.Automation.Profiles;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 资料修改在真 Chrome 里跑。页面是本地仿真页（<see cref="FacebookMimicPage.ProfileSettings"/>），
/// 选择器与产品代码共用同一份常量，三次导航都真的执行。
/// </summary>
/// <remarks>
/// 这一组证明的是「动作与选择器在真浏览器里成立」：填得进 textarea、等得到按钮解禁、
/// 按文本拼的类别选择器真的命中、`aria-disabled` 的按钮不会把点击拖成超时。
/// **它证明不了「Facebook 现在还是这个 DOM」** —— 那要等出口通了真机验。
///
/// 地址全部走 <see cref="ProfileEditOptions"/> 的模板参数指向 <c>file://</c>，
/// 所以流程里「导航到签名页 / 类别页 / 设置页」这三段没有一段被跳过。
/// </remarks>
[Collection("chrome")]
public sealed class ProfileEditFlowIntegrationTests : ChromePageTestBase
{
    private static string Base(string path) => new Uri(path).AbsoluteUri;

    /// <summary>写一份仿真页，返回指向它的调参。</summary>
    private ProfileEditOptions OptionsFor(string path) => new(
        ActionTimeout: TimeSpan.FromSeconds(4),
        SaveEnabledTimeout: TimeSpan.FromSeconds(4),
        PollInterval: TimeSpan.FromMilliseconds(20),
        IntroUrlTemplate: Base(path) + "?id={0}&sk=directory_intro",
        CategoryUrlTemplate: Base(path) + "?id={0}&sk=directory_category",
        PostingSettingsUrl: Base(path) + "?tab=profile_and_tagging");

    private string WritePage(string html, string fileName) =>
        FacebookMimicPage.Write(Dir.Path, fileName, html);

    [Fact]
    public async Task 三项全改成功()
    {
        var path = WritePage(FacebookMimicPage.ProfileSettings(), "profile.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await ProfileEditFlow.RunAsync(
            interop,
            new ProfileEditRequest("简介一句话", "教育", UpdatePostingSettings: true),
            profileId: "100012345678",
            options: OptionsFor(path));

        Assert.True(result.Success, string.Join("\n", result.LogLines));
        Assert.Equal(3, result.ChangedCount);

        // 收尾停在**类别页**（S11 把顺序调成原版日志块：头像→背景图→发帖设置→签名→类别），
        // 前三项的状态已经随那几次导航丢了 —— 它们的写入结果由日志与各自单独的用例证明。
        Assert.Equal("教育", await ReadAsync<string>(page, "() => window.__category"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__categorySaved"));

        Assert.Contains(result.LogLines, x => x.Contains("签名已输入: 简介一句话"));
        Assert.Contains(result.LogLines, x => x.Contains("已选择类别: 教育"));
    }

    [Fact]
    public async Task 已有签名时走编辑入口并保存()
    {
        var path = WritePage(FacebookMimicPage.ProfileSettings(), "profile-edit.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await SignatureEditor.RunAsync(
            interop,
            "100012345678",
            "新的个性签名",
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal("sig-edit", await ReadAsync<string>(page, "() => window.__entry"));
        Assert.Equal("新的个性签名", await ReadAsync<string>(page, "() => window.__signature"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__signatureSaved"));
    }

    /// <summary>
    /// 没有签名时页面上根本没有 <c>[aria-label='编辑个性签名']</c>，只有「关于你」。
    /// 走错入口的表现是「等一个不存在的按钮，等到超时」。
    /// </summary>
    [Fact]
    public async Task 没有签名时走关于你入口()
    {
        var path = WritePage(
            FacebookMimicPage.ProfileSettings(hasExistingSignature: false),
            "profile-empty.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await SignatureEditor.RunAsync(
            interop,
            "100012345678",
            "第一次填签名",
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal("sig-about", await ReadAsync<string>(page, "() => window.__entry"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__signatureSaved"));
    }

    /// <summary>
    /// 保存按钮在填完内容后还要过一会儿才解禁。直接点会一直等到点击超时，
    /// 日志里只剩一句英文 Timeout。这条用例就是压这个时序的。
    /// </summary>
    [Fact]
    public async Task 保存按钮延迟解禁仍能等到并点下去()
    {
        var path = WritePage(
            FacebookMimicPage.ProfileSettings(saveEnableDelayMilliseconds: 500),
            "profile-slow-save.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await SignatureEditor.RunAsync(
            interop,
            "100012345678",
            "慢一点也要存上",
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.True(
            await ReadAsync<bool>(page, "() => window.__signatureSaved"),
            string.Join("\n", result.LogLines));
        Assert.Contains(result.LogLines, x => x.Contains("已点击保存"));
    }

    /// <summary>
    /// 签名框旁边没有保存按钮时按自动保存处理，不能判失败。
    /// </summary>
    [Fact]
    public async Task 没有保存按钮仍算改成功()
    {
        var path = WritePage(
            FacebookMimicPage.ProfileSettings(withSignatureSaveButton: false),
            "profile-autosave.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await SignatureEditor.RunAsync(
            interop,
            "100012345678",
            "自动保存的签名",
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains(result.LogLines, x => x.Contains("自动保存"));
        Assert.Equal("自动保存的签名", await ReadAsync<string>(page, "() => window.__signature"));
    }

    [Fact]
    public async Task 类别能选到目标选项并保存()
    {
        var path = WritePage(FacebookMimicPage.ProfileSettings(), "profile-category.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await CategoryEditor.RunAsync(
            interop,
            "100012345678",
            "教育",
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal("教育", await ReadAsync<string>(page, "() => window.__category"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__categorySaved"));
    }

    /// <summary>
    /// 当前类别已经是目标时不做任何改动。
    /// </summary>
    /// <remarks>
    /// 这条压的是「先判当前类别再动手」的顺序。顺序反了（或判定用错选择器）时，
    /// 这里会变成「又点了一遍编辑、又搜了一遍、又保存了一次」—— 页面状态看不出来，
    /// 但对 FB 来说那是三次多余动作。
    /// </remarks>
    [Fact]
    public async Task 当前类别已是目标时不点也不保存()
    {
        var path = WritePage(
            FacebookMimicPage.ProfileSettings(currentCategory: "教育"),
            "profile-category-same.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await CategoryEditor.RunAsync(
            interop,
            "100012345678",
            "教育",
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Null(await ReadAsync<string>(page, "() => window.__category"));
        Assert.False(await ReadAsync<bool>(page, "() => window.__categorySaved"));
    }

    /// <summary>
    /// 类别名里带双引号时，拼出来的选择器必须仍然合法。
    /// </summary>
    /// <remarks>
    /// 不转义会得到 <c>div[dir='auto']:has-text("说"唱"")</c> —— 语法错误的选择器，
    /// Playwright 报的是「选择器无效」，往类别值上想很难。这条用例是转义那两行的守卫。
    /// </remarks>
    [Fact]
    public async Task 类别名带双引号也能命中()
    {
        const string quoted = "说\"唱\"";

        var path = WritePage(
            FacebookMimicPage.ProfileSettings(categories: ["教育", quoted]),
            "profile-quote.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await CategoryEditor.RunAsync(
            interop,
            "100012345678",
            quoted,
            OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal(quoted, await ReadAsync<string>(page, "() => window.__category"));
    }

    [Fact]
    public async Task 发帖设置改为仅限自己并保存()
    {
        var path = WritePage(FacebookMimicPage.ProfileSettings(), "profile-posting.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await PostingSettingsEditor.RunAsync(interop, OptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal("仅限自己", await ReadAsync<string>(page, "() => window.__posting"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__postingSaved"));
    }

    /// <summary>
    /// 签名一项失败时，后面两项照跑（原版「修改签名失败(继续下一步)」的语义）。
    /// </summary>
    [Fact]
    public async Task 签名找不到入口时后两项照跑()
    {
        var path = WritePage(
            FacebookMimicPage.ProfileSettings(withSignatureEntry: false),
            "profile-nosig.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await ProfileEditFlow.RunAsync(
            interop,
            new ProfileEditRequest("签名", "教育", UpdatePostingSettings: true),
            profileId: "100012345678",
            options: OptionsFor(path));

        Assert.False(result.Success);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, result.ChangedCount);
        Assert.Contains("个性签名", result.Summary);

        // 最后确实走到了类别页并改成功 —— 说明第一项失败没有把流程掐掉。
        // 类别现在是最后一项，所以这条路也顺带证明了「失败项之后的每一项都跑到了」。
        Assert.True(await ReadAsync<bool>(page, "() => window.__categorySaved"));
    }

    /// <summary>
    /// profile id 的「打开主页 → 读整页 → 正则提取」要在真浏览器里跑一遍。
    /// </summary>
    [Fact]
    public async Task 从主页解析profile_id后三项照改()
    {
        var path = WritePage(FacebookMimicPage.ProfileSettings(), "profile-resolve.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var result = await ProfileEditFlow.RunAsync(
            interop,
            new ProfileEditRequest("签名", "教育", UpdatePostingSettings: true),
            options: OptionsFor(path),
            homeUrl: Base(path));

        Assert.True(result.Success, string.Join("\n", result.LogLines));
        Assert.Contains(result.LogLines, x => x.Contains("已获取主页 profile id: 100012345678"));
    }

    [Fact]
    public async Task 主页没有profile链接时整条失败且不碰设置页()
    {
        var login = WritePage(
            """
            <!DOCTYPE html>
            <html lang="zh-CN"><body><div>登录 Facebook</div></body></html>
            """,
            "login.html");
        var path = WritePage(FacebookMimicPage.ProfileSettings(), "profile-guard.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var options = OptionsFor(path);
        var result = await ProfileEditFlow.RunAsync(
            interop,
            new ProfileEditRequest("签名", "教育", UpdatePostingSettings: true),
            options: options,
            homeUrl: Base(login));

        Assert.False(result.Success);
        Assert.Contains("没有找到主页 profile id", result.Summary);

        // 停在登录页上：没有 id 就不该去拼签名页/类别页的地址。
        Assert.DoesNotContain("directory_intro", page.Url);
        Assert.DoesNotContain("profile_and_tagging", page.Url);
    }

    // ── 头像与背景图 ─────────────────────────────────────────────────────

    /// <summary>指向仿主页的调参：头像与背景图都落在这一页。</summary>
    private ProfileEditOptions HomeOptionsFor(string path) => new(
        ActionTimeout: TimeSpan.FromSeconds(4),
        SaveEnabledTimeout: TimeSpan.FromSeconds(4),
        SaveSettleTimeout: TimeSpan.FromSeconds(4),
        ImageRejectionWindow: TimeSpan.FromMilliseconds(600),
        PollInterval: TimeSpan.FromMilliseconds(20),
        ProfileHomeUrlTemplate: Base(path) + "?id={0}");

    /// <summary>
    /// 头像全链路：点头像区域 → 菜单 → 上传照片的 file input → 注入 → 保存 → 面板关闭。
    /// </summary>
    /// <remarks>
    /// 重点不在「注入成功」，而在**那个 input 是点两步之后才被创建出来的**。
    /// 若产品代码省掉「点选择头像」，注入会落到一个不存在的 input 上；
    /// 这个仿真页会把这件事暴露成「文件没进 __avatarUploads」而不是静默通过。
    /// </remarks>
    [Fact]
    public async Task 头像走完点开菜单注入保存三步()
    {
        var path = WritePage(FacebookMimicPage.ProfileHome(), "home-avatar.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var avatar = ImageFixtures.WritePng(Dir.Path, "avatar.png", 400, 400);

        var result = await AvatarEditor.RunAsync(
            interop,
            "100012345678",
            [avatar],
            HomeOptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);

        var uploads = await ReadAsync<string[]>(page, "() => window.__avatarUploads");
        Assert.Equal(["avatar.png"], uploads);
        Assert.True(await ReadAsync<bool>(page, "() => window.__avatarSaved"));
    }

    /// <summary>
    /// 背景图被拒一次之后换下一张，最终保存。
    /// </summary>
    /// <remarks>
    /// 换下一张不只是「再注入一次」：拒绝对话框必须先关掉，否则下一张一注入就又被判成
    /// 「刚上传就被拒」。这条用例把「关提示 → 注入第二张」这条顺序也压住了。
    /// </remarks>
    [Fact]
    public async Task 背景图被拒后换下一张并保存()
    {
        var path = WritePage(FacebookMimicPage.ProfileHome(), "home-cover.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var rejected = ImageFixtures.WritePng(Dir.Path, "reject-1.png", 820, 312);
        var good = ImageFixtures.WritePng(Dir.Path, "good-1.png", 820, 312);

        var result = await CoverPhotoEditor.RunAsync(
            interop,
            "100012345678",
            [rejected, good],
            HomeOptionsFor(path));

        Assert.Equal(FieldEditStatus.Changed, result.Status);

        var uploads = await ReadAsync<string[]>(page, "() => window.__coverUploads");
        Assert.Equal(["reject-1.png", "good-1.png"], uploads);
        Assert.Equal("good-1.png", await ReadAsync<string>(page, "() => window.__coverAccepted"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__coverSaved"));

        // 拒绝对话框真的被点关闭了，而不是靠别的方式糊过去。
        Assert.Equal(1, await ReadAsync<int>(page, "() => window.__rejectClosed"));
    }

    /// <summary>
    /// 页面上没有「头像操作」时立刻失败，且一个文件都没注入。
    /// </summary>
    /// <remarks>
    /// 这条对应真机上最常见的坏情况：登录态失效后被重定向到别处，页面上什么都没有。
    /// 若代码在找不到入口时还继续注入，日志里会是一串「注入失败」的超时，
    /// 看日志的人会以为是文件路径问题。
    /// </remarks>
    [Fact]
    public async Task 头像入口不存在时立即失败不注入()
    {
        var path = WritePage(
            FacebookMimicPage.ProfileHome(withAvatarEntry: false),
            "home-no-avatar.html");
        var page = await Channel.NewPageAsync(Browser);
        var interop = new PlaywrightPageInterop(page);

        var avatar = ImageFixtures.WritePng(Dir.Path, "avatar.png", 400, 400);

        var result = await AvatarEditor.RunAsync(
            interop,
            "100012345678",
            [avatar],
            HomeOptionsFor(path));

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到【头像操作】按钮", result.Message);
        Assert.Equal(0, await ReadAsync<int>(page, "() => window.__avatarUploads.length"));
    }
}
