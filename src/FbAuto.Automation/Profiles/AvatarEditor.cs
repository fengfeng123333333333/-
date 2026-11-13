using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Flows;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 头像：打开主页 → 点头像区域 → 等「选择头像」菜单 → 点它 → 注入图片 → 等处理 → 点保存 → 等保存完成。
/// </summary>
/// <remarks>
/// 六步的顺序全部来自原版日志（重扫 <c>FaceBook自动化.dll</c> 得到，见
/// <c>_analysis/u16_all.txt</c> 第 3437–3456 行）：
/// <code>
/// : 未找到背景图 file input
/// : 背景图已注入:
/// : 点击头像区域...
/// [aria-label='头像操作']
/// : 未找到【头像操作】按钮
/// : 等待【选择头像】菜单出现...
/// div[role='menuitem']:has-text('选择头像')
/// : 未找到【选择头像】菜单项
/// : 通过【上传照片】定位 file input...
/// div:has(> div[aria-label='上传照片']) > input[type='file']
/// : 未找到头像 file input
/// : 头像已注入:
/// 注入失败:
/// : 修改头像...
/// : 修改头像失败，第 N 次重试...
/// : 等待头像处理...
/// : 点击【保存】保存头像...
/// : 保存头像失败，第 N 次重试...
/// : 等待头像保存...
/// </code>
/// 也就是说原版是「每轮重试都重开一次菜单」：注入被拒之后对话框已经关了，
/// 不重开就注不进第二张图。这一点很关键，所以换图流程加了一个每轮准备钩子
/// （<c>CoverAttemptFlow.RunForAvatarAsync</c> 的 <c>prepareAsync</c>），
/// 而不是「打开一次、循环注入」。
///
/// **为什么要先点「头像操作」再点「选择头像」**：原版的 file input 选择器
/// <c>div:has(> div[aria-label='上传照片']) &gt; input[type='file']</c> 只有在
/// 「上传照片」那个对话框渲染出来之后才在 DOM 里。直接注入拿到的是空集合，
/// SetInputFiles 会以「找不到元素」超时收场。
/// </remarks>
public static class AvatarEditor
{
    public const string FieldName = "头像";

    /// <summary>原版跳过时的文案。</summary>
    public const string SkipLine = "已跳过【修改头像】";

    public static async Task<ProfileFieldResult> RunAsync(
        IPageInterop page,
        string profileId,
        IReadOnlyList<string>? candidates,
        ProfileEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= ProfileEditOptions.Default;

        var images = (candidates ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (images.Length == 0)
        {
            // 没配图片目录就跳过，不算失败 —— 这与原版「已跳过【修改头像】」一致。
            return ProfileFieldResult.Skipped(FieldName, SkipLine);
        }

        var lines = new List<string> { "修改头像..." };

        await page.NavigateAsync(
            FacebookUrls.Format(options.EffectiveProfileHomeUrlTemplate, profileId),
            cancellationToken).ConfigureAwait(false);

        CoverAttemptResult attempt;
        try
        {
            attempt = await CoverAttemptFlow.RunForAvatarAsync(
                page,
                images,
                rejectionWindow: options.EffectiveImageRejectionWindow,
                cancellationToken: cancellationToken,
                prepareAsync: ct => OpenUploadDialogAsync(page, options, lines, ct)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PageActionException ex)
        {
            // 打开入口的半途失败（按钮没了 / 菜单没弹 / input 没出现）也算这一项失败，
            // 但不往上抛：资料修改里其它项还要跑，见 ProfileEditFlow 的取舍说明。
            lines.Add($"修改头像失败: {ex.Message}");
            return ProfileFieldResult.Failed(FieldName, ex.Message, lines);
        }

        lines.AddRange(attempt.LogLines);
        if (!attempt.Accepted)
        {
            return ProfileFieldResult.Failed(FieldName, attempt.Summary, lines);
        }

        lines.Add("等待头像处理...");
        if (!await options.WaitVisibleAsync(page, FacebookSelectors.SaveButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到头像编辑面板的保存按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到头像编辑面板的保存按钮", lines);
        }

        lines.Add("点击【保存】保存头像...");
        if (!await options.ClickWhenEnabledAsync(page, FacebookSelectors.SaveButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("保存按钮一直不可用");
            return ProfileFieldResult.Failed(FieldName, "保存头像失败：保存按钮一直不可用", lines);
        }

        lines.Add("等待头像保存...");
        var settled = await options.WaitHiddenAsync(page, FacebookSelectors.SaveButton, cancellationToken)
            .ConfigureAwait(false);
        lines.Add(settled ? "头像已保存" : "保存按钮在超时内仍未消失，按已提交处理");

        return ProfileFieldResult.Changed(FieldName, "头像修改完成", lines);
    }

    /// <summary>
    /// 打开「上传照片」对话框，让 file input 进入 DOM。
    /// </summary>
    /// <remarks>
    /// 日志行写进调用方传进来的 <paramref name="lines"/>，不返回 —— 因为中途失败时
    /// 已经走过的那几步必须留在日志里。若改成「成功才返回列表」，卡在第二步的失败
    /// 会连「点过头像区域」这一行都没有，看日志的人分不清是没点还是点不动。
    /// </remarks>
    private static async Task OpenUploadDialogAsync(
        IPageInterop page,
        ProfileEditOptions options,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        if (!await page.IsVisibleAsync(FacebookSelectors.AvatarArea, cancellationToken).ConfigureAwait(false))
        {
            throw new PageActionException("定位", FacebookSelectors.AvatarArea, "未找到【头像操作】按钮");
        }

        await page.ClickAsync(FacebookSelectors.AvatarArea, 0, cancellationToken).ConfigureAwait(false);
        lines.Add("点击头像区域...");

        lines.Add("等待【选择头像】菜单出现...");
        if (!await options.WaitVisibleAsync(page, FacebookSelectors.ChooseAvatarMenuItem, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new PageActionException("定位", FacebookSelectors.ChooseAvatarMenuItem, "未找到【选择头像】菜单项");
        }

        await page.ClickAsync(FacebookSelectors.ChooseAvatarMenuItem, 0, cancellationToken).ConfigureAwait(false);

        lines.Add("通过【上传照片】定位 file input...");
        if (!await options.WaitExistsAsync(page, FacebookSelectors.AvatarFileInput, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new PageActionException("定位", FacebookSelectors.AvatarFileInput, "未找到头像 file input");
        }
    }
}
