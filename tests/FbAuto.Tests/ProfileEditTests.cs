using FbAuto.Automation.Profiles;
using FbAuto.Automation.Selectors;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 资料修改（谷歌版）的纯逻辑部分：profile id 提取、签名校验、类别选项拼接、三个编辑器与编排。
/// </summary>
/// <remarks>
/// 这一层用 <see cref="FakePageInterop"/> 跑，不起浏览器。它能测的是「分支走对没有」：
/// 该跳过的有没有跳过、该在写入前拦的有没有拦住、一项失败之后有没有继续。
/// 「选择器在真浏览器里能不能命中」是另一组用例的事。
///
/// 等待类参数一律压到几十毫秒 —— 默认 15 秒的话，一条「元素缺失」的用例要跑满 15 秒才返回。
/// </remarks>
public sealed class ProfileEditTests
{
    private static ProfileEditOptions Fast => new(
        ActionTimeout: TimeSpan.FromMilliseconds(120),
        SaveEnabledTimeout: TimeSpan.FromMilliseconds(120),
        PollInterval: TimeSpan.FromMilliseconds(5));

    // ── profile id 提取 ───────────────────────────────────────────────────

    /// <summary>
    /// 页面上同时有别人的相对链接时，必须取完整链接那一个。
    /// </summary>
    /// <remarks>
    /// 这是本组最重要的一条：原版先用**完整前缀**定位，说明页面上确实会混进别的
    /// <c>profile.php?id=</c>。若按出现顺序取第一个，改的就是别人的主页。
    /// </remarks>
    [Fact]
    public void 完整链接优先于先出现的相对链接()
    {
        const string content = """
            <a href="profile.php?id=111111">别人的主页</a>
            <a href="https://www.facebook.com/profile.php?id=222222">我的主页</a>
            """;

        var result = ProfileIdExtractor.ExtractDetailed(content);

        Assert.Equal("222222", result.ProfileId);
        Assert.Equal(ProfileIdSource.AbsoluteUrl, result.Source);
    }

    [Fact]
    public void 只有相对链接时走兜底并标出来源()
    {
        var result = ProfileIdExtractor.ExtractDetailed("""<a href="profile.php?id=333333">我的主页</a>""");

        Assert.Equal("333333", result.ProfileId);
        Assert.Equal(ProfileIdSource.Loose, result.Source);

        // 「用了兜底」要写进日志：真机上出现这句，说明完整链接那条路没命中，
        // 可能是登录墙或页面改版，值得人看一眼。
        Assert.Contains("相对链接兜底", result.LogLine);
    }

    [Fact]
    public void HTML转义后的链接也能取到()
    {
        // 从 innerHTML 拿内容时 & 会被转成 &amp;。
        var result = ProfileIdExtractor.ExtractDetailed(
            """<a href="profile.php?amp;id=444444">我的主页</a>""");

        Assert.Equal("444444", result.ProfileId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>登录</body></html>")]
    [InlineData("""<a href="profile.php?id=1234">太短</a>""")]
    public void 取不到时明确返回未找到(string content)
    {
        var result = ProfileIdExtractor.ExtractDetailed(content);

        Assert.False(result.Found);
        Assert.Null(result.ProfileId);
        Assert.Equal(ProfileIdSource.None, result.Source);
        Assert.Contains("没有找到", result.LogLine);
    }

    [Fact]
    public void 传入null不抛异常()
    {
        Assert.Null(ProfileIdExtractor.Extract(null));
    }

    // ── 签名校验 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public void 签名空白按跳过处理不当失败(string? raw)
    {
        var check = SignatureText.Check(raw);

        Assert.Equal(SignatureVerdict.Empty, check.Verdict);
        Assert.False(check.ShouldWrite);
    }

    [Fact]
    public void 签名去首尾空白后再写()
    {
        var check = SignatureText.Check("  你好，世界  \r\n");

        Assert.Equal(SignatureVerdict.Ok, check.Verdict);
        Assert.Equal("你好，世界", check.Text);
    }

    [Fact]
    public void 签名正好255字通过()
    {
        var check = SignatureText.Check(new string('字', SignatureText.MaxLength));

        Assert.Equal(SignatureVerdict.Ok, check.Verdict);
    }

    /// <summary>
    /// 超长必须在写入前拦住，而不是写进去让浏览器截断。
    /// </summary>
    [Fact]
    public void 签名超过255字判失败且不写入()
    {
        var check = SignatureText.Check(new string('字', SignatureText.MaxLength + 1));

        Assert.Equal(SignatureVerdict.TooLong, check.Verdict);
        Assert.False(check.ShouldWrite);

        // 日志要报出实际长度，否则使用者不知道该删多少字。
        Assert.Contains("256", check.Message);
        Assert.Contains("255", check.Message);
    }

    // ── 签名文件 ──────────────────────────────────────────────────────────

    [Fact]
    public void 没有配置签名文件时跳过并说明()
    {
        var (text, lines) = SignatureSource.Read(null);

        Assert.Equal(string.Empty, text);
        Assert.Contains(lines, x => x.Contains("没有配置签名文件"));
    }

    [Fact]
    public void 签名文件不存在时跳过并给出路径()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.txt");

        var (text, lines) = SignatureSource.Read(missing);

        Assert.Equal(string.Empty, text);
        Assert.Contains(lines, x => x.Contains("不存在") && x.Contains(missing));
    }

    /// <summary>
    /// 多行文件要整段读回来，不能只取第一行。
    /// </summary>
    [Fact]
    public void 签名文件整段读入并去掉末尾换行()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "signature.txt");
        File.WriteAllText(path, "第一行\n第二行\n");

        var (text, lines) = SignatureSource.Read(path);

        Assert.Equal("第一行\n第二行", text);
        Assert.Contains(lines, x => x.Contains("签名已加载"));
    }

    // ── 类别选择器拼接 ────────────────────────────────────────────────────

    /// <summary>
    /// 下拉候选：原版是 <c>div[role='option']</c> 与 <c>li[role='option']</c> 的并集。
    /// </summary>
    [Fact]
    public void 类别下拉项按文本拼接()
    {
        Assert.Equal(
            """div[role='option']:has-text("教育"), li[role='option']:has-text("教育")""",
            CategoryEditor.OptionSelector("教育"));
    }

    /// <summary>
    /// 「当前类别」的判定用的**不是**下拉项选择器，而是页面上展示当前类别的那段文本。
    /// 两者混用会点到只读文本上 —— 点了没反应，随后卡一个点击超时。
    /// </summary>
    [Fact]
    public void 当前类别判定用展示文本选择器()
    {
        Assert.Equal("""div[dir='auto']:has-text("教育")""", CategoryEditor.CurrentCategorySelector("教育"));
    }

    /// <summary>
    /// 类别值里带双引号时，选择器必须能扛住 —— 不转义会得到语法错误的选择器，
    /// Playwright 报「选择器无效」，很容易被当成页面改版查半天。
    /// </summary>
    [Fact]
    public void 类别值里的双引号被转义()
    {
        var option = CategoryEditor.OptionSelector("说\"唱\"");
        var current = CategoryEditor.CurrentCategorySelector("说\"唱\"");

        Assert.Contains("""has-text("说\"唱\"")""", option, StringComparison.Ordinal);
        Assert.Equal("""div[dir='auto']:has-text("说\"唱\"")""", current);
    }

    [Fact]
    public void 类别值里的反斜杠被转义()
    {
        var option = CategoryEditor.OptionSelector(@"a\b");

        Assert.Contains("""has-text("a\\b")""", option, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void 类别为空时拼选择器直接抛异常(string category)
    {
        Assert.Throws<ArgumentException>(() => CategoryEditor.OptionSelector(category));
        Assert.Throws<ArgumentException>(() => CategoryEditor.CurrentCategorySelector(category));
    }

    // ── 地址与调参 ────────────────────────────────────────────────────────

    [Fact]
    public void 默认地址指向官方页面()
    {
        var options = ProfileEditOptions.Default;

        Assert.Equal(FacebookUrls.ProfileIntro, options.EffectiveIntroUrlTemplate);
        Assert.Equal(FacebookUrls.ProfileCategory, options.EffectiveCategoryUrlTemplate);
        Assert.Equal(FacebookUrls.PostingSettings, options.EffectivePostingSettingsUrl);
    }

    [Fact]
    public void 覆盖后的地址生效()
    {
        var options = new ProfileEditOptions(
            IntroUrlTemplate: "file:///intro.html?id={0}",
            CategoryUrlTemplate: "file:///cat.html?id={0}",
            PostingSettingsUrl: "file:///posting.html");

        Assert.Equal("file:///intro.html?id={0}", options.EffectiveIntroUrlTemplate);
        Assert.Equal("file:///cat.html?id={0}", options.EffectiveCategoryUrlTemplate);
        Assert.Equal("file:///posting.html", options.EffectivePostingSettingsUrl);
    }

    [Fact]
    public void 空白地址退回默认值()
    {
        var options = new ProfileEditOptions(IntroUrlTemplate: "  ");

        Assert.Equal(FacebookUrls.ProfileIntro, options.EffectiveIntroUrlTemplate);
    }

    // ── 签名编辑器 ────────────────────────────────────────────────────────

    [Fact]
    public async Task 签名为空时根本不导航()
    {
        var page = new FakePageInterop();

        var result = await SignatureEditor.RunAsync(page, "100012345678", string.Empty, Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);

        // 没内容却先导航过去，等于白跑一次页面加载，真机上还会多一次风控可见动作。
        Assert.Empty(page.NavigatedUrls);
    }

    [Fact]
    public async Task 签名超长时根本不导航()
    {
        var page = new FakePageInterop();

        var result = await SignatureEditor.RunAsync(page, "100012345678", new string('字', 300), Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Empty(page.NavigatedUrls);
    }

    [Fact]
    public async Task 已有签名时点编辑入口()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SignatureEditEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureEmptyEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureTextarea] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = true;

        var result = await SignatureEditor.RunAsync(page, "100012345678", "新签名", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);

        // 两个入口同时可见时也要用「编辑个性签名」那个：另一条是「关于你」，
        // 在已经有签名的主页上点它进的是另一套界面。
        Assert.Equal(FacebookSelectors.SignatureEditEntry, page.Clicked[0]);
        Assert.Contains(page.Filled, f => f.Selector == FacebookSelectors.SignatureTextarea && f.Text == "新签名");
        Assert.Contains(FacebookSelectors.SaveButton, page.Clicked);
    }

    [Fact]
    public async Task 没有签名时点关于你入口()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SignatureEmptyEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureTextarea] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = true;

        var result = await SignatureEditor.RunAsync(page, "100012345678", "签名", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal(FacebookSelectors.SignatureEmptyEntry, page.Clicked[0]);
        Assert.Contains(result.LogLines, x => x.Contains("未检测到已有签名"));
    }

    [Fact]
    public async Task 两个签名入口都没有时判失败()
    {
        var page = new FakePageInterop();

        var result = await SignatureEditor.RunAsync(page, "100012345678", "签名", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到任何签名入口", result.Message);
    }

    [Fact]
    public async Task 找不到输入框时判失败()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SignatureEditEntry] = true;

        var result = await SignatureEditor.RunAsync(page, "100012345678", "签名", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到签名输入框", result.Message);
    }

    /// <summary>
    /// 没有保存按钮不是失败：FB 的签名框在某些版本里是自动保存的。
    /// </summary>
    [Fact]
    public async Task 没有保存按钮按自动保存处理()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SignatureEditEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureTextarea] = true;

        var result = await SignatureEditor.RunAsync(page, "100012345678", "签名", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains(result.LogLines, x => x.Contains("自动保存"));
    }

    /// <summary>
    /// 保存按钮一直禁用也不能判失败 —— 内容已经填进去了，判失败会让人以为没改成功。
    /// </summary>
    [Fact]
    public async Task 保存按钮一直不可用时仍按自动保存处理()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SignatureEditEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureTextarea] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = false;

        var result = await SignatureEditor.RunAsync(page, "100012345678", "签名", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains(result.LogLines, x => x.Contains("一直不可用"));
    }

    [Fact]
    public async Task 签名页地址带上了profile_id()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.SignatureEditEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureTextarea] = true;

        await SignatureEditor.RunAsync(page, "100012345678", "签名", Fast);

        Assert.Equal(
            "https://www.facebook.com/profile.php?id=100012345678&sk=directory_intro",
            page.NavigatedUrls[0]);
    }

    // ── 类别编辑器 ────────────────────────────────────────────────────────

    [Fact]
    public async Task 类别为空时根本不导航()
    {
        var page = new FakePageInterop();

        var result = await CategoryEditor.RunAsync(page, "100012345678", " ", Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Empty(page.NavigatedUrls);
    }

    [Fact]
    public async Task 类别正常修改时按顺序点三下()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;
        page.VisibleSelectors[FacebookSelectors.CategorySearchInput] = true;
        page.VisibleSelectors[CategoryEditor.OptionSelector("教育")] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = true;

        var result = await CategoryEditor.RunAsync(page, "100012345678", " 教育 ", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal(
            [
                FacebookSelectors.EditCategoryButton,
                CategoryEditor.OptionSelector("教育"),
                FacebookSelectors.SaveButton,
            ],
            page.Clicked);

        // 类别的改法是「在搜索框里输入关键词，再从下拉里选」，不是点一列现成选项。
        Assert.Contains(
            page.Filled,
            f => f.Selector == FacebookSelectors.CategorySearchInput && f.Text == "教育");
    }

    /// <summary>
    /// 当前类别已经是目标时直接跳过，不做任何改动。
    /// </summary>
    /// <remarks>
    /// 省的不只是时间：每多点一次编辑、保存，都是一次 FB 视野里的动作。
    /// </remarks>
    [Fact]
    public async Task 当前类别已是目标时跳过()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;
        page.VisibleSelectors[CategoryEditor.CurrentCategorySelector("教育")] = true;

        var result = await CategoryEditor.RunAsync(page, "100012345678", "教育", Fast);

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Empty(page.Clicked);
        Assert.Contains(result.LogLines, x => x.Contains("当前类别已是"));
    }

    [Fact]
    public async Task 当前类别不是目标时继续改()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;
        page.VisibleSelectors[CategoryEditor.CurrentCategorySelector("戏剧")] = true;
        page.VisibleSelectors[FacebookSelectors.CategorySearchInput] = true;
        page.VisibleSelectors[CategoryEditor.OptionSelector("教育")] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = true;

        var result = await CategoryEditor.RunAsync(page, "100012345678", "教育", Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Contains(result.LogLines, x => x.Contains("当前类别不是"));
    }

    [Fact]
    public async Task 类别入口缺失时判失败()
    {
        var page = new FakePageInterop();

        var result = await CategoryEditor.RunAsync(page, "100012345678", "教育", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到编辑类别按钮", result.Message);
    }

    /// <summary>
    /// 搜索框没出现时不能去猜选项 —— 直接报失败。
    /// </summary>
    [Fact]
    public async Task 类别搜索框缺失时判失败()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;

        var result = await CategoryEditor.RunAsync(page, "100012345678", "教育", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到类别搜索输入框", result.Message);
    }

    /// <summary>
    /// 目标类别搜不出来时必须报失败，而不是点一个别的候选凑数。
    /// </summary>
    [Fact]
    public async Task 类别下拉项不存在时判失败()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;
        page.VisibleSelectors[FacebookSelectors.CategorySearchInput] = true;
        page.VisibleSelectors[CategoryEditor.OptionSelector("教育")] = true;

        var result = await CategoryEditor.RunAsync(page, "100012345678", "戏剧", Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到下拉项", result.Message);
        Assert.DoesNotContain(page.Clicked, x => x == CategoryEditor.OptionSelector("教育"));
    }

    [Fact]
    public async Task 类别保存按钮一直不可用时判失败()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;
        page.VisibleSelectors[FacebookSelectors.CategorySearchInput] = true;
        page.VisibleSelectors[CategoryEditor.OptionSelector("教育")] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = false;

        var result = await CategoryEditor.RunAsync(page, "100012345678", "教育", Fast);

        // 这里与签名不同：类别没有「自动保存」这条兜底路径（原版也是显式点保存）。
        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("一直不可用", result.Message);
    }

    // ── 发帖设置 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task 发帖设置正常时按顺序点三下()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.PostingSettingRowButton] = true;
        page.VisibleSelectors[FacebookSelectors.PostingSettingSelfOnly] = true;
        page.VisibleSelectors[FacebookSelectors.PostingSettingSave] = true;
        page.EnabledSelectors[FacebookSelectors.PostingSettingSave] = true;

        var result = await PostingSettingsEditor.RunAsync(page, Fast);

        Assert.Equal(FieldEditStatus.Changed, result.Status);
        Assert.Equal(
            [
                FacebookSelectors.PostingSettingRowButton,
                FacebookSelectors.PostingSettingSelfOnly,
                FacebookSelectors.PostingSettingSave,
            ],
            page.Clicked);

        // 点的是按钮而不是那一行的文本 span —— 点 span 在真页面上不一定触发。
        Assert.DoesNotContain(FacebookSelectors.PostingSettingRow, page.Clicked);
    }

    [Fact]
    public async Task 发帖设置行缺失时判失败()
    {
        var page = new FakePageInterop();

        var result = await PostingSettingsEditor.RunAsync(page, Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("未找到发帖设置行", result.Message);
    }

    [Fact]
    public async Task 仅限自己选项缺失时判失败()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.PostingSettingRowButton] = true;

        var result = await PostingSettingsEditor.RunAsync(page, Fast);

        Assert.Equal(FieldEditStatus.Failed, result.Status);
        Assert.Contains("仅限自己", result.Message);
    }

    // ── 编排 ──────────────────────────────────────────────────────────────

    private static FakePageInterop PageWithCategoryAndPosting()
    {
        var page = new FakePageInterop();
        page.VisibleSelectors[FacebookSelectors.EditCategoryButton] = true;
        page.VisibleSelectors[FacebookSelectors.CategorySearchInput] = true;
        page.VisibleSelectors[CategoryEditor.OptionSelector("教育")] = true;
        page.VisibleSelectors[FacebookSelectors.SaveButton] = true;
        page.VisibleSelectors[FacebookSelectors.PostingSettingRowButton] = true;
        page.VisibleSelectors[FacebookSelectors.PostingSettingSelfOnly] = true;
        page.VisibleSelectors[FacebookSelectors.PostingSettingSave] = true;
        page.EnabledSelectors[FacebookSelectors.SaveButton] = true;
        page.EnabledSelectors[FacebookSelectors.PostingSettingSave] = true;
        return page;
    }

    /// <summary>
    /// 签名一项失败时，类别与发帖设置必须照跑。
    /// </summary>
    /// <remarks>
    /// 这是原版明确的语义（日志「修改签名失败(继续下一步)」）。三条项互不依赖，
    /// 一项挂了让另外两项白跑一遍，等于每条任务都多一次界面可见动作。
    /// </remarks>
    [Fact]
    public async Task 签名失败不中断后面两项()
    {
        var page = PageWithCategoryAndPosting();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest("要改的签名", "教育", UpdatePostingSettings: true),
            profileId: "100012345678",
            options: Fast);

        Assert.Equal(1, result.FailedCount);
        Assert.Equal(2, result.ChangedCount);

        // 有一项失败就整体失败 —— 不能因为「改成了两项」就报成功，
        // 否则使用者不会去核对页面，漏掉的那一项要等他自己发现。
        Assert.False(result.Success);
        Assert.Contains("个性签名", result.Summary);
        Assert.Contains(result.LogLines, x => x.Contains("继续下一步"));
    }

    [Fact]
    public async Task 全部跳过时整体算成功()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(string.Empty, string.Empty, UpdatePostingSettings: false),
            profileId: "100012345678",
            options: Fast);

        Assert.True(result.Success);
        Assert.Equal(0, result.FailedCount);

        // 六项全跳过：背景图、头像（没给候选图）、发帖设置（没启用）、粉丝公开内容（没给屏蔽词）、签名、类别。
        Assert.Equal(6, result.SkippedCount);
        Assert.Equal(
            ["背景图", "头像", "发帖设置", "粉丝公开内容", "个性签名", "类别"],
            result.Fields.Select(f => f.Field));

        // 什么都没要求改，就不该碰页面。
        Assert.Empty(page.NavigatedUrls);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 解析不到profile_id时整条失败且不碰任何字段()
    {
        var page = new FakePageInterop();
        page.PageContent = "<html><body>登录 Facebook</body></html>";

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest("签名", "教育", UpdatePostingSettings: true),
            options: Fast);

        Assert.False(result.Success);
        Assert.Empty(result.Fields);
        Assert.Contains("没有找到主页 profile id", result.Summary);

        // 只有打开主页那一次导航，之后一个动作都不该有。
        Assert.Single(page.NavigatedUrls);
        Assert.Empty(page.Clicked);
    }

    [Fact]
    public async Task 已知profile_id时不再打开主页()
    {
        var page = new FakePageInterop();

        await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(string.Empty, string.Empty, UpdatePostingSettings: false),
            profileId: "100012345678",
            options: Fast);

        Assert.DoesNotContain(page.NavigatedUrls, x => x == ProfileIdResolver.DefaultHomeUrl);
    }

    [Fact]
    public async Task 未启用发帖设置时明确记为跳过()
    {
        var page = new FakePageInterop();

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest(string.Empty, string.Empty, UpdatePostingSettings: false),
            profileId: "100012345678",
            options: Fast);

        var posting = result.Fields.Single(f => f.Field == PostingSettingsEditor.FieldName);
        Assert.Equal(FieldEditStatus.Skipped, posting.Status);
        Assert.Contains("未启用", posting.Message);

        Assert.DoesNotContain(
            page.NavigatedUrls,
            x => x == FacebookUrls.PostingSettings);
    }

    [Fact]
    public async Task 三项全成功时汇总里带改了几项()
    {
        var page = PageWithCategoryAndPosting();
        page.VisibleSelectors[FacebookSelectors.SignatureEditEntry] = true;
        page.VisibleSelectors[FacebookSelectors.SignatureTextarea] = true;

        var result = await ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest("签名内容", "教育", UpdatePostingSettings: true),
            profileId: "100012345678",
            options: Fast);

        Assert.True(result.Success);
        Assert.Equal(3, result.ChangedCount);
        Assert.Contains("3 项已改", result.Summary);
    }

    /// <summary>
    /// 取消要原样往外抛，不能被编排层当成「这一项失败」吞掉。
    /// </summary>
    /// <remarks>
    /// 吞掉的表现是：使用者按了停止，程序却把后面几项继续跑完，
    /// 还以为自己在执行一条正常任务。
    /// </remarks>
    [Fact]
    public async Task 取消原样抛出()
    {
        var page = new FakePageInterop();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProfileEditFlow.RunAsync(
            page,
            new ProfileEditRequest("签名", "教育", UpdatePostingSettings: true),
            profileId: "100012345678",
            options: Fast,
            cancellationToken: cts.Token));
    }
}
