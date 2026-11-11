using FbAuto.Application.Browser;
using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Bit;
using FbAuto.Browser.Chrome;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：通道工厂。上层只认接口，换通道不改任务代码；
/// 配置里的 ChromePath 必须真的传到通道里，否则用户改了配置却没生效，最难查。
/// </summary>
public sealed class BrowserChannelFactoryTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly TestLogger _logger = new();

    [Fact]
    public void 谷歌通道返回Chrome实现()
    {
        var channel = BrowserChannelFactory.Create(BrowserChannelKind.Google, _logger);

        Assert.IsType<ChromeChannel>(channel);
        Assert.Equal(BrowserChannelKind.Google, channel.Kind);
    }

    [Fact]
    public void 比特通道返回比特实现()
    {
        var channel = BrowserChannelFactory.Create(BrowserChannelKind.Bit, _logger);

        Assert.IsType<BitBrowserChannel>(channel);
        Assert.Equal(BrowserChannelKind.Bit, channel.Kind);
    }

    [Fact]
    public async Task 配置里的ChromePath会传到通道()
    {
        var missing = _dir.Combine("custom", "chrome.exe");
        var channel = BrowserChannelFactory.Create(BrowserChannelKind.Google, _logger, new AppConfig
        {
            ChromePath = missing,
        });

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() => channel.LaunchAsync(new BrowserLaunchRequest
        {
            ProfileDirectory = _dir.Combine("profile"),
        }));

        // 报的就是配置里那个路径，说明配置确实生效了，而不是回退到自动查找
        Assert.Equal(BrowserChannelFailure.ExecutableNotFound, ex.Reason);
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void 未知通道直接拒绝()
    {
        Assert.Throws<NotSupportedException>(() =>
            BrowserChannelFactory.Create((BrowserChannelKind)99, _logger));
    }

    [Fact]
    public void 日志为空时拒绝创建()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BrowserChannelFactory.Create(BrowserChannelKind.Google, null!));
    }

    public void Dispose() => _dir.Dispose();
}
