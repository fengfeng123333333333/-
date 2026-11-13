using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 粉丝公开内容：跳设置页 → 把屏蔽关键词逐个敲进标签框（每个词一次回车）→ 点保存。
/// </summary>
/// <remarks>
/// 顺序与文案全部来自原版串（<c>_analysis/u16_all.txt</c> 3307–3345 行）：
/// <code>
/// : 跳转到粉丝公开内容设置页...  settings?tab=followers_and_public_content
/// textarea[id='CometUSFTagInputFreeformDialogTagEditor'][placeholder*='限 1000']
/// : 未找到关键词输入框，跳过关键词设置
/// : 输入屏蔽词...
/// .com  https  pn  dn  inbox
/// : 共 N 个屏蔽词:
/// Enter
/// : 已添加屏蔽词: X
/// : 点击保存按钮...
/// : 已点击保存
/// : 未找到保存按钮，尝试按 Escape 关闭...
/// : 粉丝公开内容设置完成
/// : 修改粉丝公开内容失败:
/// </code>
///
/// **这里才是屏蔽词的归属**。它是「资料修改」的一项，跟视频上传没有任何关系 ——
/// 早先（S10）把它放进了视频上传页，是因为只看到配置键名 <c>CommentsBlockText</c>
/// 就按「评论」去猜用途，没有回证据看它出现在哪一段串里。已改正。
///
/// **开关部分没做**。同一段串里有 <c>input[role='switch'][aria-checked]</c> 的碎片
/// （被 UTF-16 拆得读不出完整选择器），无法确认它控制的是哪几个开关、目标值是什么，
/// 硬猜会变成「点了不知道哪来的开关」。只做关键词，开关留作未完成项。
/// </remarks>
public static class FollowersContentEditor
{
    public const string FieldName = "粉丝公开内容";

    /// <summary>原版跳过时的文案。</summary>
    public const string SkipLine = "已跳过【修改粉丝公开内容】";

    /// <summary>输入框找不到时的文案。注意原版说的是「跳过关键词设置」，不是整项失败。</summary>
    public const string NoInputLine = "未找到关键词输入框，跳过关键词设置";

    public static async Task<ProfileFieldResult> RunAsync(
        IPageInterop page,
        IReadOnlyList<string>? blockWords,
        ProfileEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= ProfileEditOptions.Default;

        // 去空白、去重，保持配置里的顺序。
        var words = (blockWords ?? [])
            .Select(w => w?.Trim())
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (words.Length == 0)
        {
            return ProfileFieldResult.Skipped(FieldName, SkipLine);
        }

        var lines = new List<string>();

        await page.NavigateAsync(
            options.EffectiveFollowersContentUrl,
            cancellationToken).ConfigureAwait(false);
        lines.Add("跳转到粉丝公开内容设置页...");

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.FollowersKeywordInput, cancellationToken)
                .ConfigureAwait(false))
        {
            // 不算失败：原版也是「跳过关键词设置」继续往下走。
            // 判失败会让整条资料修改汇总成「有 1 项失败」，而实际上只少配了几个词。
            lines.Add(NoInputLine);
            return ProfileFieldResult.Skipped(FieldName, NoInputLine, lines);
        }

        lines.Add("输入屏蔽词...");
        foreach (var word in words)
        {
            await page.FillAsync(FacebookSelectors.FollowersKeywordInput, word, 0, cancellationToken)
                .ConfigureAwait(false);

            // 回车这一步是整项的关键：标签框只填不回车，词会留在输入框里，
            // 下一页或保存后一起丢掉，而日志上看不出任何异常。
            await page.PressKeyAsync(FacebookSelectors.FollowersKeywordInput, "Enter", 0, cancellationToken)
                .ConfigureAwait(false);

            lines.Add($"已添加屏蔽词: {word}");
        }

        lines.Add($"共 {words.Length} 个屏蔽词: {string.Join(", ", words)}");

        await SaveAsync(page, options, lines, cancellationToken).ConfigureAwait(false);

        lines.Add("粉丝公开内容设置完成");
        return ProfileFieldResult.Changed(FieldName, "粉丝公开内容设置完成", lines);
    }

    /// <summary>
    /// 点保存；没有保存按钮就按 Escape 关掉面板。
    /// </summary>
    /// <remarks>
    /// **保存按钮不存在不判失败**，依据是原版串的顺序：
    /// 「未找到保存按钮，尝试按 Escape 关闭...」后面紧跟着「粉丝公开内容设置完成」。
    /// 原版自己就把这种情况当成功 —— 这一页的标签是即时生效的，保存按钮只关面板。
    /// </remarks>
    private static async Task SaveAsync(
        IPageInterop page,
        ProfileEditOptions options,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        lines.Add("点击保存按钮...");
        if (await options.WaitVisibleAsync(page, FacebookSelectors.SaveButton, cancellationToken)
                .ConfigureAwait(false))
        {
            await page.ClickAsync(FacebookSelectors.SaveButton, 0, cancellationToken).ConfigureAwait(false);
            lines.Add("已点击保存");
            return;
        }

        lines.Add("未找到保存按钮，尝试按 Escape 关闭...");
        await page.PressEscapeAsync(cancellationToken).ConfigureAwait(false);
    }
}
