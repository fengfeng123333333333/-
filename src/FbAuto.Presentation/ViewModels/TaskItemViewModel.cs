using CommunityToolkit.Mvvm.ComponentModel;
using FbAuto.Core.Models;

namespace FbAuto.Presentation.ViewModels;

/// <summary>
/// 队列中的一行。包一层 ViewModel 是因为状态与结果会随执行变化，而 <see cref="AutomatedTask"/> 是纯模型。
/// </summary>
public sealed partial class TaskItemViewModel : ObservableObject
{
    public TaskItemViewModel(AutomatedTask model)
    {
        Model = model;
        _status = model.Status;
        _browserName = model.BrowserName;
        _result = model.Result;
    }

    public AutomatedTask Model { get; }

    public Guid Id => Model.Id;

    /// <summary>任务列显示名，如「[视频上传] - 谷歌版」。</summary>
    public string DisplayName => Model.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TaskRunStatus _status;

    [ObservableProperty]
    private string _browserName;

    [ObservableProperty]
    private string _result;

    public string StatusText => Status.ToDisplay();

    /// <summary>状态色：待执行灰、执行中青、成功绿、失败红、暂停/中断黄。</summary>
    public string StatusColor => Status switch
    {
        TaskRunStatus.Running => "#0891B2",
        TaskRunStatus.Success => "#16A34A",
        TaskRunStatus.Failed => "#DC2626",
        TaskRunStatus.Paused => "#D97706",
        TaskRunStatus.Interrupted => "#D97706",
        _ => "#8A939E",
    };

    public void ApplyToModel()
    {
        Model.Status = Status;
        Model.BrowserName = BrowserName;
        Model.Result = Result;
    }
}
