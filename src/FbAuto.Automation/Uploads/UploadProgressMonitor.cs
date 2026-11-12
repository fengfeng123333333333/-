namespace FbAuto.Automation.Uploads;

/// <summary>
/// 一轮进度采样。
/// </summary>
/// <param name="BarCount">当前可见的进度条个数（含读不到百分比的）。</param>
/// <param name="ProgressValues">读到的百分比值，0-100。读不到 <c>aria-valuenow</c> 的进度条不出现在这里。</param>
/// <param name="AllCopyrightReview">
/// 页面上剩下的条目全部是「版权审核」状态。
/// 原版串：<c>所有进度条均为版权审核，上传已完成</c>。
/// </param>
/// <param name="Elapsed">从开始监控到这一刻的时间。</param>
/// <param name="FailedCount">
/// 页面上已经出现的失败条目数（带「移除」按钮的那种）。
/// 它参与「上传是否结束」的判定：一批视频全部失败时进度条会直接消失，
/// 如果只看进度条，就只能等到总超时才收场 —— 而这时候早已该判「零成功」了。
/// </param>
public sealed record UploadSample(
    int BarCount,
    IReadOnlyList<int> ProgressValues,
    bool AllCopyrightReview,
    TimeSpan Elapsed,
    int FailedCount = 0);

/// <summary>监控结论。</summary>
public enum UploadTick
{
    /// <summary>继续等。</summary>
    Waiting,

    /// <summary>上传结束（全部消失，或全部转入版权审核）。</summary>
    Completed,

    /// <summary>判定卡住。</summary>
    Stalled,
}

/// <summary>一轮判定结果。日志行直接写进任务日志。</summary>
/// <param name="Tick">结论。</param>
/// <param name="LogLine">中文说明。</param>
public sealed record UploadVerdict(UploadTick Tick, string LogLine)
{
    public bool IsTerminal => Tick != UploadTick.Waiting;

    public static UploadVerdict Waiting(string line) => new(UploadTick.Waiting, line);

    public static UploadVerdict Completed(string line) => new(UploadTick.Completed, line);

    public static UploadVerdict Stalled(string line) => new(UploadTick.Stalled, line);
}

/// <summary>监控阈值。</summary>
/// <param name="ZeroProgressTimeout">
/// 进度一直是 0 的容忍时长。原版是「进度为 0 已超过 90 秒，判定为卡住失败」，取同一个值。
/// </param>
/// <param name="NoChangeTimeout">
/// 进度百分比停止变化的容忍时长。原版按「轮」计数（<c>% 已超过 N 轮</c>），
/// 这里换成时间：轮数取决于轮询间隔，改间隔就得重算轮数，时间不会。
/// </param>
/// <param name="TotalTimeout">
/// 单个批次的兜底上限。计划书要求「按文件大小估 1.5 倍时长」，
/// 所以真正的值应由调用方按素材算完传进来，这里只给一个不会太早触发的默认值。
/// </param>
/// <param name="NoBarTimeout">
/// 「从头到尾没出现过进度条」的容忍时长。
/// </param>
/// <remarks>
/// 这条超时是补一个真实漏洞：原来「没出现过进度条」的分支直接返回「继续等」，
/// 而总超时的检查写在它**后面**，永远执行不到 —— 结果是监控可以无限等下去。
/// 真机上的表现是任务卡住十几分钟一点日志都不再输出；测试里的表现更糟，
/// 是**进程挂死、零输出**（不是失败），排查时极容易误判成环境问题。
/// </remarks>
public sealed record UploadMonitorOptions(
    TimeSpan? ZeroProgressTimeout = null,
    TimeSpan? NoChangeTimeout = null,
    TimeSpan? TotalTimeout = null,
    TimeSpan? NoBarTimeout = null)
{
    public static UploadMonitorOptions Default { get; } = new();

    public TimeSpan EffectiveZeroProgressTimeout => ZeroProgressTimeout ?? TimeSpan.FromSeconds(90);

    public TimeSpan EffectiveNoChangeTimeout => NoChangeTimeout ?? TimeSpan.FromSeconds(120);

    public TimeSpan EffectiveTotalTimeout => TotalTimeout ?? TimeSpan.FromMinutes(15);

    /// <summary>
    /// 从没出现过进度条的容忍时长，默认 90 秒（与「进度一直为 0」同一个量级）。
    /// </summary>
    /// <remarks>
    /// 为什么不是「等到总超时」：15 分钟太久了。没进度条基本只有三种原因 ——
    /// 文件没注入进去、文件被页面直接拒绝、选择器改版。三种都不需要等 15 分钟才看得出来。
    /// </remarks>
    public TimeSpan EffectiveNoBarTimeout => NoBarTimeout ?? TimeSpan.FromSeconds(90);
}

/// <summary>
/// 上传进度监控状态机。输入每轮采样，输出「继续等 / 完成 / 卡住」。
/// </summary>
/// <remarks>
/// 为什么必须是有状态的类而不是纯函数：判定完全依赖「和上一轮比有没有变」。
/// 纯函数只能看到当前快照，看不出「停在 45% 已经两分钟」。
///
/// 四条规则，每条都对应一次真实的误判：
/// <list type="number">
/// <item>
/// **首次采样只记录，不判定**。注入文件后进度条要过几百毫秒才渲染出来，
/// 这时候 <c>BarCount == 0</c>。如果把它当「进度条全部消失 = 上传结束」，
/// 就会在视频一个字节都没传的时候去点发布 —— 原版也有对应的保护
/// （<c>无视频正在上传，等待最后一轮间隔后跳出判断</c>）。
/// </item>
/// <item>
/// **从没出现过进度条不算完成**。注入可能根本没生效（file input 选错、文件被拒），
/// 页面从头到尾没有进度条。这种情况靠 <see cref="UploadMonitorOptions.EffectiveNoBarTimeout"/>
/// 判失败（不是判「已完成」，也不是无限等下去）。
/// </item>
/// <item>
/// **版权审核等同于完成**。FB 的进度条在审核阶段会从百分比变成「版权审核」文案，
/// 此时等百分比消失会一直等到总超时。
/// </item>
/// <item>
/// **进度不变要能说清停在哪**。日志里写「停在 45%」和写「卡住」的排查成本差一个量级。
/// </item>
/// </list>
/// </remarks>
public sealed class UploadProgressMonitor
{
    private readonly UploadMonitorOptions _options;

    private bool _first = true;
    private bool _sawBar;
    private TimeSpan _startedAt;
    private TimeSpan _changedAt;
    private TimeSpan? _zeroSince;
    private string _signature = string.Empty;

    public UploadProgressMonitor(UploadMonitorOptions? options = null)
    {
        _options = options ?? UploadMonitorOptions.Default;
    }

    /// <summary>已经采样了几轮。</summary>
    public int Samples { get; private set; }

    /// <summary>
    /// 喂一轮采样，得到结论。一旦返回终态，后续调用仍会重复返回同样的结论
    /// （调用方应当停止喂）。
    /// </summary>
    public UploadVerdict Observe(UploadSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        Samples++;
        var signature = Signature(sample);

        if (_first)
        {
            _first = false;
            _startedAt = sample.Elapsed;
            _changedAt = sample.Elapsed;
            _signature = signature;
            _sawBar = sample.BarCount > 0;
            _zeroSince = IsAllZero(sample) ? sample.Elapsed : null;

            return UploadVerdict.Waiting($"首次采样：可见进度条 {sample.BarCount} 个，仅记录不判定");
        }

        // 版权审核优先于「进度条消失」：审核阶段可能既没有百分比也没有进度条，
        // 但它意味着上传本身已经成功。
        if (sample.AllCopyrightReview)
        {
            return UploadVerdict.Completed("所有进度条均为版权审核，上传已完成");
        }

        if (sample.BarCount == 0)
        {
            // 两种情况都算「上传流程结束」：
            // ① 先出现过进度条再消失 —— 正常传完；
            // ② 页面上已经出现失败条目 —— 整批直接失败了，进度条根本没长出来。
            //    少了 ② 就得等总超时，而那时该判「零成功」的结论已经晚了几分钟。
            //
            // 「完成」永远优先于任何超时：传完了却因为超时报失败，会让人去查一个不存在的问题。
            if (_sawBar || sample.FailedCount > 0)
            {
                return UploadVerdict.Completed("进度条全部消失，上传流程结束");
            }

            // 从没出现过进度条，且连失败条目都没有：注入大概率没生效。
            // **这条分支必须自己带超时** —— 原来它直接 return「继续等」，而总超时的检查
            // 写在它后面，永远执行不到，监控可以无限等下去（真机卡十几分钟，测试进程挂死）。
            var waited = sample.Elapsed - _startedAt;
            if (waited >= _options.EffectiveNoBarTimeout)
            {
                return UploadVerdict.Stalled(
                    $"已等待 {waited.TotalSeconds:0} 秒仍没有任何进度条，判定注入没有生效"
                    + "（文件可能没被选中、被页面拒绝，或上传区选择器已改版）");
            }

            if (waited >= _options.EffectiveTotalTimeout)
            {
                return UploadVerdict.Stalled(
                    $"上传已进行 {waited.TotalMinutes:0.#} 分钟仍未出现进度条，超过上限 "
                    + $"{_options.EffectiveTotalTimeout.TotalMinutes:0.#} 分钟，判定失败");
            }

            return UploadVerdict.Waiting(
                $"尚未出现进度条，继续等待（超过 {_options.EffectiveNoBarTimeout.TotalSeconds:0} 秒判失败）");
        }

        _sawBar = true;

        var total = sample.Elapsed - _startedAt;
        if (total >= _options.EffectiveTotalTimeout)
        {
            return UploadVerdict.Stalled(
                $"上传已进行 {total.TotalMinutes:0.#} 分钟仍未结束，超过上限 "
                + $"{_options.EffectiveTotalTimeout.TotalMinutes:0.#} 分钟，判定失败");
        }

        if (IsAllZero(sample))
        {
            _zeroSince ??= sample.Elapsed;
            var stuck = sample.Elapsed - _zeroSince.Value;
            if (stuck >= _options.EffectiveZeroProgressTimeout)
            {
                return UploadVerdict.Stalled(
                    $"上传进度为 0 已持续 {stuck.TotalSeconds:0} 秒，超过 "
                    + $"{_options.EffectiveZeroProgressTimeout.TotalSeconds:0} 秒，判定为卡住");
            }
        }
        else
        {
            _zeroSince = null;
        }

        if (signature != _signature)
        {
            _signature = signature;
            _changedAt = sample.Elapsed;
        }
        else if (sample.ProgressValues.Count > 0)
        {
            var unchanged = sample.Elapsed - _changedAt;
            if (unchanged >= _options.EffectiveNoChangeTimeout)
            {
                return UploadVerdict.Stalled(
                    $"上传进度停在 {signature}% 已 {unchanged.TotalSeconds:0} 秒没有变化，判定为卡住");
            }
        }

        return UploadVerdict.Waiting(DescribeWaiting(sample));
    }

    /// <summary>
    /// 采样签名。用逗号拼接而不是求和：<c>50,50</c> 与 <c>100,0</c> 的和都是 100，
    /// 前者是两个都停住、后者是一个传完一个没开始，处置完全不同。
    /// </summary>
    private static string Signature(UploadSample sample) => string.Join(",", sample.ProgressValues);

    /// <summary>
    /// 是否「有进度条且读到的百分比全是 0」。
    /// </summary>
    /// <remarks>
    /// <c>ProgressValues.Count == 0</c> 不算全 0：那是「读不到百分比」，
    /// 可能是版权审核，也可能是 DOM 换了写法，不能当成「一点没动」。
    /// </remarks>
    private static bool IsAllZero(UploadSample sample) =>
        sample.BarCount > 0
        && sample.ProgressValues.Count > 0
        && sample.ProgressValues.All(v => v <= 0);

    private static string DescribeWaiting(UploadSample sample)
    {
        if (sample.ProgressValues.Count == 0)
        {
            return $"上传中：{sample.BarCount} 个进度条（读不到百分比）";
        }

        var done = sample.ProgressValues.Count(v => v >= 100);
        return $"上传中：{string.Join("/", sample.ProgressValues)}%，已完成 {done}/{sample.BarCount}";
    }
}
