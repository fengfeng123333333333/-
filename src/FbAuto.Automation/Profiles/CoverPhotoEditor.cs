using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Flows;
using FbAuto.Automation.Media;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 背景图（公共主页封面）：打开主页 → 注入图片（被拒就换下一张）→ 点「保存更改」→ 等保存完成。
/// </summary>
/// <remarks>
/// 顺序来自原版日志：
/// <code>
/// div:has(> div[role='button']:has-text('封面照片')) &gt; input[type='file']
/// : 未找到背景图 file input
/// : 背景图已注入:
/// : 修改背景图...
/// : 修改背景图失败，第 N 次重试...
/// : 检测背景图尺寸提示...
/// : 背景图尺寸不符合要求（太大或太小），将尝试其他图片
/// : 点击【保存更改】保存背景图...
/// : 保存背景图失败，第 N 次重试...
/// : 等待背景图保存...
/// : 已跳过【修改背景图】
/// </code>
///
/// **与头像的关键差别**：封面的 file input 直接挂在主页 DOM 上，不需要点开任何菜单，
/// 所以重试时不用重开入口；而头像必须。这就是为什么只有头像那条传了准备钩子。
///
/// **本地尺寸预校验默认开启**（<see cref="CoverSizeRule.FacebookPageCover"/>）。
/// 这是相对原版的增强：原版纯靠页面的「请选择其他封面照片」对话框，本地这层能省掉
/// 一次注定失败的上传。预检挡掉的图不算一次上传尝试。
/// </remarks>
public static class CoverPhotoEditor
{
    public const string FieldName = "背景图";

    /// <summary>原版跳过时的文案。</summary>
    public const string SkipLine = "已跳过【修改背景图】";

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
            return ProfileFieldResult.Skipped(FieldName, SkipLine);
        }

        var lines = new List<string> { "修改背景图..." };

        await page.NavigateAsync(
            FacebookUrls.Format(options.EffectiveProfileHomeUrlTemplate, profileId),
            cancellationToken).ConfigureAwait(false);

        // file input 是 display:none，只能按「存在」判断，不能按可见判断。
        if (!await options.WaitExistsAsync(page, FacebookSelectors.CoverPhotoFileInput, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到背景图 file input");
            return ProfileFieldResult.Failed(FieldName, "未找到背景图 file input", lines);
        }

        var attempt = await CoverAttemptFlow.RunForCoverAsync(
            page,
            images,
            rejectionWindow: options.EffectiveImageRejectionWindow,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        lines.AddRange(attempt.LogLines);
        if (!attempt.Accepted)
        {
            return ProfileFieldResult.Failed(FieldName, attempt.Summary, lines);
        }

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.SaveChangesButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到【保存更改】按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到【保存更改】按钮", lines);
        }

        lines.Add("点击【保存更改】保存背景图...");
        if (!await options.ClickWhenEnabledAsync(page, FacebookSelectors.SaveChangesButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("保存更改按钮一直不可用");
            return ProfileFieldResult.Failed(FieldName, "保存背景图失败：保存更改按钮一直不可用", lines);
        }

        lines.Add("等待背景图保存...");
        var settled = await options.WaitHiddenAsync(page, FacebookSelectors.SaveChangesButton, cancellationToken)
            .ConfigureAwait(false);
        lines.Add(settled ? "背景图已保存" : "保存更改按钮在超时内仍未消失，按已提交处理");

        return ProfileFieldResult.Changed(FieldName, "背景图修改完成", lines);
    }
}
