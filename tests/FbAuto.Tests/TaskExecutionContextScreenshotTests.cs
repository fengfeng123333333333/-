using FbAuto.Application.Execution;
using FbAuto.Browser.Abstractions;
using FbAuto.Core.Models;
using FbAuto.Tests.Support;
using Microsoft.Playwright;
using Moq;

namespace FbAuto.Tests;

/// <summary>
/// 失败截图这一小块。盯两件事：**文件名必须唯一**、**别名里的非法字符不能把截图整个丢掉**。
///
/// 为什么值得单独测：截图失败是「吞掉异常」的设计（截图不该让失败变成崩溃），
/// 所以它坏掉的时候一句日志都没有，只有使用者打开目录才发现「三条失败任务只有一张图」。
/// 并发跑批时这种情况很容易出现，事后又完全无法复现。
/// </summary>
public sealed class TaskExecutionContextScreenshotTests
{
    /// <summary>建一个上下文，页面截图会真的往磁盘写文件，好让「有没有覆盖」看得见。</summary>
    private static TaskExecutionContext NewContext(TempDirectory temp, string alias, out string shotDirectory)
    {
        shotDirectory = temp.Combine("shots");

        var page = new Mock<IPage>();
        page.Setup(p => p.ScreenshotAsync(It.IsAny<PageScreenshotOptions>()))
            .Returns<PageScreenshotOptions?>(options =>
            {
                File.WriteAllBytes(options!.Path!, [0x89, 0x50, 0x4E, 0x47]);
                return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            });

        return new TaskExecutionContext
        {
            Task = new AutomatedTask
            {
                Kind = TaskKind.VideoUploadGoogle,
                Channel = BrowserChannelKind.Google,
                AccountAlias = alias,
            },
            Account = new AccountEntry { Alias = alias, Channel = BrowserChannelKind.Google },
            Channel = new FakeBrowserChannel(),
            Session = new BrowserSession { Kind = BrowserChannelKind.Google },
            Page = page.Object,
            Logger = new TestLogger(),
            ScreenshotDirectory = shotDirectory,
        };
    }

    /// <summary>
    /// 同一秒内的两次失败必须落成两个文件。
    /// 原来是 yyyyMMdd_HHmmss，并发下同秒失败会互相覆盖——三条任务都写着「有截图」，磁盘上只剩一张。
    /// </summary>
    [Fact]
    public async Task 同一秒内的两次失败截图不会互相覆盖()
    {
        using var temp = new TempDirectory();
        var context = NewContext(temp, "acc01", out var shotDirectory);

        // 同一个上下文连着失败两次，必然落在同一秒内——这正是原来会覆盖的场景。
        var first = await context.FailAsync("第一次失败");
        var second = await context.FailAsync("第二次失败");

        Assert.NotNull(first.ScreenshotPath);
        Assert.NotNull(second.ScreenshotPath);
        Assert.NotEqual(first.ScreenshotPath, second.ScreenshotPath);

        Assert.Equal(2, Directory.GetFiles(shotDirectory, "*.png").Length);
    }

    /// <summary>
    /// 别名带路径分隔符时截图仍然要落盘。
    /// 别名来自手工维护的 CSV，写个 <c>acc/01</c> 完全可能；
    /// 直接拼进路径会变成子目录，Directory 不存在 → 抛异常 → 被 catch 吞掉 → 使用者只看到「没截图」。
    /// </summary>
    [Theory]
    [InlineData("acc/01")]
    [InlineData("acc:01")]
    [InlineData("acc*01")]
    public async Task 别名含非法文件名字符时截图仍然能保存(string alias)
    {
        using var temp = new TempDirectory();
        var context = NewContext(temp, alias, out var shotDirectory);

        var result = await context.FailAsync("失败");

        Assert.NotNull(result.ScreenshotPath);
        Assert.True(File.Exists(result.ScreenshotPath!), $"截图没落盘：{result.ScreenshotPath}");

        // 文件名里不能残留非法字符，否则 Windows 上根本建不出来。
        var fileName = Path.GetFileName(result.ScreenshotPath!);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => fileName.Contains(c));
    }

    /// <summary>别名为空也不能生成 <c>__</c> 这种分不清是谁的文件。</summary>
    [Fact]
    public async Task 别名为空时文件名用固定占位而不是空白()
    {
        using var temp = new TempDirectory();
        var context = NewContext(temp, string.Empty, out _);

        var result = await context.FailAsync("失败");

        Assert.NotNull(result.ScreenshotPath);
        Assert.Contains("unknown", Path.GetFileName(result.ScreenshotPath!));
    }
}
