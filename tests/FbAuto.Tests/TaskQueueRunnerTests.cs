using System.Text;
using FbAuto.Application.Execution;
using FbAuto.Browser.Abstractions;
using FbAuto.Core.Models;
using FbAuto.Infrastructure.Accounts;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 队列执行器。盯的是「调度」这件事本身：并发、间隔、取消、异常、必关浏览器。
/// 用假通道，不真启 Chrome——真机链路由集成测试单独覆盖。
/// </summary>
public sealed class TaskQueueRunnerTests
{
    private const string DefaultCsv = """
        别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注
        acc01,google,D:\FbProfiles\acc01,,,,,,正常,
        acc02,google,D:\FbProfiles\acc02,,,,,,正常,
        """;

    /// <summary>一条谷歌、一条比特，用来验「两个通道的失败清单各写各的目录」。</summary>
    private const string MixedCsv = """
        别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注
        acc01,google,D:\FbProfiles\acc01,,,,,,正常,
        acc02,bit,,asset-77,,,,,正常,
        """;

    private static AutomatedTask NewTask(string alias = "acc01", TaskKind kind = TaskKind.VideoUploadGoogle) =>
        new()
        {
            Kind = kind,
            Channel = BrowserChannelKind.Google,
            AccountAlias = alias,
            BrowserName = "Chrome-" + alias,
        };

    private static void RaisePeak(ref int peak, int value)
    {
        int snapshot;
        while (value > (snapshot = Volatile.Read(ref peak)))
        {
            Interlocked.CompareExchange(ref peak, value, snapshot);
        }
    }

    private sealed class Harness : IDisposable
    {
        public Harness(params ITaskExecutor[] executors) : this(DefaultCsv, executors)
        {
        }

        public Harness(string csv, params ITaskExecutor[] executors)
        {
            Temp = new TempDirectory();
            Logger = new TestLogger();
            Channel = new FakeBrowserChannel();

            var path = Temp.Combine("accounts.csv");
            File.WriteAllText(path, csv, new UTF8Encoding(false));
            Accounts = new AccountListCsv(path, Logger);

            Runner = new TaskQueueRunner(
                new TaskExecutorRegistry(executors),
                new SingleChannelProvider(Channel),
                Accounts,
                Logger);

            Runner.StatusChanged += (_, e) =>
            {
                lock (Events)
                {
                    Events.Add(e);
                }
            };
        }

        public TempDirectory Temp { get; }

        public TestLogger Logger { get; }

        public FakeBrowserChannel Channel { get; }

        public AccountListCsv Accounts { get; }

        public TaskQueueRunner Runner { get; }

        public List<TaskStatusChangedEventArgs> Events { get; } = [];

        public void Dispose() => Temp.Dispose();

        private sealed class SingleChannelProvider(FakeBrowserChannel channel) : IBrowserChannelProvider
        {
            public IBrowserChannel Create(BrowserChannelKind kind) => channel;
        }
    }

    [Fact]
    public async Task 只执行待执行的任务()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);

        var pending = NewTask();
        var alreadyDone = NewTask();
        alreadyDone.Status = TaskRunStatus.Success;

        var summary = await h.Runner.RunAsync([pending, alreadyDone], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(1, summary.Started);
        Assert.Equal(1, executor.CallCount);
        Assert.Equal(TaskRunStatus.Success, pending.Status);
    }

    [Fact]
    public async Task 队列里没有待执行任务时什么也不启动()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);

        var done = NewTask();
        done.Status = TaskRunStatus.Success;

        var summary = await h.Runner.RunAsync([done], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(0, summary.Started);
        Assert.Equal(0, h.Channel.LaunchCount);
        Assert.Equal(0, executor.CallCount);
        Assert.Contains(h.Logger.Entries, e => e.Contains("没有待执行的任务"));
    }

    [Fact]
    public async Task 成功的任务结果里带耗时()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle, (_, _) =>
            Task.FromResult(TaskExecutionResult.Ok("已上传 1 条"))));

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Success, task.Status);
        Assert.Contains("已上传 1 条", task.Result);
        Assert.Contains("耗时", task.Result);
        Assert.NotNull(task.StartedAt);
        Assert.NotNull(task.FinishedAt);
    }

    [Fact]
    public async Task 执行器抛异常时任务失败且程序不崩()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle,
            (_, _) => throw new InvalidOperationException("页面结构变了")));

        var task = NewTask();
        var summary = await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("InvalidOperationException", task.Result);
        Assert.Contains("页面结构变了", task.Result);
        Assert.Equal(1, summary.Failed);
        // 异常路径也必须把浏览器关掉
        Assert.Equal(h.Channel.LaunchCount, h.Channel.CloseCount);
    }

    [Fact]
    public async Task 执行器返回失败结果时状态为失败并保留原因()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle, (_, _) =>
            Task.FromResult(TaskExecutionResult.Fail("找不到上传按钮"))));

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("找不到上传按钮", task.Result);
    }

    [Fact]
    public async Task 账号不存在时失败原因是中文且指出清单文件()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle));

        var task = NewTask("acc99");
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("acc99", task.Result);
        Assert.Contains("accounts.csv", task.Result);
        // 账号都没有，不必启浏览器
        Assert.Equal(0, h.Channel.LaunchCount);
    }

    [Fact]
    public async Task 账号被停用时跳过且不启浏览器()
    {
        const string csv = """
            别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注
            acc01,google,D:\P1,,,,,,停用,
            """;

        using var h = new Harness(csv, new StubTaskExecutor(TaskKind.VideoUploadGoogle));

        var task = NewTask("acc01");
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("停用", task.Result);
        Assert.Equal(0, h.Channel.LaunchCount);
    }

    [Fact]
    public async Task 没有注册执行器时明确报程序缺陷()
    {
        // 只注册了视频上传，却跑一个资料修改任务
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle));

        var task = NewTask(kind: TaskKind.ProfileEditGoogle);
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("没有对应的执行器", task.Result);
        Assert.Equal(0, h.Channel.LaunchCount);
    }

    [Fact]
    public async Task 浏览器启动失败时任务失败且原因是中文()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);
        h.Channel.LaunchException = BrowserChannelException.ExecutableNotFound(null);

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("找不到 Chrome", task.Result);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task 并发数为一的时候不会同时执行两个任务()
    {
        var current = 0;
        var peak = 0;
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            RaisePeak(ref peak, Interlocked.Increment(ref current));
            await Task.Delay(120, ct);
            Interlocked.Decrement(ref current);
            return TaskExecutionResult.Ok("ok");
        });

        using var h = new Harness(executor);

        await h.Runner.RunAsync(
            [NewTask("acc01"), NewTask("acc02"), NewTask("acc01")],
            new TaskRunOptions { ConcurrentCount = 1, LaunchIntervalSeconds = 0 });

        Assert.Equal(1, Volatile.Read(ref peak));
    }

    [Fact]
    public async Task 并发数为二时确实并行()
    {
        var current = 0;
        var peak = 0;
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            RaisePeak(ref peak, Interlocked.Increment(ref current));
            await Task.Delay(200, ct);
            Interlocked.Decrement(ref current);
            return TaskExecutionResult.Ok("ok");
        });

        using var h = new Harness(executor);

        await h.Runner.RunAsync(
            [NewTask("acc01"), NewTask("acc02")],
            new TaskRunOptions { ConcurrentCount = 2, LaunchIntervalSeconds = 0 });

        Assert.Equal(2, Volatile.Read(ref peak));
    }

    [Fact]
    public async Task 两次浏览器启动之间至少隔设定的秒数()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);

        await h.Runner.RunAsync(
            [NewTask("acc01"), NewTask("acc02")],
            new TaskRunOptions { ConcurrentCount = 2, LaunchIntervalSeconds = 1 });

        var times = h.Channel.LaunchTimes;
        Assert.Equal(2, times.Count);

        var gap = times[1] - times[0];
        Assert.True(gap >= TimeSpan.FromMilliseconds(850), $"两次启动间隔只有 {gap.TotalMilliseconds:F0} ms，小于设定值");
    }

    [Fact]
    public async Task 取消后所有任务都落到终态不会卡在执行中()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            await Task.Delay(3000, ct);
            return TaskExecutionResult.Ok("不该走到这里");
        });

        using var h = new Harness(executor);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var tasks = new[] { NewTask("acc01"), NewTask("acc02") };
        var summary = await h.Runner.RunAsync(
            tasks,
            new TaskRunOptions { ConcurrentCount = 1, LaunchIntervalSeconds = 0 },
            cts.Token);

        Assert.All(tasks, t => Assert.Equal(TaskRunStatus.Interrupted, t.Status));
        Assert.Equal(2, summary.Interrupted);
        Assert.Contains(h.Logger.Entries, e => e.Contains("已停止"));

        // 中断同样要关浏览器
        Assert.Equal(h.Channel.LaunchCount, h.Channel.CloseCount);
    }

    [Fact]
    public async Task 单条超时判失败而不是中断()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            await Task.Delay(5000, ct);
            return TaskExecutionResult.Ok("不该走到这里");
        });

        using var h = new Harness(executor);

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions
        {
            LaunchIntervalSeconds = 0,
            TaskTimeout = TimeSpan.FromMilliseconds(200),
        });

        Assert.Equal(TaskRunStatus.Failed, task.Status);
        Assert.Contains("执行超时", task.Result);
    }

    [Fact]
    public async Task 关闭浏览器失败也不会让程序崩()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);
        h.Channel.CloseException = new InvalidOperationException("进程已经没了");

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        // 任务本身已经成功，关闭失败只是警告
        Assert.Equal(TaskRunStatus.Success, task.Status);
        Assert.Contains(h.Logger.Entries, e => e.Contains("关闭浏览器失败"));
    }

    [Fact]
    public async Task 一个任务失败不影响同批其他任务()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, (ctx, _) =>
            ctx.Task.AccountAlias == "acc01"
                ? throw new InvalidOperationException("第一条坏了")
                : Task.FromResult(TaskExecutionResult.Ok("ok")));

        using var h = new Harness(executor);

        var first = NewTask("acc01");
        var second = NewTask("acc02");
        var summary = await h.Runner.RunAsync([first, second], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(TaskRunStatus.Failed, first.Status);
        Assert.Equal(TaskRunStatus.Success, second.Status);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Succeeded);
    }

    [Fact]
    public async Task 已成功的任务在下一轮不会被重跑()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(1, executor.CallCount);
        Assert.Equal(1, h.Channel.LaunchCount);
    }

    [Fact]
    public async Task 每个任务开一个独立页面并带上账号的profile()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle);
        using var h = new Harness(executor);

        var task = NewTask("acc02");
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        Assert.Equal(1, h.Channel.ConnectCount);
        Assert.Equal(1, h.Channel.NewPageCount);
        Assert.Equal(@"D:\FbProfiles\acc02", h.Channel.LastProfileDirectory);
    }

    [Fact]
    public async Task 状态事件先执行中再成功()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle));

        var task = NewTask();
        await h.Runner.RunAsync([task], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        List<TaskRunStatus> statuses;
        lock (h.Events)
        {
            statuses = h.Events.Where(e => e.TaskId == task.Id).Select(e => e.Status).ToList();
        }

        Assert.Equal([TaskRunStatus.Running, TaskRunStatus.Success], statuses);
    }

    [Fact]
    public async Task 执行中再次开始会被忽略()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            await Task.Delay(300, ct);
            return TaskExecutionResult.Ok("ok");
        });

        using var h = new Harness(executor);

        var first = NewTask("acc01");
        var running = h.Runner.RunAsync([first], new TaskRunOptions { LaunchIntervalSeconds = 0 });
        await Task.Delay(80);

        var second = await h.Runner.RunAsync([NewTask("acc02")], new TaskRunOptions { LaunchIntervalSeconds = 0 });
        await running;

        Assert.Equal(0, second.Started);
        Assert.Contains(h.Logger.Entries, e => e.Contains("已有一轮执行在进行中"));
    }

    [Fact]
    public async Task 失败任务会把账号失败写进结果目录()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle, (_, _) =>
            Task.FromResult(TaskExecutionResult.Fail("找不到上传按钮"))));

        var exportDir = h.Temp.Combine("导出");
        var task = NewTask();

        await h.Runner.RunAsync([task], new TaskRunOptions
        {
            LaunchIntervalSeconds = 0,
            ExportDirectories = new Dictionary<BrowserChannelKind, string>
            {
                [BrowserChannelKind.Google] = exportDir,
            },
        });

        var path = Path.Combine(exportDir, AccountErrorLog.FileNameFor(DateTime.Now));
        Assert.True(File.Exists(path), $"没写出 {path}");

        var text = File.ReadAllText(path);
        Assert.Contains("[账号失败]", text);
        Assert.Contains("浏览器:Chrome-acc01", text);
        Assert.Contains("原因:找不到上传按钮", text);
        // 谷歌通道没有比特窗口 ID，assetId 留空而不是编一个
        Assert.Contains("assetId: |", text);
    }

    /// <summary>
    /// 成功的一轮不能留下空文件：使用者看到「有清单文件但里面没内容」会以为导出坏了。
    /// </summary>
    [Fact]
    public async Task 全部成功时不产生清单文件()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle));
        var exportDir = h.Temp.Combine("导出");

        await h.Runner.RunAsync([NewTask()], new TaskRunOptions
        {
            LaunchIntervalSeconds = 0,
            ExportDirectories = new Dictionary<BrowserChannelKind, string>
            {
                [BrowserChannelKind.Google] = exportDir,
            },
        });

        Assert.False(Directory.Exists(exportDir) && Directory.EnumerateFiles(exportDir).Any(),
            "全部成功却写出了文件");
    }

    /// <summary>
    /// 两个通道的目录不同，失败必须落到各自那份 ——
    /// 混在一个文件里就没法按通道对照了（原版也是 Google / Bit 各存一份配置）。
    /// </summary>
    [Fact]
    public async Task 两个通道的失败各写各的目录()
    {
        static Task<TaskExecutionResult> Fail(TaskExecutionContext ctx, CancellationToken _) =>
            Task.FromResult(TaskExecutionResult.Fail($"坏了-{ctx.Account.Alias}"));

        using var h = new Harness(
            MixedCsv,
            new StubTaskExecutor(TaskKind.VideoUploadGoogle, Fail),
            new StubTaskExecutor(TaskKind.VideoUploadBit, Fail));

        var googleDir = h.Temp.Combine("g");
        var bitDir = h.Temp.Combine("b");

        var googleTask = NewTask("acc01");
        var bitTask = new AutomatedTask
        {
            Kind = TaskKind.VideoUploadBit,
            Channel = BrowserChannelKind.Bit,
            AccountAlias = "acc02",
            BrowserName = "Bit-acc02",
        };

        await h.Runner.RunAsync([googleTask, bitTask], new TaskRunOptions
        {
            LaunchIntervalSeconds = 0,
            ConcurrentCount = 1,
            ExportDirectories = new Dictionary<BrowserChannelKind, string>
            {
                [BrowserChannelKind.Google] = googleDir,
                [BrowserChannelKind.Bit] = bitDir,
            },
        });

        var name = AccountErrorLog.FileNameFor(DateTime.Now);
        var googleText = File.ReadAllText(Path.Combine(googleDir, name));
        var bitText = File.ReadAllText(Path.Combine(bitDir, name));

        Assert.Contains("坏了-acc01", googleText);
        Assert.DoesNotContain("acc02", googleText);

        Assert.Contains("坏了-acc02", bitText);
        Assert.DoesNotContain("acc01", bitText);
        // 比特通道要把窗口 ID 记进 assetId，否则使用者没法回比特浏览器里定位是哪个窗口
        Assert.Contains("assetId:asset-77", bitText);
    }

    /// <summary>
    /// 没配目录时一条都不写 —— 这是原版的默认值（两份 settings.json 里都是空串）。
    /// </summary>
    [Fact]
    public async Task 没配目录时不导出()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle, (_, _) =>
            Task.FromResult(TaskExecutionResult.Fail("失败"))));

        await h.Runner.RunAsync([NewTask()], new TaskRunOptions { LaunchIntervalSeconds = 0 });

        var stray = Directory.EnumerateFiles(h.Temp.Path, "AccountErrors_*", SearchOption.AllDirectories).ToArray();
        Assert.Empty(stray);
    }

    /// <summary>账号在清单里找不到时也该留档：这类失败最需要事后对账。</summary>
    [Fact]
    public async Task 账号找不到也记一条失败()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle));
        var exportDir = h.Temp.Combine("导出");

        await h.Runner.RunAsync([NewTask("acc99")], new TaskRunOptions
        {
            LaunchIntervalSeconds = 0,
            ExportDirectories = new Dictionary<BrowserChannelKind, string>
            {
                [BrowserChannelKind.Google] = exportDir,
            },
        });

        var text = File.ReadAllText(Path.Combine(exportDir, AccountErrorLog.FileNameFor(DateTime.Now)));
        Assert.Contains("acc99", text);
        Assert.Contains("账号清单里没有别名", text);
    }

    /// <summary>
    /// 同账号的两条任务，并发开到 2 也必须串行。
    /// </summary>
    /// <remarks>
    /// 这不是保守，是血账：两条同账号任务并行会各自启动一个 Chrome 指向同一个 user-data-dir，
    /// Chrome 把第二个实例的启动请求转发给第一个、然后自己退出；
    /// 而 <c>ChromeChannel</c> 启动前「清掉残留的 DevToolsActivePort」删的是**第一个实例正在用的**文件，
    /// 于是先开的那条读不到端口、后开的那条报「启动后立即退出」，两条一起废。
    /// </remarks>
    [Fact]
    public async Task 并发数为二时同一账号的两条任务仍然串行()
    {
        var current = 0;
        var peak = 0;
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            RaisePeak(ref peak, Interlocked.Increment(ref current));
            await Task.Delay(150, ct);
            Interlocked.Decrement(ref current);
            return TaskExecutionResult.Ok("ok");
        });

        using var h = new Harness(executor);

        await h.Runner.RunAsync(
            [NewTask("acc01"), NewTask("acc01")],
            new TaskRunOptions { ConcurrentCount = 2, LaunchIntervalSeconds = 0 });

        Assert.Equal(1, Volatile.Read(ref peak));

        // 串行 ≠ 少跑：两条都执行完，各启过一次浏览器。
        Assert.Equal(2, h.Channel.LaunchCount);
        Assert.Equal(2, h.Channel.CloseCount);
    }

    /// <summary>排队必须留痕，否则使用者看到任务迟迟不开始会以为卡死。</summary>
    [Fact]
    public async Task 同账号排队时写一行说明原因的日志()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            await Task.Delay(120, ct);
            return TaskExecutionResult.Ok("ok");
        });

        using var h = new Harness(executor);

        await h.Runner.RunAsync(
            [NewTask("acc01"), NewTask("acc01")],
            new TaskRunOptions { ConcurrentCount = 2, LaunchIntervalSeconds = 0 });

        Assert.Contains(h.Logger.Entries, e => e.Contains("排队等待") && e.Contains("acc01"));

        // 只有真排队的那条写一行。第一条直接进，不该也来一句「排队等待」。
        Assert.Single(h.Logger.Entries, e => e.Contains("排队等待"));
    }

    /// <summary>并发数比任务多时日志必须写实际并发，否则事后对账会以为丢了任务。</summary>
    [Fact]
    public async Task 并发数大于任务数时日志写实际并发()
    {
        using var h = new Harness(new StubTaskExecutor(TaskKind.VideoUploadGoogle));

        await h.Runner.RunAsync(
            [NewTask("acc01"), NewTask("acc02")],
            new TaskRunOptions { ConcurrentCount = 5, LaunchIntervalSeconds = 0 });

        Assert.Contains(h.Logger.Entries, e => e.Contains("实际并发 2"));
    }

    /// <summary>
    /// 取消时账号锁不能泄漏：卡在「等账号锁」上的任务要被取消成中断态，
    /// 而不是永远等下去（那会让整轮 WhenAll 永不返回）。
    /// </summary>
    [Fact]
    public async Task 取消时卡在账号锁上的任务也落到中断态()
    {
        var executor = new StubTaskExecutor(TaskKind.VideoUploadGoogle, async (_, ct) =>
        {
            await Task.Delay(3000, ct);
            return TaskExecutionResult.Ok("不该走到这里");
        });

        using var h = new Harness(executor);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var tasks = new[] { NewTask("acc01"), NewTask("acc01") };
        var summary = await h.Runner.RunAsync(
            tasks,
            new TaskRunOptions { ConcurrentCount = 2, LaunchIntervalSeconds = 0 },
            cts.Token);

        Assert.All(tasks, t => Assert.Equal(TaskRunStatus.Interrupted, t.Status));
        Assert.Equal(2, summary.Interrupted);

        // 只启了一个浏览器：第二条根本没轮上，不该留下「开了没关」的账。
        Assert.Equal(h.Channel.LaunchCount, h.Channel.CloseCount);
    }
}
