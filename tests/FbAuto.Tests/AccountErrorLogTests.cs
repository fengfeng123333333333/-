using System.Text;
using FbAuto.Application.Execution;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 「资料修改结果保存目录」里那份账号失败清单。
/// </summary>
/// <remarks>
/// 这几条用例盯的是**格式**：文件名与行内字段全部照原版串抄的
/// （<c>AccountErrors_yyyyMMdd.txt</c> / <c>[账号失败] 浏览器: … | assetId: … | 原因: …</c>），
/// 「看起来差不多」不行 —— 使用者可能拿我们产出的文件去跟原版对比。
/// </remarks>
public sealed class AccountErrorLogTests
{
    private static readonly DateTime FixedTime = new(2026, 11, 16, 9, 5, 3, 27, DateTimeKind.Local);

    [Fact]
    public void 行格式与原版串一致()
    {
        var line = AccountErrorLog.ComposeLine(FixedTime, "Chrome-acc01", "abc123", "登录态失效");

        Assert.Equal("[2026-11-16 09:05:03.027] [账号失败] 浏览器:Chrome-acc01 | assetId:abc123 | 原因:登录态失效", line);
    }

    /// <summary>
    /// 时间格式不能退化成 <c>yyyy-MM-dd HH:mm:ss</c>：同一秒内的两条失败要能分开，
    /// 否则「哪个账号先挂」就无从判断。
    /// </summary>
    [Fact]
    public void 时间带毫秒且同一秒两条能区分()
    {
        var a = AccountErrorLog.ComposeLine(FixedTime, "Chrome-acc01", "", "x");
        var b = AccountErrorLog.ComposeLine(FixedTime.AddMilliseconds(400), "Chrome-acc01", "", "x");

        Assert.NotEqual(a, b);
        Assert.Contains(".027]", a);
        Assert.Contains(".427]", b);
    }

    [Fact]
    public void 文件名按日期且带txt扩展名()
    {
        Assert.Equal("AccountErrors_20261116.txt", AccountErrorLog.FileNameFor(FixedTime));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 没配目录时记了也不落盘(string? directory)
    {
        var log = new AccountErrorLog();

        log.Record(directory, FixedTime, "Chrome-acc01", "abc", "失败");

        Assert.Equal(0, log.Count);
        Assert.Empty(log.Flush(FixedTime));
    }

    [Fact]
    public void 两个目录各写各的文件()
    {
        using var temp = new TempDirectory();
        var google = temp.Combine("google");
        var bit = temp.Combine("bit");

        var log = new AccountErrorLog();
        log.Record(google, FixedTime, "Chrome-acc01", "", "谷歌版失败");
        log.Record(bit, FixedTime, "Bit-acc02", "asset-9", "比特版失败");

        var written = log.Flush(FixedTime);

        Assert.Equal(2, written.Count);
        Assert.Equal(2, log.Count);

        var googleText = File.ReadAllText(Path.Combine(google, "AccountErrors_20261116.txt"));
        var bitText = File.ReadAllText(Path.Combine(bit, "AccountErrors_20261116.txt"));

        Assert.Contains("谷歌版失败", googleText);
        Assert.DoesNotContain("比特版失败", googleText);
        Assert.Contains("比特版失败", bitText);
        Assert.DoesNotContain("谷歌版失败", bitText);
        Assert.Contains("assetId:asset-9", bitText);
    }

    /// <summary>
    /// 同一天跑第二轮不能把第一轮的记录冲掉。
    /// 原版只暴露 <c>WriteAllText</c>（整体覆盖），直接覆盖就会丢历史。
    /// </summary>
    [Fact]
    public void 同一天再写一轮接在已有记录后面()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "AccountErrors_20261116.txt");
        File.WriteAllText(path, "[2026-11-16 08:00:00.000] [账号失败] 浏览器:Chrome-old | assetId: | 原因:上一轮\n",
            new UTF8Encoding(false));

        var log = new AccountErrorLog();
        log.Record(temp.Path, FixedTime, "Chrome-new", "", "这一轮");
        log.Flush(FixedTime);

        var lines = File.ReadAllLines(path);

        Assert.Equal(2, lines.Length);
        Assert.Contains("Chrome-old", lines[0]);
        Assert.Contains("Chrome-new", lines[1]);
    }

    /// <summary>
    /// 已有文件末尾没有换行时，新记录不能被粘在同一行上
    /// —— 手工编辑过的文件经常是这样。
    /// </summary>
    [Fact]
    public void 已有文件末尾没换行时不粘行()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "AccountErrors_20261116.txt");
        File.WriteAllText(path, "旧的一行没有换行", new UTF8Encoding(false));

        var log = new AccountErrorLog();
        log.Record(temp.Path, FixedTime, "Chrome-acc01", "", "新的");
        log.Flush(FixedTime);

        var lines = File.ReadAllLines(path);

        Assert.Equal(2, lines.Length);
        Assert.Equal("旧的一行没有换行", lines[0]);
        Assert.Contains("新的", lines[1]);
    }

    /// <summary>
    /// 目录建不出来（这里是「路径上有个同名文件」）时不能抛：
    /// 导出是附加产物，不能因为磁盘问题把本轮结论改成「崩了」。
    /// </summary>
    [Fact]
    public void 写盘失败不抛而是回调报错()
    {
        using var temp = new TempDirectory();
        var blocker = temp.Combine("不可能是目录");
        File.WriteAllText(blocker, "x", new UTF8Encoding(false));

        var log = new AccountErrorLog();
        log.Record(Path.Combine(blocker, "sub"), FixedTime, "Chrome-acc01", "", "失败");

        var errors = new List<string>();
        var written = log.Flush(FixedTime, errors.Add);

        Assert.Empty(written);
        Assert.Single(errors);
        Assert.Contains("不影响任务结论", errors[0]);
    }

    /// <summary>
    /// 目标目录不存在时要自己建 —— 使用者在界面上手填的路径，第一次跑时通常还不存在。
    /// </summary>
    [Fact]
    public void 目录不存在时自动创建()
    {
        using var temp = new TempDirectory();
        var nested = temp.Combine("a", "b", "c");

        var log = new AccountErrorLog();
        log.Record(nested, FixedTime, "Chrome-acc01", "", "失败");
        var written = log.Flush(FixedTime);

        Assert.Single(written);
        Assert.True(File.Exists(written[0]));
        Assert.Contains("失败", File.ReadAllText(written[0]));
    }
}
