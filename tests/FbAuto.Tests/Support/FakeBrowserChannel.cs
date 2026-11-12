using FbAuto.Browser.Abstractions;
using FbAuto.Core.Models;
using Microsoft.Playwright;
using Moq;

namespace FbAuto.Tests.Support;

/// <summary>
/// 假的浏览器通道：记录启动/关闭次数与时间，可注入延迟与异常。
/// 调度器的核心断言（并发、间隔、必关）都靠它，不依赖真 Chrome。
/// </summary>
public sealed class FakeBrowserChannel : IBrowserChannel
{
    private readonly List<DateTimeOffset> _launchTimes = [];
    private readonly object _sync = new();
    private int _launchCount;
    private int _closeCount;

    public FakeBrowserChannel(BrowserChannelKind kind = BrowserChannelKind.Google) => Kind = kind;

    public BrowserChannelKind Kind { get; }

    public int LaunchCount => Volatile.Read(ref _launchCount);

    public int CloseCount => Volatile.Read(ref _closeCount);

    public int ConnectCount { get; private set; }

    public int NewPageCount { get; private set; }

    /// <summary>每次 Launch 的阻塞时长，用来模拟浏览器启动耗时。</summary>
    public TimeSpan LaunchDelay { get; set; } = TimeSpan.Zero;

    /// <summary>非空则在 Launch 时抛出。</summary>
    public Exception? LaunchException { get; set; }

    /// <summary>非空则在 Close 时抛出（验证「关闭失败也不会让程序崩」）。</summary>
    public Exception? CloseException { get; set; }

    public string? LastProfileDirectory { get; private set; }

    public string? LastProxy { get; private set; }

    public bool? LastHeadless { get; private set; }

    public IReadOnlyList<DateTimeOffset> LaunchTimes
    {
        get
        {
            lock (_sync)
            {
                return [.. _launchTimes];
            }
        }
    }

    public async Task<BrowserSession> LaunchAsync(BrowserLaunchRequest request, CancellationToken cancellationToken = default)
    {
        var index = Interlocked.Increment(ref _launchCount);

        lock (_sync)
        {
            _launchTimes.Add(DateTimeOffset.UtcNow);
        }

        LastProfileDirectory = request.ProfileDirectory;
        LastProxy = request.Proxy;
        LastHeadless = request.Headless;

        if (LaunchDelay > TimeSpan.Zero)
        {
            await Task.Delay(LaunchDelay, cancellationToken);
        }

        if (LaunchException is not null)
        {
            throw LaunchException;
        }

        return new BrowserSession
        {
            Kind = Kind,
            ProcessId = 1000 + index,
            DebugPort = 9000 + index,
            ProfileDirectory = request.ProfileDirectory,
            BitProfileId = request.BitProfileId,
        };
    }

    public Task<string> GetWsEndpointAsync(BrowserSession session, CancellationToken cancellationToken = default)
        => Task.FromResult($"ws://127.0.0.1:{(session.DebugPort ?? 0)}/devtools/browser/fake");

    public Task<IBrowser> ConnectAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        // IBrowser 有几十个成员，手写假实现容易漏；空壳足够——测试不碰浏览器行为。
        return Task.FromResult(new Mock<IBrowser>().Object);
    }

    public Task<IPage> NewPageAsync(IBrowser browser, CancellationToken cancellationToken = default)
    {
        NewPageCount++;
        return Task.FromResult(new Mock<IPage>().Object);
    }

    public Task<bool> IsAliveAsync(BrowserSession session, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task CloseAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _closeCount);

        if (CloseException is not null)
        {
            throw CloseException;
        }

        return Task.CompletedTask;
    }
}
