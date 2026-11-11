using System.Text;
using FbAuto.Core.Abstractions;

namespace FbAuto.Infrastructure.Logging;

/// <summary>
/// 文件日志。每天一个文件（App_yyyyMMdd.log），追加写入，进程内加锁。
/// 原版的教训：缺运行时时程序静默退出、一行日志都没有，排障只能靠猜。所以日志要在最早时机落盘。
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly Lock _gate = new();
    private readonly string _logDirectory;

    public FileAppLogger(string logDirectory)
    {
        _logDirectory = logDirectory;
        Directory.CreateDirectory(_logDirectory);
        LogFilePath = Path.Combine(_logDirectory, $"App_{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogFilePath { get; }

    public void Info(string message) => Write("INF", message);

    public void Warn(string message) => Write("WRN", message);

    public void Error(string message, Exception? exception = null)
    {
        var text = exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}";
        Write("ERR", text);
        if (exception is not null)
        {
            Write("ERR", exception.StackTrace ?? "(无堆栈)");
        }
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
        try
        {
            lock (_gate)
            {
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写不进去不能反过来打断程序，S9 的首启自检会单独校验日志可写。
        }
    }
}
