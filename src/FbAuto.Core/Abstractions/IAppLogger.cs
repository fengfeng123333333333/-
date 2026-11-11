namespace FbAuto.Core.Abstractions;

/// <summary>
/// 应用日志。S1 只需落盘与可查，滚动与归档在 S9 补齐。
/// </summary>
public interface IAppLogger
{
    /// <summary>当前日志文件绝对路径，启动时打一次便于排障。</summary>
    string LogFilePath { get; }

    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? exception = null);
}
