using FbAuto.Application.Execution;
using FbAuto.Core.Models;

namespace FbAuto.Tests;

/// <summary>
/// 执行选项里「按通道查导出目录」这件事。
/// </summary>
/// <remarks>
/// 单独写用例是因为它有三个容易写错的出口：没配整张表、表里没这个通道、表里是空串。
/// 三种都必须返回 <c>null</c> —— 返回 <c>""</c> 的话下游会把「当前工作目录」当成导出目录，
/// 表现是文件莫名其妙出现在程序目录里，而使用者明明没配。
/// </remarks>
public sealed class TaskRunOptionsTests
{
    [Fact]
    public void 没配导出目录表时任何通道都返回空()
    {
        var options = new TaskRunOptions();

        Assert.Null(options.ExportDirectoryFor(BrowserChannelKind.Google));
        Assert.Null(options.ExportDirectoryFor(BrowserChannelKind.Bit));
    }

    [Fact]
    public void 只配了谷歌时比特拿不到目录()
    {
        var options = new TaskRunOptions
        {
            ExportDirectories = new Dictionary<BrowserChannelKind, string>
            {
                [BrowserChannelKind.Google] = @"D:\g\export",
            },
        };

        Assert.Equal(@"D:\g\export", options.ExportDirectoryFor(BrowserChannelKind.Google));
        Assert.Null(options.ExportDirectoryFor(BrowserChannelKind.Bit));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void 表里是空白串时按没配处理(string directory)
    {
        var options = new TaskRunOptions
        {
            ExportDirectories = new Dictionary<BrowserChannelKind, string>
            {
                [BrowserChannelKind.Google] = directory,
            },
        };

        Assert.Null(options.ExportDirectoryFor(BrowserChannelKind.Google));
    }
}
