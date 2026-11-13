using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbAuto.Application.Execution;
using FbAuto.Browser.Abstractions;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;

namespace FbAuto.Presentation.ViewModels;

/// <summary>
/// 主界面 ViewModel。S4 起「开始任务」不再是日志占位，而是真的驱动
/// <see cref="TaskQueueRunner"/> 跑队列；状态由调度器回调、这里 marshal 回 UI 线程。
///
/// 有意保留的边界：本类不判断「能不能跑」（那是调度器的事），只管按钮可用性与显示。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>浏览器列表加载完成前的占位文案，与原版一致。</summary>
    public const string BrowserLoadingPlaceholder = "正在加载列表...";

    private readonly IConfigService _configService;
    private readonly IAppLogger _logger;
    private readonly TaskQueueRunner _runner;
    private readonly IAccountDirectory _accounts;
    private readonly IBrowserChannelProvider _channels;

    /// <summary>「打开浏览器」开出去、还没关的会话。窗口关闭时统一收掉，避免残留进程。</summary>
    private readonly List<(IBrowserChannel Channel, BrowserSession Session)> _openedBrowsers = [];

    private CancellationTokenSource? _runCts;

    public MainViewModel(
        IConfigService configService,
        IAppLogger logger,
        TaskQueueRunner runner,
        IAccountDirectory accounts,
        IBrowserChannelProvider channels)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));

        var config = configService.Current;
        _concurrentCount = config.ConcurrentCount;
        _browserOpenIntervalSeconds = config.BrowserOpenIntervalSeconds;
        _videoDirectory = config.VideoDirectory;
        _descriptionFile = config.DescriptionFile;
        _saveDirectory = config.SaveDirectory;
        _browserType = config.BrowserType;
        _scheduledDate = config.ScheduledDate;
        _scheduledStartHour = config.ScheduledStartHour;
        _bitVideoDirectory = config.BitVideoDirectory;
        _bitDescriptionFile = config.BitDescriptionFile;
        _imageDirectory = config.ImageDirectory;
        _bitImageDirectory = config.BitImageDirectory;
        _signatureFile = config.SignatureFile;
        _bitSignatureFile = config.BitSignatureFile;
        _namesFile = config.NamesFile;
        _bitNamesFile = config.BitNamesFile;
        _pageNamePassword = config.PageNamePassword;
        _bitPageNamePassword = config.BitPageNamePassword;
        _commentsBlockWords = string.Join(",", config.CommentsBlockWords);
        _deleteMessageLimit = config.DeleteMessageLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _deleteMessageCookieRootPath = config.DeleteMessageCookieRootPath;

        LoadBrowsers();

        Tasks.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTasks));
            OnPropertyChanged(nameof(TaskSummary));
            NotifyCommandStates();
        };

        _runner.StatusChanged += OnRunnerStatusChanged;
    }

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    public ObservableCollection<string> Browsers { get; } = [];

    public IReadOnlyList<TaskTypeDescriptor> TaskTypes => TaskTypeCatalog.All;

    public IReadOnlyList<string> BrowserTypes { get; } = ["谷歌版", "比特版"];

    [ObservableProperty]
    private TaskItemViewModel? _selectedTask;

    [ObservableProperty]
    private string? _selectedBrowser;

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

    /// <summary>比特版的视频目录（原版键名 BitVideoPath，与谷歌版是两套）。</summary>
    [ObservableProperty]
    private string _bitVideoDirectory;

    /// <summary>比特版的描述文件（原版键名 BitDescriptionFilePath）。</summary>
    [ObservableProperty]
    private string _bitDescriptionFile;

    /// <summary>
    /// 头像与背景图共用的图片目录（谷歌版）。
    /// </summary>
    /// <remarks>
    /// **一个目录，不是两个**。早先界面按「头像 / 背景图」摆了俩输入框，
    /// 但原版提示是「头像和背景图均从该目录随机选取」+「同一账号内使用同一张随机图片」，
    /// 拆成两个反而会让人以为可以配两套图。S11 合成一个。
    /// </remarks>
    [ObservableProperty]
    private string _imageDirectory;

    /// <summary>比特版的图片目录（与谷歌版成对，互不覆盖）。</summary>
    [ObservableProperty]
    private string _bitImageDirectory;

    /// <summary>签名文件（谷歌版）。整段 txt 作为一段个性签名。</summary>
    [ObservableProperty]
    private string _signatureFile;

    /// <summary>签名文件（比特版）。</summary>
    [ObservableProperty]
    private string _bitSignatureFile;

    /// <summary>主页名称文件（谷歌版）。一行一个名字，按别名挑一行。</summary>
    [ObservableProperty]
    private string _namesFile;

    /// <summary>主页名称文件（比特版）。</summary>
    [ObservableProperty]
    private string _bitNamesFile;

    /// <summary>改主页名字时输的主账号密码（谷歌版）。留空则名字多半改不动。</summary>
    [ObservableProperty]
    private string _pageNamePassword;

    /// <summary>改主页名字时输的主账号密码（比特版）。</summary>
    [ObservableProperty]
    private string _bitPageNamePassword;

    /// <summary>评论屏蔽词（不是「删除消息」的屏蔽词 —— 原版键名是 CommentsBlockText）。</summary>
    [ObservableProperty]
    private string _commentsBlockWords;

    /// <summary>每个账号最多删除多少条消息。0 表示不限。</summary>
    [ObservableProperty]
    private string _deleteMessageLimit;

    /// <summary>删除消息专用 Cookie 根目录（当前版本只记录，不注入）。</summary>
    [ObservableProperty]
    private string _deleteMessageCookieRootPath;

    [ObservableProperty]
    private bool _isRunning;

    /// <summary>界面顶部「浏览器类型」下拉对应的通道。</summary>
    /// <remarks>
    /// 只有「[删除消息]」这类跟随全局的任务会用到它。文案是原版下拉项原文，别改字。
    /// </remarks>
    public BrowserChannelKind GlobalChannel =>
        string.Equals(BrowserType, "比特版", StringComparison.Ordinal)
            ? BrowserChannelKind.Bit
            : BrowserChannelKind.Google;

    /// <summary>队列空态判断：空时显示「暂无任务，请从左侧添加」。</summary>
    public bool HasTasks => Tasks.Count > 0;

    /// <summary>浏览器下拉是否已可用；占位状态下禁用，避免点了没反应。</summary>
    public bool IsBrowserListReady => Browsers.Count > 0 && Browsers[0] != BrowserLoadingPlaceholder;

    public bool CanStart => !IsRunning && Tasks.Any(t => t.Status == TaskRunStatus.Pending);

    public bool CanStop => IsRunning;

    public string TaskSummary => IsRunning
        ? $"执行中 · 待执行 {Tasks.Count(t => t.Status == TaskRunStatus.Pending)} 条"
        : Tasks.Count == 0
            ? "队列为空"
            : $"共 {Tasks.Count} 条 · 待执行 {Tasks.Count(t => t.Status == TaskRunStatus.Pending)} 条";

    /// <summary>
    /// 从账号清单填顶栏浏览器下拉。没有可用账号时保持占位文案并禁用，
    /// 而不是显示一个空下拉让人以为程序坏了。
    /// </summary>
    private void LoadBrowsers()
    {
        Browsers.Clear();

        foreach (var account in _accounts.All)
        {
            Browsers.Add(account.BrowserDisplayName);
        }

        if (Browsers.Count == 0)
        {
            Browsers.Add(BrowserLoadingPlaceholder);
        }
        else
        {
            _logger.Info($"浏览器列表已加载：{Browsers.Count} 个");
        }

        SelectedBrowser = Browsers[0];
        OnPropertyChanged(nameof(IsBrowserListReady));
    }

    [RelayCommand]
    private void AddTask(TaskTypeDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        // 添加时就绑定账号：原版队列的「浏览器」列在建任务时就显示了，不是执行时才分配。
        // 通道不能直接用 descriptor.Channel：「[删除消息]」只有一条，跟随顶部「浏览器类型」下拉。
        var channel = TaskTypeCatalog.ChannelFor(descriptor, GlobalChannel);
        var account = _accounts.NextAvailable(channel);

        var task = new AutomatedTask
        {
            Kind = descriptor.Kind,
            Channel = channel,
            AccountAlias = account?.Alias ?? string.Empty,
            BrowserName = account?.BrowserDisplayName ?? "未分配",
        };

        var item = new TaskItemViewModel(task) { Result = "—" };

        Tasks.Add(item);
        SelectedTask = item;

        _logger.Info(account is null
            ? $"添加任务：{descriptor.DisplayName}（账号清单里没有 {channel.ToDisplay()} 可用账号，执行时会失败）"
            : $"添加任务：{descriptor.DisplayName} → {account.BrowserDisplayName}");
    }

    [RelayCommand]
    private void RemoveTask()
    {
        if (SelectedTask is null)
        {
            _logger.Warn("删除任务被忽略：未选中任何任务");
            return;
        }

        if (SelectedTask.Status == TaskRunStatus.Running)
        {
            _logger.Warn("删除任务被忽略：该任务正在执行，请先停止");
            return;
        }

        var removed = SelectedTask;
        Tasks.Remove(removed);
        SelectedTask = Tasks.FirstOrDefault();
        _logger.Info($"删除任务：{removed.DisplayName}");

        // 模型对象也一并丢弃，避免调度器在下一轮里还拿着它。
        removed.Model.Status = TaskRunStatus.Interrupted;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartTasksAsync()
    {
        if (!Tasks.Any(t => t.Status == TaskRunStatus.Pending))
        {
            _logger.Warn("开始任务被忽略：没有待执行的任务（已完成的不重跑，V1 不做自动重试）");
            return;
        }

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        SetRunning(true);

        try
        {
            var options = new TaskRunOptions
            {
                ConcurrentCount = Math.Max(1, ConcurrentCount),
                LaunchIntervalSeconds = Math.Max(0, BrowserOpenIntervalSeconds),
                ScreenshotDirectory = ResolveScreenshotDirectory(),
            };

            var models = Tasks.Select(t => t.Model).ToList();
            var summary = await _runner.RunAsync(models, options, _runCts.Token);

            _logger.Info(
                $"本轮结束：成功 {summary.Succeeded}，失败 {summary.Failed}，中断 {summary.Interrupted}");
        }
        catch (Exception ex)
        {
            // 调度器内部已兜住绝大多数异常；这里兜住的是「它自己坏了」的情况。
            _logger.Error("执行链路异常", ex);
        }
        finally
        {
            SetRunning(false);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopTasks()
    {
        if (_runCts is null || !IsRunning)
        {
            return;
        }

        _logger.Info("已请求停止：正在执行的任务会被中断，未开始的标记为中断");
        _runCts.Cancel();
    }

    /// <summary>
    /// 顶栏「打开浏览器」。按选中的浏览器（账号）真实启一次，用于确认 profile 与通道配置是否正确。
    /// 打开的实例在这里记账，窗口关闭时统一关闭——不关就会留下孤儿进程。
    /// </summary>
    [RelayCommand]
    private async Task OpenBrowserAsync()
    {
        var account = _accounts.All.FirstOrDefault(a =>
            string.Equals(a.BrowserDisplayName, SelectedBrowser, StringComparison.Ordinal));

        if (account is null)
        {
            _logger.Warn($"打开浏览器被忽略：账号清单里没有「{SelectedBrowser}」，请先填写 {_accounts.SourcePath}");
            return;
        }

        try
        {
            var channel = _channels.Create(account.Channel);
            var session = await channel.LaunchAsync(new BrowserLaunchRequest
            {
                ProfileDirectory = account.ProfileDirectory,
                BitProfileId = string.IsNullOrWhiteSpace(account.BitProfileId) ? null : account.BitProfileId,
                Proxy = string.IsNullOrWhiteSpace(account.Proxy) ? null : account.Proxy,
            });

            _openedBrowsers.Add((channel, session));
            _logger.Info($"已打开浏览器：{account.BrowserDisplayName}，PID {session.ProcessId?.ToString() ?? "未知"}");
        }
        catch (BrowserChannelException ex)
        {
            _logger.Warn($"打开浏览器失败：{ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error($"打开浏览器异常（{ex.GetType().Name}）", ex);
        }
    }

    /// <summary>窗口关闭前调用：收掉自己开出去的浏览器。</summary>
    public async Task ShutdownAsync()
    {
        // 先停执行，再关浏览器。顺序反了会让执行中的任务在关闭后继续跑。
        _runCts?.Cancel();

        foreach (var (channel, session) in _openedBrowsers)
        {
            try
            {
                await channel.CloseAsync(session, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Warn($"关闭浏览器失败（{ex.Message}），可能残留 PID {session.ProcessId}");
            }
        }

        _openedBrowsers.Clear();
    }

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
        config.BitVideoDirectory = BitVideoDirectory ?? string.Empty;
        config.BitDescriptionFile = BitDescriptionFile ?? string.Empty;
        config.ImageDirectory = ImageDirectory ?? string.Empty;
        config.BitImageDirectory = BitImageDirectory ?? string.Empty;
        config.SignatureFile = SignatureFile ?? string.Empty;
        config.BitSignatureFile = BitSignatureFile ?? string.Empty;
        config.NamesFile = NamesFile ?? string.Empty;
        config.BitNamesFile = BitNamesFile ?? string.Empty;
        config.PageNamePassword = PageNamePassword ?? string.Empty;
        config.BitPageNamePassword = BitPageNamePassword ?? string.Empty;

        config.CommentsBlockWords = CommentsBlockWords
            .Split([',', '，'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (int.TryParse(DeleteMessageLimit, out var limit))
        {
            // 负数按「不限」处理，与规划的语义一致；不在这里拦成 0 —— 拦了等于偷偷改了使用者的意图。
            config.DeleteMessageLimit = limit < 0 ? 0 : limit;
        }

        config.DeleteMessageCookieRootPath = DeleteMessageCookieRootPath ?? string.Empty;

        _configService.Save();
    }

    /// <summary>
    /// 截图目录：优先用配置的保存目录，未配置时落到程序目录下的 Screenshots。
    /// 不能让「没配保存目录」变成「失败时没有截图」——失败证据比整洁重要。
    /// </summary>
    private string ResolveScreenshotDirectory()
    {
        if (!string.IsNullOrWhiteSpace(SaveDirectory))
        {
            return Path.Combine(SaveDirectory, "Screenshots");
        }

        return Path.Combine(AppContext.BaseDirectory, "Screenshots");
    }

    private void SetRunning(bool running)
    {
        IsRunning = running;
        OnPropertyChanged(nameof(TaskSummary));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        StartTasksCommand.NotifyCanExecuteChanged();
        StopTasksCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 调度器在后台线程发事件。这里负责切回 UI 线程——直接改绑定集合会抛
    /// 「该类型的 CollectionView 不支持从不同于 Dispatcher 线程的线程更改」。
    /// </summary>
    private void OnRunnerStatusChanged(object? sender, TaskStatusChangedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyStatus(e);
        }
        else
        {
            dispatcher.BeginInvoke(() => ApplyStatus(e));
        }
    }

    private void ApplyStatus(TaskStatusChangedEventArgs e)
    {
        var item = Tasks.FirstOrDefault(t => t.Id == e.TaskId);
        if (item is null)
        {
            return;
        }

        item.SyncFromModel();

        OnPropertyChanged(nameof(TaskSummary));
        NotifyCommandStates();
    }
}
