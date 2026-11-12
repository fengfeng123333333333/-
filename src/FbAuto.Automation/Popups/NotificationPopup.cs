namespace FbAuto.Automation.Popups;

/// <summary>关闭动作的种类。</summary>
public enum PopupDismissAction
{
    /// <summary>没有弹窗，什么都不用做。**不要**在这种情况下按 Escape —— 会关掉正在编辑的对话框。</summary>
    None = 0,

    /// <summary>点击某个选择器。</summary>
    Click = 1,

    /// <summary>按 Escape 兜底。</summary>
    PressEscape = 2,
}

/// <summary>
/// 一条弹窗关闭规则。
/// </summary>
/// <param name="Id">稳定标识，用于测试与观测对齐。</param>
/// <param name="Description">中文说明，进日志。</param>
/// <param name="Selector">要点的选择器；按 Escape 的规则为空。</param>
/// <param name="Action">动作。</param>
/// <param name="HighConfidence">
/// 是否高置信度。低置信度的规则默认不启用 —— 文案匹配（如 has-text('继续')）误伤面大，
/// 点错地方比不点更糟。
/// </param>
public sealed record PopupDismissRule(
    string Id,
    string Description,
    string Selector,
    PopupDismissAction Action,
    bool HighConfidence = true);

/// <summary>
/// 弹窗关闭规则表，全部来自原版 DLL 字符串证据。
/// </summary>
/// <remarks>
/// 原版三处 <c>CloseNotificationPopupAsync</c>（FacebookFillService d__20 / MessageDeleteService d__17 /
/// ProfileEditService d__36）共用同一段串：
/// <code>
/// div[aria-modal='true'][role='dialog']:has-text('发生了什么')
/// div[aria-label='关闭']
/// Escape
/// : 已点击关闭按钮
/// : 已按 Escape 关闭提示
/// </code>
/// 顺序即优先级：「发生了什么」是异常对话框，先于普通通知处理。
/// </remarks>
public static class PopupDismissCatalog
{
    /// <summary>默认启用的规则，按优先级排列。</summary>
    public static IReadOnlyList<PopupDismissRule> Default { get; } =
    [
        new(
            "dialog-what-happened",
            "异常提示对话框",
            "div[aria-modal='true'][role='dialog']:has-text('发生了什么')",
            PopupDismissAction.Click),

        new(
            "notification-close",
            "通知弹窗关闭按钮",
            "div[aria-label='关闭']",
            PopupDismissAction.Click),
    ];

    /// <summary>
    /// 需要显式开启的规则。
    /// </summary>
    /// <remarks>
    /// 原版对应串是 <c>div[aria-label='[object Object]'] span:has-text('继续')</c> ——
    /// 拼选择器时把对象直接当字符串用了，<c>[object Object]</c> 是 C# 对象 <c>ToString()</c> 的默认输出。
    /// 这条规则在原版里**永远匹配不到任何元素**，属于实实在在的缺陷。
    /// 这里不复刻该缺陷：给出可用的选择器，但默认不启用（文案匹配误伤面大，点错「继续」
    /// 可能跳过本该填的内容）。
    /// </remarks>
    public static IReadOnlyList<PopupDismissRule> OptIn { get; } =
    [
        new(
            "dialog-continue",
            "「继续」按钮",
            "div[role='button'] span:has-text('继续')",
            PopupDismissAction.Click,
            HighConfidence: false),
    ];

    /// <summary>默认规则 + 开启的可选规则。</summary>
    public static IReadOnlyList<PopupDismissRule> Build(bool includeOptIn = false) =>
        includeOptIn ? [.. Default, .. OptIn] : Default;

    /// <summary>按 Id 取规则，找不到返回 null。</summary>
    public static PopupDismissRule? Find(string? id, bool includeOptIn = false) =>
        Build(includeOptIn).FirstOrDefault(r => r.Id == id);
}

/// <summary>
/// 页面上某个弹窗规则的实际观测结果。探测由调用方（Playwright 实现）完成，这里只做决策。
/// </summary>
/// <param name="RuleId">对应 <see cref="PopupDismissRule.Id"/>；未知弹窗可以用任意新 Id。</param>
/// <param name="Visible">是否可见。</param>
/// <param name="Enabled">若可点，是否处于可用状态（disabled 的按钮点不动）。</param>
public sealed record PopupObservation(string RuleId, bool Visible, bool Enabled = true);

/// <summary>
/// 决策出的一步动作。
/// </summary>
public sealed record PopupDismissStep(
    PopupDismissAction Action,
    string? Selector,
    string? RuleId,
    string Reason,
    string LogLine)
{
    public bool IsNoOp => Action == PopupDismissAction.None;
}

/// <summary>
/// 弹窗关闭决策器：给定「页面上现在有什么」，决定「下一步做什么」。
/// </summary>
/// <remarks>
/// 一次只决策一步。因为点掉一个弹窗后页面会变，下一步必须重新观测 —— 一次性排好所有步骤
/// 等于假设页面不会变，真机上必然错位。
/// </remarks>
public static class PopupDismissPlanner
{
    /// <summary>「已点击关闭按钮」（原版文案）。</summary>
    public const string ClickedCloseLine = "已点击关闭按钮";

    /// <summary>「已按 Escape 关闭提示」（原版文案）。</summary>
    public const string PressedEscapeLine = "已按 Escape 关闭提示";

    /// <summary>没有任何弹窗时的日志。</summary>
    public const string NoPopupLine = "未发现弹窗";

    /// <summary>
    /// 决策下一步。
    /// </summary>
    /// <param name="observations">当前观测到的弹窗状态；可为空。</param>
    /// <param name="rules">规则表，null 用默认表。</param>
    public static PopupDismissStep Decide(
        IReadOnlyList<PopupObservation>? observations,
        IReadOnlyList<PopupDismissRule>? rules = null)
    {
        rules ??= PopupDismissCatalog.Default;

        var visible = (observations ?? []).Where(o => o.Visible).ToList();
        if (visible.Count == 0)
        {
            return new PopupDismissStep(PopupDismissAction.None, null, null, "没有可见弹窗", NoPopupLine);
        }

        var blocked = new List<string>();

        // 按规则表顺序找第一个「可见且可用」的 —— 顺序就是优先级。
        foreach (var rule in rules)
        {
            var hit = visible.FirstOrDefault(o => o.RuleId == rule.Id);
            if (hit is null)
            {
                continue;
            }

            if (!hit.Enabled)
            {
                blocked.Add(rule.Description);
                continue;
            }

            if (rule.Action == PopupDismissAction.PressEscape)
            {
                return new PopupDismissStep(
                    PopupDismissAction.PressEscape,
                    null,
                    rule.Id,
                    rule.Description,
                    PressedEscapeLine);
            }

            return new PopupDismissStep(
                PopupDismissAction.Click,
                rule.Selector,
                rule.Id,
                rule.Description,
                $"{ClickedCloseLine}（{rule.Description}）");
        }

        // 有弹窗但没一条规则能点：可能是按钮被禁用，也可能是没见过的弹窗。
        // 两种情况都按 Escape 兜底 —— 原版「找不到关闭按钮就按 Escape」的行为。
        var reason = blocked.Count > 0
            ? $"{string.Join(" / ", blocked)} 不可用，改用 Escape"
            : "弹窗未在规则表中，改用 Escape 兜底";

        return new PopupDismissStep(PopupDismissAction.PressEscape, null, null, reason, PressedEscapeLine);
    }
}

/// <summary>
/// 一轮弹窗清理的记录器：控制最多关几个，防止「关掉一个又冒出一个」时死循环。
/// </summary>
/// <remarks>
/// 上限取 3 —— 原版在同类轮询里用 3 次（串为「3次检测均未找到新号弹窗，继续下一步」）。
/// </remarks>
public sealed class PopupDismissPass
{
    private readonly List<PopupDismissStep> _steps = [];

    public PopupDismissPass(int maxSteps = 3)
    {
        if (maxSteps < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSteps), "上限至少为 1");
        }

        MaxSteps = maxSteps;
    }

    public int MaxSteps { get; }

    public IReadOnlyList<PopupDismissStep> Steps => _steps;

    /// <summary>还能不能继续决策。</summary>
    public bool CanContinue => _steps.Count < MaxSteps;

    /// <summary>是否因为达到上限而停手（此时可能仍有弹窗未关）。</summary>
    public bool ReachedLimit => _steps.Count >= MaxSteps;

    /// <summary>记一步。空操作不计数，否则「没有弹窗」也会把预算耗光。</summary>
    public void Record(PopupDismissStep step)
    {
        if (step.IsNoOp)
        {
            return;
        }

        _steps.Add(step);
    }

    /// <summary>一轮的中文总结。</summary>
    public string BuildSummary()
    {
        if (_steps.Count == 0)
        {
            return "本轮没有需要关闭的弹窗";
        }

        var clicked = _steps.Count(s => s.Action == PopupDismissAction.Click);
        var escaped = _steps.Count(s => s.Action == PopupDismissAction.PressEscape);
        var parts = new List<string>();
        if (clicked > 0)
        {
            parts.Add($"点击关闭 {clicked} 次");
        }

        if (escaped > 0)
        {
            parts.Add($"按 Escape {escaped} 次");
        }

        var tail = ReachedLimit ? $"，已达单轮上限 {MaxSteps}，可能仍有弹窗" : string.Empty;
        return $"本轮关闭弹窗 {_steps.Count} 个（{string.Join("，", parts)}）{tail}";
    }
}
