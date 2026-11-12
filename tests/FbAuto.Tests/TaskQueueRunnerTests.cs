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
}
