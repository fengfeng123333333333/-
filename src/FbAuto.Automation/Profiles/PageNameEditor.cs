using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 主页名字：跳设置页 → 在含 <c>cquick</c> 的 iframe 里填名字 → 预览修改 → 输主账号密码 → 申请更改。
/// </summary>
/// <remarks>
/// 顺序与文案全部来自原版串（<c>_analysis/u16_all.txt</c> 3350–3372 行）：
/// <code>
/// : 新名字：「X」
/// : 跳转至主页名字设置页...  settings?tab=profile&amp;section=name&amp;view
/// : 当前共有 N 个 frame:
/// :   frame[i] url=
/// : 未找到包含 cquick 的 frame
/// : 使用 frame 查找表单...  form[action='/settings/name_change_preview/']
/// : 未找到表单
/// : 找到表单，在表单内查找名字输入框...  input[name='pseudonymous_name']
/// : 在表单内未找到 input[name='pseudonymous_name']
/// : 名字已填入
/// button[type='submit']  →  未找到【预览修改】按钮 / 已点击【预览修改】
/// #save_password        →  未找到密码输入框 / 输入密码... / 未配置密码，跳过
/// button[value='1'].layerConfirm  →  未找到【申请更改】按钮 / 已点击【申请更改】
/// : 修改主页名字失败:
/// </code>
///
/// **这是唯一一项要跨 frame 的**。表单在 iframe 里（原版按 URL 含 <c>cquick</c> 挑 frame），
/// 而密码框与确认按钮回到主 frame —— 两者混用会表现成「未找到密码输入框」，
/// 流程随即以为没配密码，直接去点确认。
///
/// **这一项也是唯一需要主账号密码的**。密码缺失时按原版语义「跳过」，
/// 但结论会带上「可能未生效」的说明，不报成一个干净的「已修改」——
/// 名字改没改是账号级操作，虚报成功比报失败代价大。
/// </remarks>
public static class PageNameEditor
{
    public const string FieldName = "主页名字";

    /// <summary>原版跳过时的文案。</summary>
    public const string SkipLine = "已跳过【修改主页名字】";

    /// <summary>frame 里找不到表单时的文案。</summary>
    public const string NoFormLine = "未找到表单";

    /// <summary>frame 里找不到名字输入框时的文案。</summary>
    public const string NoInputLine = "在表单内未找到 input[name='pseudonymous_name']";

    /// <summary>没有名为 <c>cquick</c> 的 frame 时的文案。</summary>
    public const string NoFrameLine = "未找到包含 cquick 的 frame";

    public static async Task<ProfileFieldResult> RunAsync(
        IPageInterop page,
        string? newName,
        string? password,
        ProfileEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= ProfileEditOptions.Default;

        var name = newName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return ProfileFieldResult.Skipped(FieldName, SkipLine);
        }

        var lines = new List<string> { $"新名字：「{name}」" };

        await page.NavigateAsync(options.EffectiveNameSettingsUrl, cancellationToken).ConfigureAwait(false);
        lines.Add("跳转至主页名字设置页...");

        // frame 要等：设置页先出壳，iframe 后挂。不等就枚举，得到的是一份没有 cquick 的清单，
        // 日志会写「未找到包含 cquick 的 frame」—— 看着像页面改版，实际只是慢了一步。
        var frames = await WaitFramesAsync(page, options, cancellationToken).ConfigureAwait(false);
        lines.Add($"当前共有 {frames.Count} 个 frame:");
        foreach (var frame in frames)
        {
            lines.Add($"  frame[{frame.Index}] url={frame.Url}");
        }

        if (!frames.Any(f => f.Matches(FacebookSelectors.NameFrameUrlFragment)))
        {
            lines.Add(NoFrameLine);
            return ProfileFieldResult.Failed(FieldName, NoFrameLine, lines);
        }

        lines.Add($"使用 frame 查找表单... {FacebookSelectors.NameForm}");
        if (!await page.FrameExistsAsync(
                FacebookSelectors.NameFrameUrlFragment,
                FacebookSelectors.NameForm,
                cancellationToken).ConfigureAwait(false))
        {
            lines.Add(NoFormLine);
            return ProfileFieldResult.Failed(FieldName, NoFormLine, lines);
        }

        lines.Add($"找到表单，在表单内查找名字输入框... {FacebookSelectors.NameInput}");
        if (!await page.FrameExistsAsync(
                FacebookSelectors.NameFrameUrlFragment,
                FacebookSelectors.NameInput,
                cancellationToken).ConfigureAwait(false))
        {
            lines.Add(NoInputLine);
            return ProfileFieldResult.Failed(FieldName, NoInputLine, lines);
        }

        await page.FrameFillAsync(
            FacebookSelectors.NameFrameUrlFragment,
            FacebookSelectors.NameInput,
            name,
            cancellationToken).ConfigureAwait(false);
        lines.Add("名字已填入");

        // 预览修改：这一步把表单提交到 FB，弹出版本对比/密码确认框。
        // 找不到它就不要往后走 —— 表单没提交，后面既不会有密码框，也不会有确认框，
        // 硬着头皮点确认只会得到一句「未找到【申请更改】按钮」，把真实原因藏起来。
        if (!await page.FrameExistsAsync(
                FacebookSelectors.NameFrameUrlFragment,
                FacebookSelectors.NamePreviewButton,
                cancellationToken).ConfigureAwait(false))
        {
            lines.Add("未找到【预览修改】按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到【预览修改】按钮", lines);
        }

        await page.FrameClickAsync(
            FacebookSelectors.NameFrameUrlFragment,
            FacebookSelectors.NamePreviewButton,
            0,
            cancellationToken).ConfigureAwait(false);
        lines.Add("已点击【预览修改】");

        // 密码框与确认按钮都在主 frame（弹出的对话框），不能用 Frame* 原语。
        var passwordUsed = false;
        if (await options.WaitVisibleAsync(page, FacebookSelectors.NamePasswordInput, cancellationToken)
                .ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                // 原版语义就是「跳过」：没配密码也继续点确认。
                // 但这里不把它当成一次干净的成功，见方法末尾的结论说明。
                lines.Add("未配置密码，跳过");
            }
            else
            {
                lines.Add("输入密码...");
                await page.FillAsync(
                    FacebookSelectors.NamePasswordInput,
                    password,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                passwordUsed = true;
            }
        }
        else
        {
            // 密码框没出来有两种可能：这一页对当前账号不要求密码，或者预览那步没真正提交。
            // 无法从页面区分，所以只记一行，不判失败 —— 判失败会把「不需要密码的账号」全判错。
            lines.Add("未找到密码输入框");
        }

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.NameConfirmButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到【申请更改】按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到【申请更改】按钮", lines);
        }

        await page.ClickAsync(FacebookSelectors.NameConfirmButton, 0, cancellationToken).ConfigureAwait(false);
        lines.Add("已点击【申请更改】");

        if (passwordUsed)
        {
            lines.Add("主页名字已提交申请");
            return ProfileFieldResult.Changed(FieldName, $"主页名字已提交申请：{name}", lines);
        }

        // 没有密码就点确认，FB 侧多半不会真的改。这里不报「已修改」，
        // 让结果列上直接看得到「需要人工核对」，而不是等使用者过几天发现名字没变。
        var caveat = $"主页名字已提交（未使用密码，是否生效需人工确认）：{name}";
        lines.Add(caveat);
        return ProfileFieldResult.Changed(FieldName, caveat, lines);
    }

    /// <summary>
    /// 等 frame 清单里出现含 <c>cquick</c> 的那个，返回最后一次读到的清单。
    /// </summary>
    /// <remarks>
    /// 超时后返回**最后一次**的清单而不是空表：日志要能把「一共几个 frame、分别是什么地址」
    /// 打出来，否则排查时只剩一句「未找到包含 cquick 的 frame」，看不出是没挂上还是地址变了。
    ///
    /// 每次读到的清单都是「当时」的快照，所以循环里一直覆盖同一个变量，
    /// 不累积 —— 累积会把上一轮的旧 frame 混进来，让人误以为它还在。
    /// </remarks>
    private static async Task<IReadOnlyList<FrameRef>> WaitFramesAsync(
        IPageInterop page,
        ProfileEditOptions options,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + options.EffectiveActionTimeout;
        IReadOnlyList<FrameRef> frames = [];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            frames = await page.GetFramesAsync(cancellationToken).ConfigureAwait(false);
            if (frames.Any(f => f.Matches(FacebookSelectors.NameFrameUrlFragment)))
            {
                return frames;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return frames;
            }

            await Task.Delay(options.EffectivePollInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
