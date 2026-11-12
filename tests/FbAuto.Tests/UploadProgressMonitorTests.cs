using FbAuto.Automation.Uploads;

namespace FbAuto.Tests;

/// <summary>
/// 上传进度监控。这是原版「三个会卡死的判断点」里的第一个。
/// 最贵的错误不是「卡住没发现」，而是**视频还没开始传就判成完成**，
/// 那会直接点发布，发出一条没有视频的空帖。
/// </summary>
public sealed class UploadProgressMonitorTests
{
    private static UploadSample Sample(double seconds, int barCount, int[] values, bool copyright = false) =>
        new(barCount, values, copyright, TimeSpan.FromSeconds(seconds));

    private static UploadProgressMonitor NewMonitor(
        TimeSpan? zero = null,
        TimeSpan? noChange = null,
        TimeSpan? total = null,
        TimeSpan? noBar = null) =>
        new(new UploadMonitorOptions(zero, noChange, total, noBar));

    [Fact]
    public void 首次采样即使没有进度条也不算完成()
    {
        var monitor = NewMonitor();

        // 注入文件后进度条还没渲染，这一刻页面上的进度条数是 0。
        // 若按「进度条全部消失 = 上传结束」直接判，就会在视频一个字节都没传时去点发布。
        var verdict = monitor.Observe(Sample(0, barCount: 0, values: []));

        Assert.Equal(UploadTick.Waiting, verdict.Tick);
        Assert.Contains("首次采样", verdict.LogLine);
    }

    [Fact]
    public void 从没出现过进度条时不判完成()
    {
        var monitor = NewMonitor(total: TimeSpan.FromSeconds(30));

        monitor.Observe(Sample(0, 0, []));
        var second = monitor.Observe(Sample(5, 0, []));

        // 注入没生效时页面从头到尾没有进度条。这种「一直空」不能算成功。
        Assert.Equal(UploadTick.Waiting, second.Tick);
        Assert.Contains("尚未出现进度条", second.LogLine);
    }

    /// <summary>
    /// 从头到尾没有进度条，超过容忍时长要判失败。
    /// </summary>
    /// <remarks>
    /// 这条防的是一个真实挂死：原实现里「没有进度条」的分支在总超时检查**之前**就 return 了，
    /// 总超时永远轮不到执行，监控可以无限等。真机上表现为任务卡十几分钟不再输出日志，
    /// 测试里表现为**进程挂死、零输出** —— 后者尤其容易误判成环境问题。
    /// </remarks>
    [Fact]
    public void 从没出现过进度条超过容忍时长判失败()
    {
        var monitor = NewMonitor(noBar: TimeSpan.FromSeconds(10));

        monitor.Observe(Sample(0, 0, []));
        var verdict = monitor.Observe(Sample(11, 0, []));

        Assert.Equal(UploadTick.Stalled, verdict.Tick);
        Assert.Contains("没有任何进度条", verdict.LogLine);
        Assert.Contains("注入没有生效", verdict.LogLine);
    }

    /// <summary>
    /// 没进度条的分支同样受总超时约束，且哪一个先到就按哪一个判。
    /// </summary>
    [Fact]
    public void 总超时对没有进度条的分支同样生效()
    {
        // 总超时 5 秒比 NoBar 的 10 秒更早到
        var monitor = NewMonitor(total: TimeSpan.FromSeconds(5), noBar: TimeSpan.FromSeconds(10));

        monitor.Observe(Sample(0, 0, []));
        var verdict = monitor.Observe(Sample(6, 0, []));

        Assert.Equal(UploadTick.Stalled, verdict.Tick);
        Assert.Contains("超过上限", verdict.LogLine);
    }

    [Fact]
    public void 进度条消失且之前出现过_判完成()
    {
        var monitor = NewMonitor();

        monitor.Observe(Sample(0, 1, [10]));
        monitor.Observe(Sample(5, 1, [100]));
        var verdict = monitor.Observe(Sample(10, 0, []));

        Assert.Equal(UploadTick.Completed, verdict.Tick);
        Assert.Contains("进度条全部消失", verdict.LogLine);
    }

    [Fact]
    public void 版权审核等同完成()
    {
        var monitor = NewMonitor();

        monitor.Observe(Sample(0, 1, [10]));
        var verdict = monitor.Observe(Sample(5, barCount: 1, values: [], copyright: true));

        Assert.Equal(UploadTick.Completed, verdict.Tick);
        Assert.Contains("版权审核", verdict.LogLine);
    }

    [Fact]
    public void 进度一直是0_超过90秒判卡住()
    {
        var monitor = NewMonitor(zero: TimeSpan.FromSeconds(90));

        monitor.Observe(Sample(0, 1, [0]));
        var stillWaiting = monitor.Observe(Sample(89, 1, [0]));
        var stalled = monitor.Observe(Sample(91, 1, [0]));

        Assert.Equal(UploadTick.Waiting, stillWaiting.Tick);
        Assert.Equal(UploadTick.Stalled, stalled.Tick);
        Assert.Contains("进度为 0", stalled.LogLine);
    }

    [Fact]
    public void 进度停在同一个值_超过阈值判卡住并写清停在几()
    {
        var monitor = NewMonitor(noChange: TimeSpan.FromSeconds(120));

        monitor.Observe(Sample(0, 1, [45]));
        var stillWaiting = monitor.Observe(Sample(60, 1, [45]));
        var stalled = monitor.Observe(Sample(121, 1, [45]));

        Assert.Equal(UploadTick.Waiting, stillWaiting.Tick);
        Assert.Equal(UploadTick.Stalled, stalled.Tick);

        // 日志必须能定位：只写「卡住」，排查时不知道是没开始还是停在半路。
        Assert.Contains("45%", stalled.LogLine);
    }

    [Fact]
    public void 进度有变化就重新计时()
    {
        var monitor = NewMonitor(noChange: TimeSpan.FromSeconds(120));

        monitor.Observe(Sample(0, 1, [10]));
        monitor.Observe(Sample(119, 1, [10]));
        monitor.Observe(Sample(120, 1, [20]));
        var verdict = monitor.Observe(Sample(200, 1, [20]));

        // 从 120 秒那刻重新计时，到 200 只过了 80 秒，不该判卡住。
        Assert.Equal(UploadTick.Waiting, verdict.Tick);
    }

    [Fact]
    public void 进度签名不是求和_两个都停住和一个传完一个没开始要区分开()
    {
        var monitor = NewMonitor(noChange: TimeSpan.FromSeconds(120));

        monitor.Observe(Sample(0, 2, [50, 50]));
        monitor.Observe(Sample(119, 2, [50, 50]));

        // 若签名用求和，[50,50] 与 [100,0] 都是 100，会当成「没变化」而继续累计。
        var afterSwap = monitor.Observe(Sample(120, 2, [100, 0]));
        Assert.Equal(UploadTick.Waiting, afterSwap.Tick);

        var verdict = monitor.Observe(Sample(200, 2, [100, 0]));
        Assert.Equal(UploadTick.Waiting, verdict.Tick);
    }

    [Fact]
    public void 超过总时长判失败()
    {
        var monitor = NewMonitor(total: TimeSpan.FromSeconds(10));

        monitor.Observe(Sample(0, 1, [10]));
        monitor.Observe(Sample(5, 1, [20]));
        var verdict = monitor.Observe(Sample(11, 1, [30]));

        Assert.Equal(UploadTick.Stalled, verdict.Tick);
        Assert.Contains("超过上限", verdict.LogLine);
    }

    [Fact]
    public void 进度条消失优先于总超时_传完了就不该判失败()
    {
        var monitor = NewMonitor(total: TimeSpan.FromSeconds(10));

        monitor.Observe(Sample(0, 1, [10]));
        var verdict = monitor.Observe(Sample(999, 0, []));

        // 总超时是兜底，不是主判据。已经传完的情况下超时没有意义。
        Assert.Equal(UploadTick.Completed, verdict.Tick);
    }

    [Fact]
    public void 读不到百分比时不算一点没动()
    {
        var monitor = NewMonitor(zero: TimeSpan.FromSeconds(10));

        monitor.Observe(Sample(0, 1, []));
        var verdict = monitor.Observe(Sample(30, 1, []));

        // 进度条在但读不到 aria-valuenow（可能换成别的写法），
        // 没有证据说它是 0，不能按「0 卡住」判死。
        Assert.Equal(UploadTick.Waiting, verdict.Tick);
    }

    [Fact]
    public void 整批直接失败时进度条从没出现过_也要判结束()
    {
        // 一批视频全部失败时，进度条根本不会长出来，只有失败条目。
        // 若只看进度条，这里要一直等到总超时（默认 15 分钟）才收场。
        var monitor = NewMonitor(total: TimeSpan.FromMinutes(10));

        monitor.Observe(Sample(0, 0, []));
        var verdict = monitor.Observe(
            new UploadSample(0, [], AllCopyrightReview: false, Elapsed: TimeSpan.FromSeconds(1), FailedCount: 2));

        Assert.Equal(UploadTick.Completed, verdict.Tick);
    }

    [Fact]
    public void 没有失败条目时不能靠它判完成()
    {
        var monitor = NewMonitor(total: TimeSpan.FromMinutes(10));

        monitor.Observe(Sample(0, 0, []));
        var verdict = monitor.Observe(
            new UploadSample(0, [], AllCopyrightReview: false, Elapsed: TimeSpan.FromSeconds(1), FailedCount: 0));

        Assert.Equal(UploadTick.Waiting, verdict.Tick);
    }

    [Fact]
    public void 采样轮数可查_便于日志记录()
    {
        var monitor = NewMonitor();

        monitor.Observe(Sample(0, 1, [1]));
        monitor.Observe(Sample(1, 1, [2]));
        monitor.Observe(Sample(2, 1, [3]));

        Assert.Equal(3, monitor.Samples);
    }
}
