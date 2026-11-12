namespace FbAuto.Automation.Selectors;

/// <summary>
/// 真实页面的选择器目录。全部来自原版 DLL 的 UTF-16 字符串还原（2026-11-12 重扫
/// <c>_orig/FaceBook自动化V1.1.1_1/FaceBook自动化.dll</c>，产物 <c>_analysis/utf16_strings.txt</c>）。
/// </summary>
/// <remarks>
/// **不要凭印象改这里的任何一条**。这些串是能在真页面上命中的原文；改一个字符就等于换了个选择器。
/// <c>docs/evidence/extracted/*.cn.txt</c> 里的中文是坏的（UTF-16LE 字节被按单字节解成
/// <c>慦嬀栀爀攀昀</c> 这种），在里面 grep 找不到东西，必须回原始 DLL 重扫。
///
/// 关于 <c>:has-text()</c>：这是 Playwright 的 CSS 扩展，**子串匹配且祖先也命中**。
/// 所以 <c>div[role='button']:has-text('移除')</c> 会同时匹配「包含移除按钮的外层 div」。
/// 依赖这类选择器做「第 n 个」定位时，序号必须以 <see cref="Abstractions.IPageInterop"/> 的
/// 「可见匹配顺序」为准，且真机验收时要确认页面上没有多余的同名按钮。
/// </remarks>
public static class FacebookSelectors
{
    // ── 资料修改：封面与头像 ───────────────────────────────────────────────

    /// <summary>
    /// 背景图（封面）的 file input。
    /// </summary>
    /// <remarks>原版串：<c>div:has(> div[role='button']:has-text('封面照片')) &gt; input[type='file']</c></remarks>
    public const string CoverPhotoFileInput =
        "div:has(> div[role='button']:has-text('封面照片')) > input[type='file']";

    /// <summary>「编辑封面照片」入口按钮。原版在定位 file input 之前先点它。</summary>
    public const string EditCoverPhotoButton = "div[role='button']:has-text('封面照片')";

    /// <summary>头像区域（点开后才出现「选择头像」菜单项）。原版串：<c>[aria-label='头像操作']</c></summary>
    public const string AvatarArea = "[aria-label='头像操作']";

    /// <summary>头像菜单项。原版串：<c>div[role='menuitem']:has-text('选择头像')</c></summary>
    public const string ChooseAvatarMenuItem = "div[role='menuitem']:has-text('选择头像')";

    /// <summary>
    /// 头像的 file input（点开「上传照片」对话框之后才在 DOM 里）。
    /// </summary>
    /// <remarks>原版串：<c>div:has(> div[aria-label='上传照片']) &gt; input[type='file']</c></remarks>
    public const string AvatarFileInput = "div:has(> div[aria-label='上传照片']) > input[type='file']";

    // ── 视频上传 ───────────────────────────────────────────────────────────

    /// <summary>批量上传页的通用 file input。原版串：<c>input[type='file']</c>，另有一份 JS 写法。</summary>
    public const string VideoFileInput = "input[type='file']";

    /// <summary>
    /// 「添加视频」按钮。原版在注入文件前先点它，再走 JS 注入。
    /// </summary>
    /// <remarks>
    /// 原版串只剩 <c>div[role='button']</c>（后半截文案被 UTF-16 扫描切断），
    /// 「已点击添加视频按钮 / 没能点击添加视频按钮」两句日志证明它按文案定位。
    /// 真机验收时要确认页面上没有「添加视频和照片」这类更长的同名按钮先出现。
    /// </remarks>
    public const string AddVideoButton = "div[role='button']:has-text('添加视频')";

    /// <summary>
    /// 批量上传页的描述输入框。原版串：<c>[aria-label*='在对话框中输入内容']</c>
    /// </summary>
    /// <remarks>
    /// 注意是 <c>*=</c> 前缀包含，不是全等：真实 aria-label 后面还带别的词。
    /// 这个框是 <c>contenteditable</c>，必须用 fill 而不是逐字符输入。
    /// </remarks>
    public const string DescriptionInput = "[aria-label*='在对话框中输入内容']";

    /// <summary>排期面板里的「立即发布」选项。原版串：<c>div[role='button']:has-text('立即发布')</c></summary>
    public const string ScheduleImmediateButton = "div[role='button']:has-text('立即发布')";

    /// <summary>排期面板里的「更新」选项（更新已发布的帖子）。原版串：<c>div[role='button']:has-text('更新')</c></summary>
    public const string ScheduleUpdateButton = "div[role='button']:has-text('更新')";

    /// <summary>排期面板里的「定时」选项。原版串：<c>div[role='button']:has-text('定时')</c></summary>
    public const string ScheduleOptionButton = "div[role='button']:has-text('定时')";

    /// <summary>定时日期输入框。原版串：<c>[placeholder='年-月-日']</c>，格式 <c>yyyy-M-d</c></summary>
    public const string ScheduleDateInput = "[placeholder='年-月-日']";

    /// <summary>定时小时输入框。原版串：<c>[aria-label='小时']</c></summary>
    public const string ScheduleHourInput = "[aria-label='小时']";

    /// <summary>
    /// 最终提交按钮。
    /// </summary>
    /// <remarks>
    /// **有意偏离原版**：原版串是 <c>div[role='button']:has-text('发布')</c>，
    /// 而 <c>:has-text()</c> 是子串匹配 —— 它会同时命中「立即发布」「定时发布」这类选项，
    /// 于是「点发布」很可能点到排期选项上（表现是：点了没反应，或者排期面板被重新打开）。
    /// 这里改用 <c>:text-is()</c> 要求整段文本就是「发布」。
    /// 真机上若按钮文案带前后缀（图标 alt 会并入文本），需要回来放宽成
    /// <c>:has-text('发布')</c> 并配「取最后一个可见匹配」。
    /// </remarks>
    public const string PublishButton = "div[role='button']:text-is('发布')";

    /// <summary>发布成功后的「完成」按钮。原版串：<c>div[role='button']:has-text('完成')</c></summary>
    public const string DoneButton = "div[role='button']:has-text('完成')";

    /// <summary>视频类别下拉（combobox）。原版串：<c>input[role='combobox'][aria-label='类别']</c></summary>
    public const string CategoryCombobox = "input[role='combobox'][aria-label='类别']";

    /// <summary>上传进度条。原版串：<c>div[role='progressbar'][aria-valuemax='100'][aria-valuemin='0']</c></summary>
    public const string UploadProgressBar =
        "div[role='progressbar'][aria-valuemax='100'][aria-valuemin='0']";

    /// <summary>开启通知的开关。原版串：<c>input[aria-label='开启通知的开关'][role='switch']</c></summary>
    public const string NotifySwitch = "input[aria-label='开启通知的开关'][role='switch']";

    /// <summary>营销邮件开关。原版串：<c>input[aria-label='营销邮件开关'][role='switch']</c></summary>
    public const string MarketingSwitch = "input[aria-label='营销邮件开关'][role='switch']";

    /// <summary>化名输入框。原版串：<c>input[name='pseudonymous_name']</c></summary>
    public const string PseudonymousNameInput = "input[name='pseudonymous_name']";

    // ── 失败草稿清理 ───────────────────────────────────────────────────────

    /// <summary>
    /// 失败条目的「移除」按钮。原版串：<c>div[role='button']:has-text('移除')</c>
    /// </summary>
    /// <remarks>
    /// 注意这里的陷阱：<c>:has-text('移除')</c> 也会命中「移除视频」这个文案。
    /// 若真页面上同时出现「移除」与「移除视频」按钮，序号会错位。
    /// 真机验收时必须先确认失败列表区域内只有一种「移除」按钮。
    /// </remarks>
    public const string RemoveButton = "div[role='button']:has-text('移除')";

    /// <summary>二次确认里的「移除视频」单选项。原版串：<c>div[role='radio'] span:has-text('移除视频')</c></summary>
    public const string RemoveVideoOption = "div[role='radio'] span:has-text('移除视频')";

    /// <summary>
    /// 二次确认里的「继续」按钮。原版串：<c>div[role='button'] span:has-text('继续')</c>
    /// </summary>
    /// <remarks>
    /// 原版用的是 <c>div[aria-label='[object Object]'] span:has-text('继续')</c> ——
    /// 拼选择器时把对象直接当了字符串，这条在原版里永远匹配不到。这里给的是可用版本。
    /// 页面上同时存在多个「继续」是常态，所以按序号点击，序号由
    /// <see cref="Drafts.DraftRemovalConfirmPlanner"/> 决定。
    /// </remarks>
    public const string ContinueButton = "div[role='button'] span:has-text('继续')";

    /// <summary>
    /// 二次确认对话框的判定依据：出现「移除视频」选项即视为对话框已弹出。
    /// </summary>
    public const string RemovalConfirmDialog = "div[role='dialog']:has(div[role='radio'] span:has-text('移除视频'))";
}
