using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using FbAuto.Application.Dialogs;
using FbAuto.Presentation.ViewModels;

namespace FbAuto.Presentation.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        // 界面建好之后数一遍「选择...」按钮。WPF 的绑定失败是静默的：
        // 按钮照样显示、点下去什么都不发生。文本守卫只能证明 XAML 里写了，
        // 这里证明的是**运行时真的连上了**，而且每次启动都会留一行日志。
        Loaded += (_, _) =>
        {
            var (total, wired) = CountPickerButtons();
            _viewModel.VerifyPickerWiring(total, wired);
        };
    }

    /// <summary>统计界面上「选择...」按钮的总数与其中拿到命令的数量。</summary>
    private (int Total, int Wired) CountPickerButtons()
    {
        var found = new HashSet<Button>();
        var visited = new HashSet<DependencyObject>();

        Collect(this);

        return (found.Count, found.Count(b => b.Command is not null));

        void Collect(DependencyObject node)
        {
            // 逻辑树与可视化树会大量重叠，不记录访问过的话同一棵子树会被反复遍历。
            if (!visited.Add(node))
            {
                return;
            }

            if (node is Button button && button.Content as string == "选择...")
            {
                found.Add(button);
            }

            // 逻辑树里包含 RowDefinition / ColumnDefinition 这类**不是 Visual** 的东西，
            // 直接递给 VisualTreeHelper 会抛「不是 Visual 或 Visual3D」——
            // 这不是理论风险，第一版就撞上了，界面线程未处理异常弹窗。
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                Collect(child);
            }

            if (node is not Visual and not Visual3D)
            {
                return;
            }

            var count = VisualTreeHelper.GetChildrenCount(node);

            for (var i = 0; i < count; i++)
            {
                Collect(VisualTreeHelper.GetChild(node, i));
            }
        }
    }

    /// <summary>
    /// 关窗时做两件事：把界面上的配置写回磁盘；收掉「打开浏览器」开出去的实例。
    ///
    /// 收尾用 Task.Run + 限时等待，而不是直接 .Wait()：
    /// CloseAsync 内部有 await，直接在 UI 线程等待会把 UI 上下文锁死（典型死锁姿势）。
    /// 限时 3 秒，超时就放弃——关不掉的浏览器进程比卡住的程序危害小。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.PersistConfig();

        try
        {
            Task.Run(() => _viewModel.ShutdownAsync()).Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // 收尾失败不影响退出。
        }

        base.OnClosing(e);
    }
}
