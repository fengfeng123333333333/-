using FbAuto.Infrastructure.Logging;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 冒烟测试 2：日志必须按天落盘、追加不覆盖、并发不残行，且写不进去时不能反过来打断程序。
/// 这条对应原版最大的排障黑洞——程序静默退出、一行日志都没有。
/// </summary>
public sealed class FileAppLoggerTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    [Fact]
    public void 日志文件名按天生成()
    {
        var logger = new FileAppLogger(_dir.Path);

        Assert.Equal($"App_{DateTime.Now:yyyyMMdd}.log", Path.GetFileName(logger.LogFilePath));
        Assert.Equal(_dir.Path, Path.GetDirectoryName(logger.LogFilePath));
    }

    [Fact]
    public void 同一天多次写入是追加而不是覆盖()
    {
        var logger = new FileAppLogger(_dir.Path);

        logger.Info("第一条");
        logger.Warn("第二条");
        logger.Info("第三条");

        var lines = File.ReadAllLines(logger.LogFilePath);
        Assert.Equal(3, lines.Length);
        Assert.Contains("第一条", lines[0]);
        Assert.StartsWith("[", lines[0]);
        Assert.Contains("[INF]", lines[0]);
        Assert.Contains("[WRN]", lines[1]);
        Assert.Contains("第三条", lines[2]);
    }

    [Fact]
    public void 每次写入都带毫秒级时间戳()
    {
        var logger = new FileAppLogger(_dir.Path);
        logger.Info("时间戳检查");

        var line = File.ReadAllLines(logger.LogFilePath)[0];

        // [yyyy-MM-dd HH:mm:ss.fff] [INF] 消息
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[INF\] 时间戳检查$", line);
    }

    [Fact]
    public void 错误日志包含异常类型与堆栈()
    {
        var logger = new FileAppLogger(_dir.Path);
        Exception captured;
        try
        {
            throw new InvalidOperationException("模拟故障");
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        logger.Error("执行失败", captured);

        var text = File.ReadAllText(logger.LogFilePath);
        Assert.Contains("[ERR] 执行失败 | InvalidOperationException: 模拟故障", text);
        Assert.Contains(nameof(FileAppLoggerTests), text); // 堆栈里必然出现测试类名
    }

    [Fact]
    public void 并发写入不产生残行()
    {
        var logger = new FileAppLogger(_dir.Path);

        Parallel.For(0, 60, i => logger.Info($"并发-{i}"));

        var lines = File.ReadAllLines(logger.LogFilePath);
        Assert.Equal(60, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} [^\]]+\] \[INF\] 并发-\d+$", line));
    }

    [Fact]
    public void 日志目录建不出来时构造与写入都不抛异常()
    {
        // 用一个「文件」当父目录，制造 CreateDirectory 必然失败的场景
        var blocker = Path.Combine(_dir.Path, "blocker");
        File.WriteAllText(blocker, "占位");

        var logger = new FileAppLogger(Path.Combine(blocker, "Logs"));

        // 关键：日志写不进去不能反过来打断程序启动
        var ex = Record.Exception(() =>
        {
            logger.Info("写入应当静默失败");
            logger.Error("错误日志同样静默失败", new InvalidOperationException("x"));
        });

        Assert.Null(ex);
        Assert.False(File.Exists(logger.LogFilePath));
    }

    public void Dispose() => _dir.Dispose();
}
