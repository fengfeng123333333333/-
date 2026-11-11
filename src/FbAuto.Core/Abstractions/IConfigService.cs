using FbAuto.Core.Configuration;

namespace FbAuto.Core.Abstractions;

/// <summary>
/// 配置文件读写。首次启动生成默认文件；文件损坏时退回默认值并告警，不阻塞启动。
/// </summary>
public interface IConfigService
{
    /// <summary>配置文件绝对路径。</summary>
    string ConfigFilePath { get; }

    /// <summary>当前生效配置（一定是非 null 实例）。</summary>
    AppConfig Current { get; }

    /// <summary>从磁盘加载；文件不存在则创建默认文件后返回默认值。</summary>
    AppConfig Load();

    /// <summary>把 <see cref="Current"/> 写回磁盘。</summary>
    void Save();
}
