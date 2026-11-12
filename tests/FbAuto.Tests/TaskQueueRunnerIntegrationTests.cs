using System.Diagnostics;
using System.Text;
using FbAuto.Application.Execution;
using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Chrome;
using FbAuto.Core.Models;
using FbAuto.Infrastructure.Accounts;
using FbAuto.Tests.Support;
using Microsoft.Playwright;

namespace FbAuto.Tests;

/// <summary>
/// S4 集测：真启 Chrome，走完「调度器 → 账号解析 → 启动 → 接管 → 开页 → 导航本地页 → 回写状态 → 关闭」。
///
/// 用本地 HTML 而不是 facebook.com：后者在本机 443 不通，拿它做验收只会一直红，
/// 而且网络问题会掩盖链路问题——两者必须能分开定位。
/// </summary>
[Collection("chrome")]
public sealed class TaskQueueRunnerIntegrationTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly TestLogger _logger = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task 一条任务从调度到成功并把浏览器关掉()
    {
        var pagePath = _dir.Combine("target.html");
        File.WriteAllText(pagePath, """
            <!doctype html>
            <html lang="zh-CN"><head><meta charset="utf-8"><title>调度验收页</title></head>
            <body><h1 id="title">执行链路验收</h1></body></html>
            """);

        var profileDir = _dir.Combine("profile-acc01");
        var csvPath = _dir.Combine("accounts.csv");
        File.WriteAllText(csvPath, $"""
            别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注
            acc01,google,{profileDir},,,,,,正常,集测用
            """, new UTF8Encoding(false));

        var accounts = new AccountListCsv(csvPath, _logger);
        var channel = new RecordingChannel(new ChromeChannel(_logger));
        var executors = new TaskExecutorRegistry([
            new PageProbeExecutor(TaskKind.VideoUploadGoogle, new Uri(pagePath).AbsoluteUri),
        ]);

        var runner = new TaskQueueRunner(executors, new SingleChannelProvider(channel), accounts, _logger);

        var task = new AutomatedTask
        {
            Kind = TaskKind.VideoUploadGoogle,
            Channel = BrowserChannelKind.Google,
            AccountAlias = "acc01",
            BrowserName = "Chrome-acc01",
        };

        int? pid;
        try
        {
            var summary = await runner.RunAsync([task], new TaskRunOptions
            {
                ConcurrentCount = 1,
                LaunchIntervalSeconds = 0,
                Headless = true,
                TaskTimeout = TimeSpan.FromSeconds(60),
            });

            Assert.Equal(1, summary.Succeeded);
            Assert.Equal(TaskRunStatus.Success, task.Status);
            Assert.Contains("调度验收页", task.Result);

            // 账号清单里的 profile 路径要真的传下去，不能被默认值顶掉——否则账号会串味。
            Assert.Equal(profileDir, channel.LastRequest?.ProfileDirectory);

            pid = channel.LastSession?.ProcessId;
            Assert.NotNull(pid);
            Assert.Equal(1, channel.CloseCount);
        }
        finally
        {
            await channel.DisposeAsync();
        }

        await Task.Delay(500);
        Assert.True(IsProcessGone(pid!.Value), $"浏览器进程 {pid} 应当已被调度器关闭");
    }

    [Fact]
    public async Task 执行器失败时任务记为失败且浏览器照样关闭()
    {
        var profileDir = _dir.Combine("profile-acc02");
        var csvPath = _dir.Combine("accounts.csv");
        File.WriteAllText(csvPath, $"""
            别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注
            acc02,google,{profileDir},,,,,,正常,集测用
            """, new UTF8Encoding(false));

        var accounts = new AccountListCsv(csvPath, _logger);
        var channel = new RecordingChannel(new ChromeChannel(_logger));

        // 打一个必然打不开的地址，让执行器返回失败（而不是抛异常）
        var executors = new TaskExecutorRegistry([
            new PageProbeExecutor(TaskKind.VideoUploadGoogle, "http://127.0.0.1:1/不存在的页面"),
        ]);

        var runner = new TaskQueueRunner(executors, new SingleChannelProvider(channel), accounts, _logger);

        var task = new AutomatedTask
        {
            Kind = TaskKind.VideoUploadGoogle,
            Channel = BrowserChannelKind.Google,
            AccountAlias = "acc02",
            BrowserName = "Chrome-acc02",
        };

        int? pid;
        try
        {
            var summary = await runner.RunAsync([task], new TaskRunOptions
            {
                LaunchIntervalSeconds = 0,
                Headless = true,
                TaskTimeout = TimeSpan.FromSeconds(60),
            });

            Assert.Equal(1, summary.Failed);
            Assert.Equal(TaskRunStatus.Failed, task.Status);
            Assert.Contains("打开页面失败", task.Result);

            pid = channel.LastSession?.ProcessId;
            Assert.Equal(1, channel.CloseCount);
        }
        finally
        {
            await channel.DisposeAsync();
        }

        await Task.Delay(500);
        Assert.True(IsProcessGone(pid!.Value), $"失败路径也必须关掉浏览器，但 {pid} 还在");
    }

    private static bool IsProcessGone(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    /// <summary>
    /// 包一层真通道，把会话与启动参数记下来。
    /// 调度器内部自己 Launch，外面拿不到 session，没有这层就无法断言「PID 真的没了」。
    /// </summary>
    private sealed class RecordingChannel(IBrowserChannel inner) : IBrowserChannel, IAsyncDisposable
    {
        private int _closeCount;

        public BrowserSession? LastSession { get; private set; }

        public BrowserLaunchRequest? LastRequest { get; private set; }

        public int CloseCount => Volatile.Read(ref _closeCount);

        public BrowserChannelKind Kind => inner.Kind;

        public async Task<BrowserSession> LaunchAsync(BrowserLaunchRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            LastSession = await inner.LaunchAsync(request, cancellationToken);
            return LastSession;
        }

        public Task<string> GetWsEndpointAsync(BrowserSession session, CancellationToken cancellationToken = default)
            => inner.GetWsEndpointAsync(session, cancellationToken);

        public Task<IBrowser> ConnectAsync(BrowserSession session, CancellationToken cancellationToken = default)
            => inner.ConnectAsync(session, cancellationToken);

        public Task<IPage> NewPageAsync(IBrowser browser, CancellationToken cancellationToken = default)
            => inner.NewPageAsync(browser, cancellationToken);

        public Task<bool> IsAliveAsync(BrowserSession session, CancellationToken cancellationToken = default)
            => inner.IsAliveAsync(session, cancellationToken);

        public async Task CloseAsync(BrowserSession session, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _closeCount);
            await inner.CloseAsync(session, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (inner is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }
    }

    private sealed class SingleChannelProvider(IBrowserChannel channel) : IBrowserChannelProvider
    {
        public IBrowserChannel Create(BrowserChannelKind kind) => channel;
    }
}
