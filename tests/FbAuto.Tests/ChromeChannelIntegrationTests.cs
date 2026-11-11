using System.Diagnostics;
using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Chrome;
using FbAuto.Browser.Pages;
using FbAuto.Tests.Support;
using Microsoft.Playwright;

namespace FbAuto.Tests;

/// <summary>
/// S2 集测：真启 Chrome，走完「启动 → 读端口 → ConnectOverCDP → 开页 → 导航 → 读文本 → 截图 → 探活 → 关闭」。
/// 用本地 HTML，不依赖外网——本机到 facebook.com 的 443 是不通的，拿它做验收会一直红。
/// 同一 collection 串行，避免多个 Chrome 同时抢资源。
/// </summary>
[Collection("chrome")]
public sealed class ChromeChannelIntegrationTests : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly TestLogger _logger = new();
    private ChromeChannel? _channel;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _dir.Dispose();
    }

    private string CreateLocalPage(string text = "本地验收页")
    {
        var path = _dir.Combine($"page-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, $"""
            <!doctype html>
            <html lang="zh-CN"><head><meta charset="utf-8"><title>本地验收</title></head>
            <body><h1 id="title">{text}</h1><div class="item">A</div><div class="item">B</div></body></html>
            """);
        return path;
    }

    private async Task<(ChromeChannel Channel, BrowserSession Session)> LaunchAsync(bool headless = true)
    {
        _channel = new ChromeChannel(_logger);
        var session = await _channel.LaunchAsync(new BrowserLaunchRequest
        {
            ProfileDirectory = _dir.Combine("profile"),
            Headless = headless,
            LaunchTimeout = TimeSpan.FromSeconds(40),
        });
        return (_channel, session);
    }

    [Fact]
    public async Task 启动到关闭整条链路可用()
    {
        var (channel, session) = await LaunchAsync();

        Assert.True(session.DebugPort > 0);
        Assert.StartsWith("ws://127.0.0.1:", session.WsEndpoint);
        Assert.NotNull(session.ProcessId);
        Assert.True(await channel.IsAliveAsync(session), "刚启动应当判为存活");

        var ws = await channel.GetWsEndpointAsync(session);
        Assert.Equal(session.WsEndpoint, ws);

        var browser = await channel.ConnectAsync(session);
        var page = await channel.NewPageAsync(browser);

        var pageObject = new TestPageObject(page);
        var htmlPath = CreateLocalPage();
        await pageObject.NavigateAsync(new Uri(htmlPath).AbsoluteUri);

        Assert.Equal("本地验收页", await pageObject.ReadTextAsync("#title"));
        Assert.Equal(["A", "B"], await pageObject.ReadTextsAsync(".item"));
        Assert.Equal("本地验收", await pageObject.GetTitleAsync());

        var shot = await pageObject.ScreenshotAsync(_dir.Combine("shots", "page.png"));
        Assert.True(File.Exists(shot));
        Assert.True(new FileInfo(shot).Length > 1_000, "截图文件小得可疑，可能是空图");

        // 关闭后：进程要真没了，探活要变 false
        var pid = session.ProcessId!.Value;
        await channel.CloseAsync(session);

        var exited = await WaitUntilAsync(() => !IsProcessAlive(pid), TimeSpan.FromSeconds(10));
        Assert.True(exited, $"关闭后 PID {pid} 仍然存活");
        Assert.False(await channel.IsAliveAsync(session));
    }

    [Fact]
    public async Task 同一浏览器可开多页且互不影响()
    {
        var (channel, session) = await LaunchAsync();
        var browser = await channel.ConnectAsync(session);

        var page1 = await channel.NewPageAsync(browser);
        var page2 = await channel.NewPageAsync(browser);

        var htmlA = CreateLocalPage("页面A");
        var htmlB = CreateLocalPage("页面B");

        await page1.GotoAsync(new Uri(htmlA).AbsoluteUri);
        await page2.GotoAsync(new Uri(htmlB).AbsoluteUri);

        Assert.Equal("页面A", (await page1.Locator("#title").InnerTextAsync()).Trim());
        Assert.Equal("页面B", (await page2.Locator("#title").InnerTextAsync()).Trim());

        await channel.CloseAsync(session);
    }

    [Fact]
    public async Task 上次残留的端口文件会被清掉不会连到旧端口()
    {
        var profile = _dir.Combine("profile");
        Directory.CreateDirectory(profile);
        File.WriteAllText(DevToolsActivePortFile.GetPath(profile), "1\n/devtools/browser/stale");

        _channel = new ChromeChannel(_logger);
        var session = await _channel.LaunchAsync(new BrowserLaunchRequest
        {
            ProfileDirectory = profile,
            Headless = true,
            LaunchTimeout = TimeSpan.FromSeconds(40),
        });

        Assert.NotEqual(1, session.DebugPort);
        Assert.DoesNotContain("stale", session.WsEndpoint);

        // 真连一次，确认端口是新写的而不是残留的
        var browser = await _channel.ConnectAsync(session);
        Assert.NotEmpty(browser.Contexts);

        await _channel.CloseAsync(session);
    }

    [Fact]
    public async Task 可执行文件不存在时报中文错误而不是崩溃()
    {
        var channel = new ChromeChannel(_logger, _dir.Combine("nope", "chrome.exe"));

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() => channel.LaunchAsync(new BrowserLaunchRequest
        {
            ProfileDirectory = _dir.Combine("profile"),
        }));

        Assert.Equal(BrowserChannelFailure.ExecutableNotFound, ex.Reason);
        Assert.Contains("找不到 Chrome", ex.Message);
    }

    [Fact]
    public async Task 等不到元素时抛中文超时错误()
    {
        var (channel, session) = await LaunchAsync();
        var browser = await channel.ConnectAsync(session);
        var page = await channel.NewPageAsync(browser);
        var pageObject = new TestPageObject(page);

        await pageObject.NavigateAsync(new Uri(CreateLocalPage()).AbsoluteUri);

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() =>
            pageObject.WaitForElementAsync("#nope", TimeSpan.FromSeconds(2)));

        Assert.Equal(BrowserChannelFailure.ElementNotFound, ex.Reason);
        Assert.Contains("#nope", ex.Message);

        await channel.CloseAsync(session);
    }

    [Fact]
    public async Task 导航到打不开的地址时报中文错误()
    {
        var (channel, session) = await LaunchAsync();
        var browser = await channel.ConnectAsync(session);
        var pageObject = new TestPageObject(await channel.NewPageAsync(browser));

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() =>
            pageObject.NavigateAsync("http://127.0.0.1:1/", TimeSpan.FromSeconds(5)));

        Assert.Equal(BrowserChannelFailure.NavigationFailed, ex.Reason);
        Assert.Contains("http://127.0.0.1:1/", ex.Message);

        await channel.CloseAsync(session);
    }

    [Fact]
    public async Task 进程在但调试端口不通时判为不活()
    {
        // 进程确实活着（就是测试进程自己），但那个调试端口没人监听
        int deadPort;
        await using (var probe = new FakeHttpServer(_ => "{}"))
        {
            deadPort = probe.Port;
        }

        var channel = new ChromeChannel(_logger);
        var session = new BrowserSession
        {
            Kind = FbAuto.Core.Models.BrowserChannelKind.Google,
            ProcessId = Environment.ProcessId,
            DebugPort = deadPort,
            ProfileDirectory = _dir.Path,
        };

        Assert.False(await channel.IsAliveAsync(session), "进程在但 CDP 不通，不能算活");
    }

    [Fact]
    public async Task 进程不存在时探活返回false()
    {
        var channel = new ChromeChannel(_logger);
        var session = new BrowserSession
        {
            Kind = FbAuto.Core.Models.BrowserChannelKind.Google,
            ProcessId = 1,
            DebugPort = 1,
            ProfileDirectory = _dir.Path,
        };

        Assert.False(await channel.IsAliveAsync(session));
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return condition();
    }

    /// <summary>只为了用基类的三个方法，业务页面对象在 S4 才写。</summary>
    private sealed class TestPageObject(IPage page) : PageObjectBase(page);
}
