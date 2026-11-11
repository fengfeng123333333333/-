using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Bit;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：比特接口返回解析。不同版本字段不一（code/success、msg/message、pid 有时是字符串），
/// 解析器必须都能吃下，失败时给的中文提示要能直接照着排查。
/// </summary>
public sealed class BitApiParserTests
{
    private const string NormalJson = """
        {"code":0,"msg":"成功","data":{"ws":"ws://127.0.0.1:5001/devtools/browser/abc","http":"127.0.0.1:5001","pid":4321,"dir":"C:\\BitProfiles\\p1","name":"窗口1"}}
        """;

    [Fact]
    public void 标准返回解析出全部字段()
    {
        var result = BitApiParser.ParseOpen(NormalJson);

        Assert.Equal("ws://127.0.0.1:5001/devtools/browser/abc", result.WsEndpoint);
        Assert.Equal("127.0.0.1:5001", result.HttpEndpoint);
        Assert.Equal(4321, result.ProcessId);
        Assert.Equal(@"C:\BitProfiles\p1", result.Directory);
        Assert.Equal("窗口1", result.Name);
    }

    [Fact]
    public void 兼容success字段写法()
    {
        const string json = """
            {"success":true,"data":{"ws":"ws://127.0.0.1:5002/devtools/browser/x","pid":9}}
            """;

        var result = BitApiParser.ParseOpen(json);

        Assert.Equal("ws://127.0.0.1:5002/devtools/browser/x", result.WsEndpoint);
        Assert.Equal(9, result.ProcessId);
        Assert.Null(result.HttpEndpoint);
    }

    [Fact]
    public void pid是字符串也能解析()
    {
        const string json = """
            {"code":0,"data":{"ws":"ws://127.0.0.1:5003/devtools/browser/x","pid":"4321"}}
            """;

        Assert.Equal(4321, BitApiParser.ParseOpen(json).ProcessId);
    }

    [Fact]
    public void 业务失败抛出中文异常并带比特自己的消息()
    {
        const string json = """
            {"code":10001,"msg":"浏览器实例不存在"}
            """;

        var ex = Assert.Throws<BrowserChannelException>(() => BitApiParser.ParseOpen(json));

        Assert.Equal(BrowserChannelFailure.ApiRejected, ex.Reason);
        Assert.Contains("打开窗口", ex.Message);
        Assert.Contains("10001", ex.Message);
        Assert.Contains("浏览器实例不存在", ex.Message);
    }

    [Theory]
    [InlineData("""{"code":0}""", "data")]
    [InlineData("""{"code":0,"data":{"http":"127.0.0.1:5001"}}""", "ws")]
    [InlineData("""{"code":0,"data":{"ws":"   "}}""", "ws")]
    public void 缺少关键字段时报明确的缺失项(string json, string expectedFragment)
    {
        var ex = Assert.Throws<BrowserChannelException>(() => BitApiParser.ParseOpen(json));

        Assert.Equal(BrowserChannelFailure.ApiRejected, ex.Reason);
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("")]
    public void 返回内容不合法时给出可读提示(string json)
    {
        var ex = Assert.Throws<BrowserChannelException>(() => BitApiParser.ParseOpen(json));

        Assert.Equal(BrowserChannelFailure.ApiRejected, ex.Reason);
        Assert.True(
            ex.Message.Contains("合法 JSON", StringComparison.Ordinal)
            || ex.Message.Contains("JSON 对象", StringComparison.Ordinal)
            || ex.Message.Contains("空内容", StringComparison.Ordinal),
            ex.Message);
    }

    [Fact]
    public void 关闭窗口成功时不抛异常()
    {
        BitApiParser.EnsureSuccess("""{"code":0,"msg":"success"}""", "关闭窗口");
    }

    [Fact]
    public void 关闭窗口失败时抛中文异常()
    {
        var ex = Assert.Throws<BrowserChannelException>(() =>
            BitApiParser.EnsureSuccess("""{"code":400,"msg":"窗口未打开"}""", "关闭窗口"));

        Assert.Contains("关闭窗口", ex.Message);
        Assert.Contains("窗口未打开", ex.Message);
    }

    [Fact]
    public void 没有code也没有success但有data时算成功()
    {
        var envelope = BitApiParser.ParseEnvelope("""{"data":{"ws":"ws://x"}}""", "打开窗口");

        Assert.True(envelope.Success);
        Assert.Null(envelope.Code);
    }
}
