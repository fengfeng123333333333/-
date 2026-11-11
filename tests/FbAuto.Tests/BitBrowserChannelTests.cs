using System.Diagnostics;
using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Bit;
using FbAuto.Core.Models;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：比特通道。窗口由比特客户端管，这边只负责「调接口 → 拿到 ws → 探活 → 调接口关闭」。
/// 探活必须同时看进程与调试端口：只看进程会把「窗口被手动关了但进程还挂着」判成活的。
/// </summary>
public sealed class BitBrowserChannelTests
{
    private static string OpenResponse(int pid, int httpPort) => $$$"""
        {"code":0,"data":{"ws":"ws://127.0.0.1:{{{httpPort}}}/devtools/browser/abc","http":"127.0.0.1:{{{httpPort}}}","pid":{{{pid}}},"dir":"C:\\BitProfiles\\p1","name":"窗口1"}}
        """;

    private static FakeHttpServer CreateServer(int pid)
    {
        var port = 0;
        var server = new FakeHttpServer(path => path switch
        {
            "/browser/open" => OpenResponse(pid, port),
            "/browser/close" => """{"code":0,"msg":"success"}""",
            "/json/version" => """{"Browser":"Chrome/120.0.0.0"}""",
            _ => """{"code":404,"msg":"未知接口"}""",
        });
        port = server.Port;
        return server;
    }

    [Fact]
    public async Task 没给窗口ID时直接报中文错误()
    {
        await using var server = CreateServer(Environment.ProcessId);
        using var client = new BitBrowserApiClient(server.BaseUrl);
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() =>
            channel.LaunchAsync(new BrowserLaunchRequest { BitProfileId = "  " }));

        Assert.Equal(BrowserChannelFailure.ProfileNotFound, ex.Reason);
        Assert.Contains("比特窗口 ID", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task 打开窗口后会话字段与接口返回一致()
    {
        await using var server = CreateServer(Environment.ProcessId);
        using var client = new BitBrowserApiClient(server.BaseUrl);
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var session = await channel.LaunchAsync(new BrowserLaunchRequest { BitProfileId = "p1" });

        Assert.Equal(BrowserChannelKind.Bit, session.Kind);
        Assert.Equal(Environment.ProcessId, session.ProcessId);
        Assert.Equal($"127.0.0.1:{server.Port}", session.HttpEndpoint);
        Assert.Equal($"ws://127.0.0.1:{server.Port}/devtools/browser/abc", session.WsEndpoint);
        Assert.Equal("p1", session.BitProfileId);
        Assert.Equal(@"C:\BitProfiles\p1", session.ProfileDirectory);
    }

    [Fact]
    public async Task ws地址取回后可直接用于连接()
    {
        await using var server = CreateServer(Environment.ProcessId);
        using var client = new BitBrowserApiClient(server.BaseUrl);
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var session = await channel.LaunchAsync(new BrowserLaunchRequest { BitProfileId = "p1" });
        var ws = await channel.GetWsEndpointAsync(session);

        Assert.Equal(session.WsEndpoint, ws);
    }

    [Fact]
    public async Task 没有ws时取地址报会话已关闭()
    {
        using var client = new BitBrowserApiClient("http://127.0.0.1:1");
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var session = new BrowserSession { Kind = BrowserChannelKind.Bit, BitProfileId = "p1" };

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() => channel.GetWsEndpointAsync(session));

        Assert.Equal(BrowserChannelFailure.SessionClosed, ex.Reason);
    }

    [Fact]
    public async Task 进程在且调试端口可达才算活()
    {
        await using var server = CreateServer(Environment.ProcessId);
        using var client = new BitBrowserApiClient(server.BaseUrl);
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var session = await channel.LaunchAsync(new BrowserLaunchRequest { BitProfileId = "p1" });

        Assert.True(await channel.IsAliveAsync(session));
    }

    [Fact]
    public async Task 进程已退出时判为不活()
    {
        using var victim = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true })!;
        victim.WaitForExit(5_000);
        var deadPid = victim.Id;

        await using var server = CreateServer(deadPid);
        using var client = new BitBrowserApiClient(server.BaseUrl);
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var session = new BrowserSession
        {
            Kind = BrowserChannelKind.Bit,
            ProcessId = deadPid,
            HttpEndpoint = $"127.0.0.1:{server.Port}",
            WsEndpoint = "ws://127.0.0.1:1/devtools/browser/x",
        };

        Assert.False(await channel.IsAliveAsync(session));
    }

    [Fact]
    public async Task 进程在但调试端口不通时判为不活()
    {
        int deadPort;
        await using (var probe = new FakeHttpServer(_ => "{}"))
        {
            deadPort = probe.Port;
        }

        using var client = new BitBrowserApiClient($"http://127.0.0.1:{deadPort}");
        await using var channel = new BitBrowserChannel(new TestLogger(), client);

        var session = new BrowserSession
        {
            Kind = BrowserChannelKind.Bit,
            ProcessId = Environment.ProcessId,   // 进程确实活着
            HttpEndpoint = $"127.0.0.1:{deadPort}",  // 但窗口的调试端口不通
            WsEndpoint = "ws://127.0.0.1:1/devtools/browser/x",
        };

        Assert.False(await channel.IsAliveAsync(session));
    }

    [Fact]
    public async Task 关闭窗口调的是接口而不是杀进程()
    {
        await using var server = CreateServer(Environment.ProcessId);
        using var client = new BitBrowserApiClient(server.BaseUrl);
        var logger = new TestLogger();
        await using var channel = new BitBrowserChannel(logger, client);

        var session = await channel.LaunchAsync(new BrowserLaunchRequest { BitProfileId = "p1" });
        await channel.CloseAsync(session);

        Assert.Contains("/browser/close", server.RequestedPaths);
        Assert.True(logger.Contains("INF", "已通过比特接口关闭窗口"));
    }

    [Fact]
    public async Task 关闭失败只记警告不抛异常()
    {
        int deadPort;
        await using (var probe = new FakeHttpServer(_ => "{}"))
        {
            deadPort = probe.Port;
        }

        var logger = new TestLogger();
        using var client = new BitBrowserApiClient($"http://127.0.0.1:{deadPort}");
        await using var channel = new BitBrowserChannel(logger, client);

        var session = new BrowserSession
        {
            Kind = BrowserChannelKind.Bit,
            BitProfileId = "p1",
            WsEndpoint = "ws://127.0.0.1:1/devtools/browser/x",
        };

        var ex = await Record.ExceptionAsync(() => channel.CloseAsync(session));

        Assert.Null(ex);
        Assert.True(logger.Contains("WRN", "关闭比特窗口失败"));
    }
}
