using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 个性签名：打开签名页 → 点编辑入口 → 填 <c>textarea</c> → 保存。
/// </summary>
/// <remarks>
/// 两个入口是分开的，因为 FB 在没有签名时**根本不渲染带 aria-label 的那个按钮**，
/// 只有一个写着「关于你」的入口。原版也是这么分的：
/// 「检测到已有签名,点击」/「未检测到已有签名,点击」两条日志。
///
/// 保存按钮可能不存在（自动保存），这不算失败 —— 原版的日志是
/// 「点击保存按钮失败(可能会自动保存)」，它自己也把这种情况当成功。
/// </remarks>
public static class SignatureEditor
{
    public const string FieldName = "个性签名";

    public static async Task<ProfileFieldResult> RunAsync(
        IPageInterop page,
        string profileId,
        string? signature,
        ProfileEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= ProfileEditOptions.Default;

        // 先校验再导航：超长的签名写进去会被浏览器静默截断，
        // 与其跑完整个页面流程发现页面上只有半句话，不如在这里就停下。
        var check = SignatureText.Check(signature);
        if (check.Verdict == SignatureVerdict.Empty)
        {
            return ProfileFieldResult.Skipped(FieldName, check.Message);
        }

        if (check.Verdict == SignatureVerdict.TooLong)
        {
            return ProfileFieldResult.Failed(FieldName, check.Message, [check.Message]);
        }

        var lines = new List<string>();

        await page.NavigateAsync(
            FacebookUrls.Format(options.EffectiveIntroUrlTemplate, profileId),
            cancellationToken).ConfigureAwait(false);
        lines.Add($"跳转签名页面: profile.php?id={profileId}");

        var entry = await FindEntryAsync(page, lines, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            lines.Add("未找到任何签名入口，跳过");
            return ProfileFieldResult.Failed(FieldName, "未找到任何签名入口，跳过", lines);
        }

        await page.ClickAsync(entry, 0, cancellationToken).ConfigureAwait(false);

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.SignatureTextarea, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到签名输入框");
            return ProfileFieldResult.Failed(FieldName, "未找到签名输入框", lines);
        }

        await page.FillAsync(
            FacebookSelectors.SignatureTextarea,
            check.Text,
            0,
            cancellationToken).ConfigureAwait(false);
        lines.Add($"签名已输入: {check.Text}");

        await SaveIfPresentAsync(page, options, lines, cancellationToken).ConfigureAwait(false);

        return ProfileFieldResult.Changed(FieldName, "签名修改完成", lines);
    }

    private static async Task<string?> FindEntryAsync(
        IPageInterop page,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        if (await page.IsVisibleAsync(FacebookSelectors.SignatureEditEntry, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("检测到已有签名，点击编辑入口");
            return FacebookSelectors.SignatureEditEntry;
        }

        if (await page.IsVisibleAsync(FacebookSelectors.SignatureEmptyEntry, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未检测到已有签名，点击「关于你」入口");
            return FacebookSelectors.SignatureEmptyEntry;
        }

        return null;
    }

    private static async Task SaveIfPresentAsync(
        IPageInterop page,
        ProfileEditOptions options,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        if (!await page.IsVisibleAsync(FacebookSelectors.SaveButton, cancellationToken).ConfigureAwait(false))
        {
            lines.Add("未找到保存按钮，按自动保存处理");
            return;
        }

        var clicked = await options.ClickWhenEnabledAsync(page, FacebookSelectors.SaveButton, cancellationToken)
            .ConfigureAwait(false);

        lines.Add(clicked ? "已点击保存" : "保存按钮一直不可用，按自动保存处理");
    }
}
