namespace FbAuto.Automation.Drafts;

/// <summary>
/// 页面上某个失败草稿位置的可操作状态。
/// </summary>
public enum DraftPositionState
{
    /// <summary>有移除按钮且可用。</summary>
    Removable = 0,

    /// <summary>页面上找不到对应位置的移除按钮。</summary>
    NotFound = 1,

    /// <summary>按钮存在但不可用（disabled）。</summary>
    Disabled = 2,
}

/// <summary>
/// 一个待清理的失败草稿位置。位置是页面上失败条目列表里的序号（1 起）。
/// </summary>
/// <param name="Position">序号，1 起。</param>
/// <param name="State">探测到的状态。</param>
/// <param name="Label">可选的文件名/描述，只用于日志可读性。</param>
public sealed record DraftTarget(int Position, DraftPositionState State, string? Label = null);

/// <summary>清理步骤的种类。</summary>
public enum DraftCleanupOutcome
{
    ClickRemove = 0,
    SkipNotFound = 1,
    SkipDisabled = 2,
    SkipInvalidPosition = 3,
}

/// <summary>清理计划里的一步。</summary>
public sealed record DraftCleanupStep(int Position, DraftCleanupOutcome Outcome, string LogLine)
{
    public bool ShouldClick => Outcome == DraftCleanupOutcome.ClickRemove;
}

/// <summary>
/// 一次失败草稿清理的计划。
/// </summary>
public sealed record DraftCleanupPlan(IReadOnlyList<DraftCleanupStep> Steps)
{
    /// <summary>要点击的位置，**已按倒序**，照这个顺序执行即可。</summary>
    public IReadOnlyList<int> ClickOrder =>
        Steps.Where(s => s.ShouldClick).Select(s => s.Position).ToList();

    public int ClickCount => Steps.Count(s => s.ShouldClick);

    public int SkippedCount => Steps.Count(s => !s.ShouldClick);

    /// <summary>按执行顺序排列的日志行。</summary>
    public IReadOnlyList<string> LogLines => Steps.Select(s => s.LogLine).ToList();

    /// <summary>收尾总结，与原版文案对齐。</summary>
    public string Summary => ClickCount > 0
        ? $"移除操作完成，共点击 {ClickCount} 个"
        : "没有可移除的失败视频";
}

/// <summary>
/// 失败草稿（失败视频）清理计划器。
/// </summary>
/// <remarks>
/// 行为依据原版 <c>FacebookFillService.DeleteFailedVideosAsync</c> 的字符串：
/// <code>
/// div[role='button']:has-text('移除')
/// : 位置 {n} 没有找到对应移除按钮，跳过
/// {n} 移除按钮不可用，跳过
/// : 已点击移除位置 {n}
/// {n} 的失败视频（下一轮轮询自动确认）
/// : 删除位置 {n} 时出错:
/// : 移除操作完成，共点击 {k} 个
/// </code>
///
/// **一处有意修正**：原版按位置从小到大点。但移除一个条目后，后面的条目会整体前移一位，
/// 正序执行等于「点掉第 1 个后，原来的第 2 个变成了第 1 个，再去点第 2 个就点到了第 3 个」。
/// 条目数 ≥3 时几乎必然删错视频。这里改为**倒序**执行：从最大的位置往前删，
/// 前移只影响已经处理过的位置，不会错位。该行为由单测锁死。
/// </remarks>
public static class FailedDraftCleanupPlanner
{
    /// <summary>「位置 {n} 没有找到对应移除按钮，跳过」</summary>
    public static string NotFoundLine(int position) => $"位置 {position} 没有找到对应移除按钮，跳过";

    /// <summary>「{n} 移除按钮不可用，跳过」</summary>
    public static string DisabledLine(int position) => $"{position} 移除按钮不可用，跳过";

    /// <summary>「已点击移除位置 {n}」</summary>
    public static string ClickLine(int position) => $"已点击移除位置 {position}";

    /// <summary>「位置 {n} 非法，跳过」</summary>
    public static string InvalidLine(int position) => $"位置 {position} 非法，跳过";

    /// <summary>
    /// 生成清理计划。位置重复只算一次（同一个位置点两次会误删下一条）。
    /// </summary>
    public static DraftCleanupPlan Plan(IEnumerable<DraftTarget>? targets)
    {
        var steps = new List<DraftCleanupStep>();
        if (targets is null)
        {
            return new DraftCleanupPlan(steps);
        }

        var seen = new HashSet<int>();
        var unique = new List<DraftTarget>();

        foreach (var target in targets)
        {
            if (target is null)
            {
                continue;
            }

            if (!seen.Add(target.Position))
            {
                continue;
            }

            unique.Add(target);
        }

        // 倒序：先删靠后的，避免前面的删除导致后面的位置前移。
        foreach (var target in unique.OrderByDescending(t => t.Position))
        {
            if (target.Position <= 0)
            {
                steps.Add(new DraftCleanupStep(target.Position, DraftCleanupOutcome.SkipInvalidPosition, InvalidLine(target.Position)));
                continue;
            }

            switch (target.State)
            {
                case DraftPositionState.Removable:
                    steps.Add(new DraftCleanupStep(target.Position, DraftCleanupOutcome.ClickRemove, ClickLine(target.Position)));
                    break;

                case DraftPositionState.Disabled:
                    steps.Add(new DraftCleanupStep(target.Position, DraftCleanupOutcome.SkipDisabled, DisabledLine(target.Position)));
                    break;

                default:
                    steps.Add(new DraftCleanupStep(target.Position, DraftCleanupOutcome.SkipNotFound, NotFoundLine(target.Position)));
                    break;
            }
        }

        return new DraftCleanupPlan(steps);
    }
}

/// <summary>
/// 点掉「移除」之后弹出的二级确认决策。
/// </summary>
/// <param name="CanProceed">两样都齐了才能继续。</param>
/// <param name="OptionIndex">「移除视频」单选项的下标（0 起）。</param>
/// <param name="ConfirmIndex">确认按钮的下标（0 起）。</param>
/// <param name="LogLine">日志行。</param>
public sealed record DraftRemovalConfirmDecision(
    bool CanProceed,
    int OptionIndex,
    int ConfirmIndex,
    string LogLine);

/// <summary>
/// 「移除视频」二级确认的决策。
/// </summary>
/// <remarks>
/// 原版串：
/// <code>
/// : 选择 / 移除视频 / 选项... / div[role='radio'] span:has-text('移除视频')
/// : 未找到 / 移除视频
/// : 已选择 / 移除视频
/// : 点击 / 确认继续按钮
/// : 第 {n} 个继续按钮未找到，跳过...
/// : 已点击第 {n} 个继续按钮
/// 继续按钮({n})
/// </code>
/// 说明原版要点「移除视频」单选，再点**第 n 个**继续按钮 —— 页面上同时存在多个「继续」是常态，
/// 按序号对应是必须的，否则会点到上一个对话框残留的按钮。
/// </remarks>
public static class DraftRemovalConfirmPlanner
{
    /// <summary>「移除视频」选项文案。</summary>
    public const string RemoveOptionText = "移除视频";

    /// <summary>「继续」按钮文案。</summary>
    public const string ContinueButtonText = "继续";

    public static DraftRemovalConfirmDecision Decide(int removeOptionCount, int continueButtonCount)
    {
        if (removeOptionCount <= 0)
        {
            return new DraftRemovalConfirmDecision(false, -1, -1, $"未找到 {RemoveOptionText}");
        }

        if (continueButtonCount <= 0)
        {
            return new DraftRemovalConfirmDecision(false, 0, -1, "第 1 个继续按钮未找到，跳过...");
        }

        // 取第 1 个可见的选项与第 1 个确认按钮。序号 0 起，日志里用 1 起更符合人读习惯。
        return new DraftRemovalConfirmDecision(
            true,
            0,
            0,
            $"已选择 {RemoveOptionText}；点击 确认继续按钮（第 1 个）");
    }
}
