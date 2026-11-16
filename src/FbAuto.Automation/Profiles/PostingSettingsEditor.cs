using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 发帖设置：把「谁能在你的公共主页发帖？」改成仅限自己。
/// </summary>
/// <remarks>
/// 这一项不需要 profile id —— 设置页是全局的。
///
/// 三个容易点空的地方：
/// <list type="number">
/// <item>那一行的文本是 <c>span</c>，点 span 不一定能触发按钮（padding 在按钮上），
/// 所以选择器直接落到 <c>role='button'</c> 的祖先上。</item>
/// <item>选项和保存按钮是两个不同层的控件，面板没弹出来就点保存，会点到背后的东西。</item>
/// <item>保存按钮在面板刚弹开时通常还是禁用的，直接点会卡一个点击超时。</item>
/// </list>
/// </remarks>
public static class PostingSettingsEditor
{
    public const string FieldName = "发帖设置";

    /// <summary>原版跳过串，本项未勾选时打这一句。</summary>
    public const string SkipLine = "已跳过【修改公共主页发帖设置】";

    public static async Task<ProfileFieldResult> RunAsync(
        IPageInterop page,
        ProfileEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= ProfileEditOptions.Default;

        var lines = new List<string>();

        await page.NavigateAsync(options.EffectivePostingSettingsUrl, cancellationToken).ConfigureAwait(false);
        lines.Add("跳转到发帖设置页面");

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.PostingSettingRowButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到「谁能在你的公共主页发帖？」这一行");
            return ProfileFieldResult.Failed(FieldName, "未找到发帖设置行", lines);
        }

        await page.ClickAsync(FacebookSelectors.PostingSettingRowButton, 0, cancellationToken)
            .ConfigureAwait(false);
        lines.Add("已点击发帖设置行");

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.PostingSettingSelfOnly, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到「仅限自己」选项");
            return ProfileFieldResult.Failed(FieldName, "未找到「仅限自己」选项", lines);
        }

        await page.ClickAsync(FacebookSelectors.PostingSettingSelfOnly, 0, cancellationToken)
            .ConfigureAwait(false);
        lines.Add("已选择「仅限自己」");

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.PostingSettingSave, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到保存按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到保存按钮", lines);
        }

        var saved = await options.ClickWhenEnabledAsync(
            page,
            FacebookSelectors.PostingSettingSave,
            cancellationToken).ConfigureAwait(false);

        if (!saved)
        {
            lines.Add("保存按钮一直不可用");
            return ProfileFieldResult.Failed(FieldName, "保存按钮一直不可用", lines);
        }

        lines.Add("已点击保存按钮");
        return ProfileFieldResult.Changed(FieldName, "发帖设置已改为仅限自己", lines);
    }
}
