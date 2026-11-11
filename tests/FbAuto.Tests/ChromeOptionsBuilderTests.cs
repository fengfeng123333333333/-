using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Chrome;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：Chrome 启动参数。参数错一个字母，浏览器起得来但端口不出来，
/// 现场表现和「网络不通」几乎一样，所以这里逐个 token 钉死。
/// </summary>
public sealed class ChromeOptionsBuilderTests
{
    private static BrowserLaunchRequest BaseRequest() => new()
    {
        ProfileDirectory = @"D:\FbProfiles\acc01",
    };

    [Fact]
    public void 必带独立profile与调试端口0()
    {
        var args = ChromeOptionsBuilder.Build(BaseRequest());

        Assert.Contains(@"--user-data-dir=D:\FbProfiles\acc01", args);
        Assert.Contains("--remote-debugging-port=0", args);
        Assert.Contains("--no-first-run", args);
        Assert.Contains("--no-default-browser-check", args);
    }

    [Fact]
    public void 参数里不手工加引号交给进程启动器转义()
    {
        var request = BaseRequest() with { ProfileDirectory = @"D:\Fb Profiles\acc 01" };

        var args = ChromeOptionsBuilder.Build(request);

        // 手工加引号会和 ArgumentList 的转义叠加，变成被转义的双引号，Chrome 会当成路径的一部分
        Assert.All(args, arg => Assert.DoesNotContain('"', arg));
        Assert.Contains(@"--user-data-dir=D:\Fb Profiles\acc 01", args);
    }

    [Fact]
    public void 路径末尾的分隔符会被去掉()
    {
        var request = BaseRequest() with { ProfileDirectory = @"D:\FbProfiles\acc01\" };

        var args = ChromeOptionsBuilder.Build(request);

        Assert.Contains(@"--user-data-dir=D:\FbProfiles\acc01", args);
    }

    [Fact]
    public void 默认不带无头参数()
    {
        var args = ChromeOptionsBuilder.Build(BaseRequest());

        Assert.DoesNotContain(args, a => a.StartsWith("--headless", StringComparison.Ordinal));
    }

    [Fact]
    public void 显式要求无头时才加headless()
    {
        var args = ChromeOptionsBuilder.Build(BaseRequest() with { Headless = true });

        Assert.Contains("--headless=new", args);
    }

    [Theory]
    [InlineData("127.0.0.1:7890", "http://127.0.0.1:7890")]
    [InlineData("http://127.0.0.1:7890", "http://127.0.0.1:7890")]
    [InlineData("socks5://127.0.0.1:1080", "socks5://127.0.0.1:1080")]
    public void 代理补全协议头(string input, string expected)
    {
        var args = ChromeOptionsBuilder.Build(BaseRequest() with { Proxy = input });

        Assert.Contains($"--proxy-server={expected}", args);
    }

    [Fact]
    public void 未配代理时不加代理参数()
    {
        var args = ChromeOptionsBuilder.Build(BaseRequest() with { Proxy = "  " });

        Assert.DoesNotContain(args, a => a.StartsWith("--proxy-server", StringComparison.Ordinal));
    }

    [Fact]
    public void 追加参数原样透传且过滤空白()
    {
        var args = ChromeOptionsBuilder.Build(BaseRequest() with
        {
            ExtraArguments = ["--lang=zh-CN", "", "  ", "--window-size=1440,900"],
        });

        Assert.Contains("--lang=zh-CN", args);
        Assert.Contains("--window-size=1440,900", args);
        Assert.DoesNotContain(args, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void 没给profile目录直接拒绝()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ChromeOptionsBuilder.Build(new BrowserLaunchRequest { ProfileDirectory = "  " }));

        Assert.Contains("user-data-dir", ex.Message);
    }
}
