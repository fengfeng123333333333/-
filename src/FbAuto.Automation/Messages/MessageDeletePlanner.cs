namespace FbAuto.Automation.Messages;

/// <summary>一轮循环该停下还是继续，以及停下的原因。</summary>
public enum MessageDeleteStop
{
    /// <summary>继续删。</summary>
    Continue,

    /// <summary>已达设定上限。</summary>
    LimitReached,

    /// <summary>会话列表已经空了。</summary>
    ListEmpty,

    /// <summary>连续失败次数达到上限，停下来避免空转。</summary>
    TooManyFailures,

    /// <summary>删除中止，轮次超过安全上限。</summary>
    MaxRounds,

    /// <summary>删掉了但列表没变短，疑似页面没刷新或列表是虚拟滚动。</summary>
    NoProgress,
}

/// <summary>一次判定的结论。</summary>
/// <param name="ShouldContinue">是否继续删下一条。</param>
/// <param name="Stop">停下时的原因；继续时是 <see cref="MessageDeleteStop.Continue"/>。</param>
/// <param name="LogLine">要写进任务日志的一句人话。</param>
public sealed record MessageDeleteDecision(bool ShouldContinue, MessageDeleteStop Stop, string LogLine);

/// <summary>
/// 「删除消息」的轮次规划器：只决定「还要不要再来一轮、为什么停」，不碰页面。
/// </summary>
/// <remarks>
/// 删消息是**不可逆**动作，所以退出条件必须显式、可单测，不能靠循环里的临时判断。
/// 四个出口对应四种真实现场：
/// <list type="number">
/// <item><b>已达上限</b>：需求方设的边界。这是主动停，不是出错。</item>
/// <item><b>列表已空</b>：本来就没得删了，也是正常收尾。</item>
/// <item><b>连续失败</b>：页面改版、按钮点不动、被风控挡下 —— 这时候继续跑只会
///   在日志里刷几十条同样的失败，还白耗时间，所以连续 N 条失败就停。</item>
/// <item><b>没有进展</b>：删成功了但列表长度不降。这条是**防死循环的兜底**，
///   正常情况下永远不触发；触发就说明采样方式与真实页面不一致（见类内注释）。</item>
/// </list>
///
/// **上限 0 表示不限**，一直删到列表空为止。默认配 20，是因为删掉的对话在**对方那边也会消失**，
/// 批量账号上一把梭全清没有回头路。
/// </remarks>
public sealed class MessageDeletePlanner
{
    /// <summary>连续失败多少条就停。</summary>
    public const int DefaultMaxConsecutiveFailures = 3;

    /// <summary>
    /// 连续多少次「删成功但剩余数没降」判为没有进展。0 表示关闭这个判断。
    /// </summary>
    /// <remarks>
    /// **默认关闭**，理由是真机上这条会误伤：Messenger 的会话列表是虚拟滚动，
    /// 删掉一条后下面会补一条进来，渲染出来的行数**本来就不变**。
    /// 拿它当退出条件，结果是「删两条就停」，功能等于废掉。
    ///
    /// 那为什么还留着？因为它在另一种场合是对的：如果页面改版到「点了删除但其实没删」，
    /// 数量同样不降，而连续失败判断抓不到（每一步都点成功了）。这时它是唯一的刹车。
    /// 所以做成可选：默认不启用，真机验收确认列表不是虚拟滚动后再打开。
    ///
    /// 定成 2 而不是 1：单次采样有可能刚好撞上列表刷新的中间态。
    /// </remarks>
    public const int DefaultStallThreshold = 0;

    private readonly int _maxConsecutiveFailures;
    private readonly int _stallThreshold;

    private int _stalledSuccesses;
    private int _rounds;

    /// <param name="limit">上限，0 或负数表示不限。</param>
    /// <param name="maxConsecutiveFailures">连续失败多少条就停。</param>
    /// <param name="stallThreshold">连续多少次「删成功但数量没降」判为没有进展；0 关闭。</param>
    /// <param name="maxRounds">
    /// 轮次硬上限。0 表示自动（上限 × 2 + 10；不限条数时 500）。
    /// </param>
    public MessageDeletePlanner(
        int limit,
        int maxConsecutiveFailures = DefaultMaxConsecutiveFailures,
        int stallThreshold = DefaultStallThreshold,
        int maxRounds = 0)
    {
        _maxConsecutiveFailures = Math.Max(1, maxConsecutiveFailures);
        _stallThreshold = Math.Max(0, stallThreshold);

        // 负数等同于不限 —— 配置被手改成 -1 不该表现为「一条都不删」。
        Limit = limit > 0 ? limit : 0;

        MaxRounds = maxRounds > 0
            ? maxRounds
            : Limit > 0 ? (Limit * 2) + 10 : 500;
    }

    /// <summary>生效的上限，0 表示不限。</summary>
    public int Limit { get; }

    /// <summary>生效的轮次硬上限。</summary>
    public int MaxRounds { get; }

    /// <summary>已经走过多少轮（每次 <see cref="Plan"/> 记一轮）。</summary>
    public int Rounds => _rounds;

    /// <summary>已经删掉的条数。</summary>
    public int Deleted { get; private set; }

    /// <summary>连续失败条数（成功一次就清零）。</summary>
    public int ConsecutiveFailures { get; private set; }

    /// <summary>最后一次失败的原因，用于汇总。</summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// 下一轮要不要继续。
    /// </summary>
    /// <param name="remaining">当前可见的会话行数。</param>
    public MessageDeleteDecision Plan(int remaining)
    {
        _rounds++;

        if (Limit > 0 && Deleted >= Limit)
        {
            return new MessageDeleteDecision(
                false,
                MessageDeleteStop.LimitReached,
                $"已达设定上限 {Limit} 条，停止删除（本次共删 {Deleted} 条）");
        }

        if (remaining <= 0)
        {
            return new MessageDeleteDecision(
                false,
                MessageDeleteStop.ListEmpty,
                $"会话列表已空，停止删除（本次共删 {Deleted} 条）");
        }

        if (ConsecutiveFailures >= _maxConsecutiveFailures)
        {
            return new MessageDeleteDecision(
                false,
                MessageDeleteStop.TooManyFailures,
                $"连续 {ConsecutiveFailures} 条删除失败，停止（最后一次：{LastFailure}）");
        }

        // 轮次硬上限：最后的刹车。上限判失效（配置写错、或被改坏）而列表数量又不降时，
        // 前面几个出口全都不会触发，循环会一直转下去 —— 这一条是唯一能兜住的。
        if (_rounds > MaxRounds)
        {
            return new MessageDeleteDecision(
                false,
                MessageDeleteStop.MaxRounds,
                $"已尝试 {_rounds - 1} 轮，超过安全上限 {MaxRounds} 轮，停止（本次删除 {Deleted} 条）。"
                + "页面可能一直在点但没生效，请人工打开收件箱确认。");
        }

        if (_stallThreshold > 0 && _stalledSuccesses >= _stallThreshold)
        {
            return new MessageDeleteDecision(
                false,
                MessageDeleteStop.NoProgress,
                $"连续 {_stalledSuccesses} 次删除后剩余会话数没有下降（当前 {remaining} 条），"
                + "停止以免空转；若真实页面是虚拟滚动列表，这个判断会误触发");
        }

        return new MessageDeleteDecision(
            true,
            MessageDeleteStop.Continue,
            $"继续删除第 {Deleted + 1} 条，当前剩余 {remaining} 条");
    }

    /// <summary>
    /// 记一次删除成功。
    /// </summary>
    /// <param name="remainingBefore">删除前读到的会话行数。</param>
    /// <param name="remainingAfter">删除后读到的会话行数（应已等列表刷新稳定）。</param>
    public void RecordSuccess(int remainingBefore, int remainingAfter)
    {
        Deleted++;
        ConsecutiveFailures = 0;

        // 数量没降不算错，只是记一笔；连续多次才算「没有进展」。
        _stalledSuccesses = remainingAfter >= remainingBefore ? _stalledSuccesses + 1 : 0;
    }

    /// <summary>记一次删除失败。不算进 <see cref="Deleted"/>。</summary>
    public void RecordFailure(string reason)
    {
        ConsecutiveFailures++;
        LastFailure = reason;
    }

    /// <summary>一句话汇总，写进任务结果列。</summary>
    public string Summarize(MessageDeleteStop stop)
    {
        var head = stop switch
        {
            MessageDeleteStop.LimitReached => $"删除完成，已达上限 {Limit} 条",
            MessageDeleteStop.ListEmpty => "删除完成，会话列表已清空",
            MessageDeleteStop.TooManyFailures => "删除中止，连续失败过多",
            MessageDeleteStop.MaxRounds => "删除中止，轮次超过安全上限",
            MessageDeleteStop.NoProgress => "删除中止，列表数量没有下降",
            _ => "删除结束",
        };

        var parts = new List<string> { head, $"实际删除 {Deleted} 条" };
        if (ConsecutiveFailures > 0)
        {
            parts.Add($"失败 {ConsecutiveFailures} 条");
        }

        return string.Join("；", parts);
    }

    /// <summary>
    /// 整批是否算成功。
    /// </summary>
    /// <remarks>
    /// 只有「按设定上限停」与「列表已空」算正常收尾。连续失败与没有进展都属于异常中止 ——
    /// 尤其后者，往往意味着页面已经不是我们认识的那个页面，不能让使用者看到「成功」而不再核对。
    /// </remarks>
    public static bool IsNormalStop(MessageDeleteStop stop) =>
        stop is MessageDeleteStop.LimitReached or MessageDeleteStop.ListEmpty;
}
