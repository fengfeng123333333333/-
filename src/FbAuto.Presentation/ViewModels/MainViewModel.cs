using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;

namespace FbAuto.Presentation.ViewModels;

/// <summary>
/// 主界面 ViewModel。S1 只负责：三栏数据、添加/删除任务、配置回填与保存。
/// 真正的执行链路由 S4（上传）/ S5（资料）/ S7（删消息）注入，这里只留命令出口。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>浏览器列表加载完成前的占位文案，与原版一致。</summary>
    public const string BrowserLoadingPlaceholder = "正在加载列表...";

    private readonly IConfigService _configService;
    private readonly IAppLogger _logger;

    public MainViewModel(IConfigService configService, IAppLogger logger)
    {
        _configService = configService;
        _logger = logger;

        var config = configService.Current;
        _concurrentCount = config.ConcurrentCount;
        _browserOpenIntervalSeconds = config.BrowserOpenIntervalSeconds;
        _videoDirectory = config.VideoDirectory;
        _descriptionFile = config.DescriptionFile;
        _saveDirectory = config.SaveDirectory;
        _browserType = config.BrowserType;
        _scheduledDate = config.ScheduledDate;
        _scheduledStartHour = config.ScheduledStartHour;
        _messageBlockWords = string.Join(",", config.MessageBlockWords);

        // S2 接管浏览器后替换为真实列表；之前保持占位，不假装已就绪。
        Browsers.Add(BrowserLoadingPlaceholder);

        Tasks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(TaskSummary));
        };
    }

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    public ObservableCollection<string> Browsers { get; } = [];

    public IReadOnlyList<TaskTypeDescriptor> TaskTypes => TaskTypeCatalog.All;

    public IReadOnlyList<string> BrowserTypes { get; } = ["谷歌版", "比特版"];

    [ObservableProperty]
    private TaskItemViewModel? _selectedTask;

    [ObservableProperty]
    private int _concurrentCount;

    [ObservableProperty]
    private int _browserOpenIntervalSeconds;

    [ObservableProperty]
    private string _videoDirectory;

    [ObservableProperty]
    private string _descriptionFile;

    [ObservableProperty]
    private string _saveDirectory;

    [ObservableProperty]
    private string _browserType;

    [ObservableProperty]
    private DateTime? _scheduledDate;

    [ObservableProperty]
    private int _scheduledStartHour;

    [ObservableProperty]
    private string _messageBlockWords;

    /// <summary>队列空态判断：空时显示「暂无任务，请从左侧添加」。</summary>
    public bool HasTasks => Tasks.Count > 0;

    /// <summary>顶栏浏览器下拉是否已可用；占位状态下禁用，避免点了没反应。</summary>
    public bool IsBrowserListReady => Browsers.Count > 0 && Browsers[0] != BrowserLoadingPlaceholder;

    public string TaskSummary =>
        Tasks.Count == 0
            ? "队列为空"
            : $"共 {Tasks.Count} 条 · 待执行 {Tasks.Count(t => t.Status == TaskRunStatus.Pending)} 条";

    [RelayCommand]
    private void AddTask(TaskTypeDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        var task = new AutomatedTask
        {
            Kind = descriptor.Kind,
            Channel = descriptor.Channel,
        };

        var item = new TaskItemViewModel(task)
        {
            BrowserName = descriptor.Channel == BrowserChannelKind.Google ? "Chrome-未分配" : "Bit-未分配",
            Result = "—",
        };

        Tasks.Add(item);
        SelectedTask = item;
        _logger.Info($"添加任务：{descriptor.DisplayName}");
    }

    [RelayCommand]
    private void RemoveTask()
    {
        if (SelectedTask is null)
        {
            _logger.Warn("删除任务被忽略：未选中任何任务");
            return;
        }

        var removed = SelectedTask;
        Tasks.Remove(removed);
        SelectedTask = Tasks.FirstOrDefault();
        _logger.Info($"删除任务：{removed.DisplayName}");
    }

    [RelayCommand]
    private void StartTasks()
    {
        // S4 起接真实执行链路；此刻如实记录，不假装已在跑。
        if (Tasks.Count == 0)
        {
            _logger.Warn("开始任务被忽略：队列为空");
            return;
        }

        _logger.Info($"开始任务（S1 骨架，执行链路尚未接入）：当前队列 {Tasks.Count} 条，并发数 {ConcurrentCount}");
    }

    [RelayCommand]
    private void StopTasks() => _logger.Info("停止任务（S1 骨架，执行链路尚未接入）");

    /// <summary>
    /// 把界面上的值写回配置并落盘。窗口关闭时调用，对应验收「改 config.json 重启后生效」的反向路径。
    /// </summary>
    public void PersistConfig()
    {
        var config = _configService.Current;
        config.ConcurrentCount = Math.Max(1, ConcurrentCount);
        config.BrowserOpenIntervalSeconds = Math.Max(0, BrowserOpenIntervalSeconds);
        config.VideoDirectory = VideoDirectory;
        config.DescriptionFile = DescriptionFile;
        config.SaveDirectory = SaveDirectory;
        config.BrowserType = BrowserType;
        config.ScheduledDate = ScheduledDate;
        config.ScheduledStartHour = ScheduledStartHour;
        config.MessageBlockWords = MessageBlockWords
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _configService.Save();
    }
}
