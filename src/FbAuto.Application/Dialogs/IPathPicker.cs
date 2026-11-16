namespace FbAuto.Application.Dialogs;

/// <summary>要弹的对话框种类。目录和文件在 Windows 上走的是两套原生对话框，不能混用。</summary>
public enum PathPickerKind
{
    /// <summary>选一个文件夹（视频目录、图片目录、保存目录…）。</summary>
    Folder,

    /// <summary>选一个文件（描述 txt、名字 txt、账号 csv、Chrome 的 exe…）。</summary>
    File,
}

/// <summary>
/// 一次「选择...」请求的描述。
/// </summary>
/// <remarks>
/// 放在 Application 层而不是界面层，是为了让「哪个按钮该选目录、哪个该选文件、文件是什么扩展名」
/// 这件事可以被单测钉住：测试项目刻意不引用 WPF 层（会引入 STA 与桌面依赖），
/// 放界面层就只能靠肉眼看 XAML。
///
/// 这类错误的表现很隐蔽：把描述文件选成了目录，界面照样显示一串路径，
/// 直到真跑任务才报「读不到描述文件」。所以种类必须是数据，不能只写在 XAML 里。
/// </remarks>
public sealed record PathPickerRequest
{
    public required PathPickerKind Kind { get; init; }

    /// <summary>对话框标题。要写清在选什么，别只写「打开」。</summary>
    public required string Title { get; init; }

    /// <summary>
    /// 文件筛选器，形如 <c>文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*</c>。
    /// 选目录时忽略。
    /// </summary>
    public string Filter { get; init; } = string.Empty;

    /// <summary>常用筛选器：只要 txt。</summary>
    public const string TextFilter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";

    /// <summary>常用筛选器：只要 exe。</summary>
    public const string ExecutableFilter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";

    /// <summary>常用筛选器：只要 csv。</summary>
    public const string CsvFilter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";
}

/// <summary>
/// 弹系统对话框选路径。界面层提供实现，<c>MainViewModel</c> 只依赖这个接口。
/// </summary>
/// <remarks>
/// 取消必须返回 <c>null</c>，不能返回空串 —— 调用方靠 null 判断「这次不算数，保留原值」。
/// 这两者一旦混同，使用者点了取消就会把已经填好的路径清空，而且没有任何提示。
/// </remarks>
public interface IPathPicker
{
    /// <summary>
    /// 按 <paramref name="request"/> 的种类弹对应对话框，返回选中的绝对路径；取消返回 <c>null</c>。
    /// </summary>
    /// <param name="request">选什么。</param>
    /// <param name="initialPath">打开时的起始位置（通常是该输入框现有的值）；为空则用系统默认。</param>
    string? Pick(PathPickerRequest request, string? initialPath);
}
