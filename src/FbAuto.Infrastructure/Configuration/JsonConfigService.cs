using System.Text.Encodings.Web;
using System.Text.Json;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Configuration;

namespace FbAuto.Infrastructure.Configuration;

/// <summary>
/// config.json 读写。放在程序目录下（与原版 settings.json 同位），首次启动生成默认文件。
/// 文件损坏时保留默认值并告警，绝不让配置问题挡住启动。
/// </summary>
public sealed class JsonConfigService : IConfigService
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IAppLogger _logger;

    public JsonConfigService(string baseDirectory, IAppLogger logger)
    {
        _logger = logger;
        ConfigFilePath = Path.Combine(baseDirectory, "config.json");
    }

    public string ConfigFilePath { get; }

    public AppConfig Current { get; private set; } = new();

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
            {
                Current = new AppConfig();
                Save();
                _logger.Info($"配置文件不存在，已生成默认配置：{ConfigFilePath}");
                return Current;
            }

            var json = File.ReadAllText(ConfigFilePath);
            Current = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions) ?? new AppConfig();
            _logger.Info($"配置已加载：并发数={Current.ConcurrentCount} 启动间隔={Current.BrowserOpenIntervalSeconds} 秒");
            return Current;
        }
        catch (Exception ex)
        {
            Current = new AppConfig();
            _logger.Error($"配置读取失败，回退默认值：{ConfigFilePath}", ex);
            return Current;
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, WriteOptions);
            File.WriteAllText(ConfigFilePath, json);
            _logger.Info($"配置已保存：{ConfigFilePath}");
        }
        catch (Exception ex)
        {
            _logger.Error($"配置保存失败：{ConfigFilePath}", ex);
        }
    }
}
