using FbAuto.Core.Abstractions;

namespace FbAuto.Tests.Support;

/// <summary>
/// 内存日志，用于断言「服务是否把该报的事报了出来」，不落盘、不依赖真实日志文件。
/// </summary>
public sealed class TestLogger : IAppLogger
{
    private readonly List<string> _entries = [];

    public string LogFilePath => "(memory)";

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Info(string message) => Append("INF", message);

    public void Warn(string message) => Append("WRN", message);

    public void Error(string message, Exception? exception = null)
        => Append("ERR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    public bool Contains(string level, string fragment)
        => Entries.Any(e => e.StartsWith($"[{level}]", StringComparison.Ordinal)
                            && e.Contains(fragment, StringComparison.Ordinal));

    private void Append(string level, string message)
    {
        lock (_entries)
        {
            _entries.Add($"[{level}] {message}");
        }
    }
}
