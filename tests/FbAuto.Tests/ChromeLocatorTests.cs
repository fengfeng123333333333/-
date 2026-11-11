using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Chrome;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// S2 单测：找 Chrome。显式配错路径必须报错，不能偷偷换成别的浏览器——
/// 用户指定用哪个就哪个，静默替换会让人以为是同一套环境在跑。
/// </summary>
public sealed class ChromeLocatorTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    [Fact]
    public void 显式路径存在时原样返回()
    {
        var fake = _dir.Combine("chrome.exe");
        File.WriteAllText(fake, "fake");

        Assert.Equal(fake, ChromeLocator.Locate(fake));
    }

    [Fact]
    public void 显式路径不存在时报中文错误并带上路径()
    {
        var missing = _dir.Combine("nope", "chrome.exe");

        var ex = Assert.Throws<BrowserChannelException>(() => ChromeLocator.Locate(missing));

        Assert.Equal(BrowserChannelFailure.ExecutableNotFound, ex.Reason);
        Assert.Contains("找不到 Chrome", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void 显式路径为空时自动查找()
    {
        var found = ChromeLocator.TryLocate();

        // 本机装了 Chrome 才断言；没装的话这条只验证「不抛异常」
        if (found is null)
        {
            return;
        }

        Assert.True(File.Exists(found));
        Assert.EndsWith(ChromeLocator.ExecutableName, found, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 自动查找的候选路径都指向chrome可执行文件()
    {
        foreach (var candidate in ChromeLocator.DefaultCandidates())
        {
            Assert.EndsWith("chrome.exe", candidate, StringComparison.OrdinalIgnoreCase);
            Assert.True(Path.IsPathRooted(candidate));
        }
    }

    public void Dispose() => _dir.Dispose();
}
