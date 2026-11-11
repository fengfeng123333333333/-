using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Bit;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：比特本地接口客户端。用真 TCP 桩服务，走完「发请求 → 读 JSON → 解析」整条路，
/// 只 mock 掉比特进程。这里顺带钉住一件事：客户端没开时的报错必须是中文且说清原因。
/// </summary>
public sealed class BitBrowserApiClientTests
{
    [Fact]
    public async Task 打开窗口发出正确的请求体并解析返回()
    {
        await using var server = new FakeHttpServer(path => path switch
        {
            "/browser/open" => """{"code":0,"data":{"ws":"ws://127.0.0.1:6001/devtools/browser/abc","http":"127.0.0.1:6001","pid":777}}""",
            _ => """{"code":404,"msg":"未知接口"}""",
        });
        using var client = new BitBrowserApiClient(server.BaseUrl);

        var result = await client.OpenAsync("profile-001");

        Assert.Equal("ws://127.0.0.1:6001/devtools/browser/abc", result.WsEndpoint);
        Assert.Equal(777, result.ProcessId);

        var request = Assert.Single(server.Requests);
        Assert.Equal("/browser/open", request.Path);
        Assert.Contains("profile-001", request.Body);
        Assert.Contains("\"id\"", request.Body);
    }

    [Fact]
    public async Task 关闭窗口打到close接口并带窗口ID()
    {
        await using var server = new FakeHttpServer(_ => """{"code":0,"msg":"success"}""");
        using var client = new BitBrowserApiClient(server.BaseUrl);

        await client.CloseAsync("profile-002");

        var request = Assert.Single(server.Requests);
        Assert.Equal("/browser/close", request.Path);
        Assert.Contains("profile-002", request.Body);
    }

    [Fact]
    public async Task 客户端没开时报中文错误并说明是本地接口连不上()
    {
        // 先占一个端口再放掉，确保这个端口确实没人监听
        int deadPort;
        await using (var probe = new FakeHttpServer(_ => "{}"))
        {
            deadPort = probe.Port;
        }

        var baseUrl = $"http://127.0.0.1:{deadPort}";
        using var client = new BitBrowserApiClient(baseUrl);

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() => client.OpenAsync("profile-003"));

        Assert.Equal(BrowserChannelFailure.ClientNotRunning, ex.Reason);
        Assert.Contains("比特浏览器未启动", ex.Message);
        Assert.Contains(baseUrl, ex.Message);
        Assert.Contains("请先打开比特浏览器客户端", ex.Message);
    }

    [Fact]
    public async Task 接口返回失败码时抛ApiRejected()
    {
        await using var server = new FakeHttpServer(_ => """{"code":10001,"msg":"浏览器实例不存在"}""");
        using var client = new BitBrowserApiClient(server.BaseUrl);

        var ex = await Assert.ThrowsAsync<BrowserChannelException>(() => client.OpenAsync("missing"));

        Assert.Equal(BrowserChannelFailure.ApiRejected, ex.Reason);
        Assert.Contains("浏览器实例不存在", ex.Message);
    }

    [Theory]
    [InlineData(null, "http://127.0.0.1:54345")]
    [InlineData("", "http://127.0.0.1:54345")]
    [InlineData("127.0.0.1:6000", "http://127.0.0.1:6000")]
    [InlineData("http://127.0.0.1:6000/", "http://127.0.0.1:6000")]
    [InlineData(" https://bit.local:9000 ", "https://bit.local:9000")]
    public void 接口地址归一化(string? input, string expected)
    {
        Assert.Equal(expected, BitBrowserApiClient.NormalizeBaseUrl(input));
    }
}
