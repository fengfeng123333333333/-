using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Chrome;
using Microsoft.Playwright;

namespace FbAuto.Tests.Support;

/// <summary>
/// 需要真 Chrome 的测试的公共基类：整个测试类共用一次浏览器启动。
/// </summary>
/// <remarks>
/// 为什么共用：每次 <c>LaunchAsync</c> 都要起一个 Chrome、读端口文件、ConnectOverCDP，
/// 一个类里十几个用例各起一次会让集成测试从几秒涨到一分钟。页面可以每个用例新开一张，
/// 浏览器不必。
///
/// 关闭只走 <see cref="ChromeChannel.CloseAsync"/>（按自己启动的 PID 关），
/// 绝不按进程名杀 chrome.exe —— 那会连同用户自己开着的浏览器一起关掉。
/// </remarks>
public abstract class ChromePageTestBase : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly TestLogger _logger = new();
    private ChromeChannel? _channel;
    private BrowserSession? _session;
    private IBrowser? _browser;

    protected TempDirectory Dir => _dir;

    protected ChromeChannel Channel => _channel
        ?? throw new InvalidOperationException("测试基类尚未初始化");

    protected IBrowser Browser => _browser
        ?? throw new InvalidOperationException("测试基类尚未初始化");

    public async Task InitializeAsync()
    {
        _channel = new ChromeChannel(_logger);
        _session = await _channel.LaunchAsync(new BrowserLaunchRequest
        {
            ProfileDirectory = _dir.Combine("profile"),
            Headless = true,
            LaunchTimeout = TimeSpan.FromSeconds(40),
        });

        _browser = await _channel.ConnectAsync(_session);
    }

    public async Task DisposeAsync()
    {
        if (_channel is not null && _session is not null)
        {
            await _channel.CloseAsync(_session);
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _dir.Dispose();
    }

    /// <summary>写一个本地页、开一张新页、导航过去。</summary>
    protected async Task<IPage> OpenPageAsync(string html, string fileName = "page.html")
    {
        var path = FacebookMimicPage.Write(_dir.Path, fileName, html);
        var page = await Channel.NewPageAsync(Browser);
        await page.GotoAsync(new Uri(path).AbsoluteUri, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
        });
        return page;
    }

    protected static Task<T> ReadAsync<T>(IPage page, string expression) =>
        page.EvaluateAsync<T>(expression);
}
