using FbAuto.Browser.Chrome;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：DevToolsActivePort 文件解析。
/// 这个文件是拿到 ws 地址的唯一可靠来源，解析错一位，连的就是别人家的浏览器。
/// </summary>
public sealed class DevToolsActivePortFileTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    [Fact]
    public void 正常内容解析出端口与ws地址()
    {
        var parsed = DevToolsActivePortFile.Parse("51234\n/devtools/browser/0b9c1d2e-aaaa-bbbb-cccc-1234567890ab\n");

        Assert.NotNull(parsed);
        Assert.Equal(51234, parsed!.Port);
        Assert.Equal("/devtools/browser/0b9c1d2e-aaaa-bbbb-cccc-1234567890ab", parsed.BrowserPath);
        Assert.Equal("ws://127.0.0.1:51234/devtools/browser/0b9c1d2e-aaaa-bbbb-cccc-1234567890ab", parsed.WebSocketUrl);
        Assert.Equal("http://127.0.0.1:51234/json/version", parsed.VersionUrl);
    }

    [Theory]
    [InlineData("51234")]                        // 只有端口，Chrome 还没写完
    [InlineData("")]                             // 空文件
    [InlineData("abc\n/devtools/browser/x")]     // 端口不是数字
    [InlineData("0\n/devtools/browser/x")]       // 端口 0 不是有效监听端口
    [InlineData("70000\n/devtools/browser/x")]   // 超出端口范围
    [InlineData("51234\ndevtools/browser/x")]    // 第二行不是绝对路径
    public void 内容不完整返回null(string content)
    {
        Assert.Null(DevToolsActivePortFile.Parse(content));
    }

    [Fact]
    public void 兼容Windows换行与多余行()
    {
        var parsed = DevToolsActivePortFile.Parse("51234\r\n/devtools/browser/abc\r\n/extra\r\n");

        Assert.NotNull(parsed);
        Assert.Equal(51234, parsed!.Port);
        Assert.Equal("/devtools/browser/abc", parsed.BrowserPath);
    }

    [Fact]
    public void 读取不存在的目录返回null而不是抛异常()
    {
        Assert.Null(DevToolsActivePortFile.TryRead(Path.Combine(_dir.Path, "not-exists")));
    }

    [Fact]
    public void 读取真实写入的文件()
    {
        var profile = _dir.Combine("profile");
        Directory.CreateDirectory(profile);
        File.WriteAllText(DevToolsActivePortFile.GetPath(profile), "49222\n/devtools/browser/xyz");

        var parsed = DevToolsActivePortFile.TryRead(profile);

        Assert.NotNull(parsed);
        Assert.Equal(49222, parsed!.Port);
    }

    public void Dispose() => _dir.Dispose();
}
