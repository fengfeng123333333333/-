using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Selectors;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 类别：打开类别页 → 判断是否已经是目标类别 → 点编辑 → 搜索 → 选下拉项 → 保存。
/// </summary>
/// <remarks>
/// 流程照原版串还原，顺序一步不差：
/// <code>
/// &amp;sk=directory_category   →  : 跳转类别设置页面
/// div[dir='auto']:has-text('戏剧')  →  : 当前类别已是 / : 当前类别不是
/// [aria-label='编辑类别']      →  : 已点击编辑类别
/// input[role='combobox'][aria-label='类别']  →  : 在输入框中输入
/// Control+a / Backspace        →  : 已输入
/// div[role='option']:has-text('戏剧'), li[role='option']:has-text('戏剧')  →  : 已选择
/// div[role='button'][aria-label='保存']  →  : 类别修改完成
/// </code>
///
/// 两个容易搞错的地方：
/// <list type="number">
/// <item><b><c>div[dir='auto']:has-text(...)</c> 是用来判「当前已是」的，不是选项</b>。
/// 按它去点会点到页面上展示当前类别的那段文本上 —— 点了没反应，然后一直等到超时。</item>
/// <item>选项是搜索引擎出来的 <c>[role='option']</c>，而且原版把 <c>div</c> 与 <c>li</c>
/// 两种标签都写上（同一个下拉在不同版本里标签不同）。这里保持一致。</item>
/// </list>
///
/// 「当前已是就跳过」不是省事，是省一次可见动作：每一步点击都在 FB 的视野里。
/// </remarks>
public static class CategoryEditor
{
    public const string FieldName = "类别";

    /// <summary>
    /// 原版跳过串，本项未勾选时打这一句。
    /// </summary>
    /// <remarks>
    /// 类别为空走的是「类别为空，跳过该项」，两者别混：
    /// 前者是「这次不打算改」，后者是「想改但没料」。
    /// </remarks>
    public const string SkipLine = "已跳过【修改类别】";

    /// <summary>
    /// 原版硬编码的类别值。
    /// </summary>
    /// <remarks>
    /// 证据：原版 DLL 里类别**没有配置键**（两份 settings.json 的 22 个键里都没有类别），
    /// 也没有界面输入框，选择器与日志里直接写死这一句
    /// （<c>div[role='option']:has-text('戏剧')</c>、<c>未找到【戏剧】选项</c>、<c>已选择【戏剧】</c>）。
    ///
    /// 本实现把它做成可配置（<c>ProfileCategory</c> / <c>BitProfileCategory</c>），
    /// 但**留空时回落到这个词**，而不是「跳过」——原版的语义就是永远按戏剧改，
    /// 留空即跳过等于让勾了「修改类别」的任务永远什么都不做。
    /// </remarks>
    public const string DefaultCategory = "戏剧";

    /// <summary>
    /// 把配置里的类别值解析成真正要填的值：留空回落到 <see cref="DefaultCategory"/>。
    /// </summary>
    /// <remarks>
    /// 抽成纯函数是为了能测：<see cref="RunAsync"/> 要真页面，测不了；
    /// 而「留空到底算跳过还是算戏剧」这个判断恰恰是最容易写反的一处。
    /// </remarks>
    public static string ResolveTarget(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? DefaultCategory : configured.Trim();

    public static async Task<ProfileFieldResult> RunAsync(
        IPageInterop page,
        string profileId,
        string? category,
        ProfileEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        options ??= ProfileEditOptions.Default;

        if (string.IsNullOrWhiteSpace(category))
        {
            return ProfileFieldResult.Skipped(FieldName, "类别为空，跳过该项");
        }

        var lines = new List<string>();
        var target = category.Trim();

        await page.NavigateAsync(
            FacebookUrls.Format(options.EffectiveCategoryUrlTemplate, profileId),
            cancellationToken).ConfigureAwait(false);
        lines.Add($"跳转类别设置页面: profile.php?id={profileId}&sk=directory_category");

        // 先等页面画出来再判「当前类别」。反过来的话，导航刚完成、React 还没渲染，
        // 一定会判成「不是目标类别」，于是每次任务都白改一遍。
        if (!await options.WaitVisibleAsync(page, FacebookSelectors.EditCategoryButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到编辑类别按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到编辑类别按钮", lines);
        }

        if (await page.IsVisibleAsync(CurrentCategorySelector(target), cancellationToken).ConfigureAwait(false))
        {
            var message = $"当前类别已是「{target}」，跳过";
            lines.Add(message);
            return ProfileFieldResult.Skipped(FieldName, message);
        }

        lines.Add($"当前类别不是「{target}」，继续修改");

        await page.ClickAsync(FacebookSelectors.EditCategoryButton, 0, cancellationToken).ConfigureAwait(false);
        lines.Add("已点击编辑类别按钮");

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.CategorySearchInput, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到类别搜索输入框");
            return ProfileFieldResult.Failed(FieldName, "未找到类别搜索输入框", lines);
        }

        // 原版是先 Control+a 再 Backspace 清空，再输入。fill 的语义就是「清空并填值」，
        // 一步等价；逐字敲反而会在中文输入法下多出拼音前缀。
        await page.FillAsync(
            FacebookSelectors.CategorySearchInput,
            target,
            0,
            cancellationToken).ConfigureAwait(false);
        lines.Add($"已输入: {target}");

        var option = OptionSelector(target);
        if (!await options.WaitVisibleAsync(page, option, cancellationToken).ConfigureAwait(false))
        {
            var message = $"未找到下拉项「{target}」";
            lines.Add(message);
            return ProfileFieldResult.Failed(FieldName, message, lines);
        }

        await page.ClickAsync(option, 0, cancellationToken).ConfigureAwait(false);
        lines.Add($"已选择类别: {target}");

        if (!await options.WaitVisibleAsync(page, FacebookSelectors.SaveButton, cancellationToken)
                .ConfigureAwait(false))
        {
            lines.Add("未找到保存按钮");
            return ProfileFieldResult.Failed(FieldName, "未找到保存按钮", lines);
        }

        var saved = await options.ClickWhenEnabledAsync(page, FacebookSelectors.SaveButton, cancellationToken)
            .ConfigureAwait(false);

        if (!saved)
        {
            lines.Add("保存按钮一直不可用");
            return ProfileFieldResult.Failed(FieldName, "保存按钮一直不可用", lines);
        }

        lines.Add("已点击保存");
        return ProfileFieldResult.Changed(FieldName, "类别修改完成", lines);
    }

    /// <summary>
    /// 下拉项选择器。原版是 <c>div</c> 与 <c>li</c> 两种标签的并集。
    /// </summary>
    /// <remarks>
    /// 值里的双引号与反斜杠必须转义：不转义会拼出语法非法的选择器，
    /// Playwright 报的是「选择器无效」，很难往类别值上想。
    /// </remarks>
    public static string OptionSelector(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var escaped = Escape(category);
        return $"div[role='option']:has-text(\"{escaped}\"), li[role='option']:has-text(\"{escaped}\")";
    }

    /// <summary>
    /// 「当前类别」的判定选择器：页面上存在这个文本，就说明已经是目标类别。
    /// </summary>
    public static string CurrentCategorySelector(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        return $"div[dir='auto']:has-text(\"{Escape(category)}\")";
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
