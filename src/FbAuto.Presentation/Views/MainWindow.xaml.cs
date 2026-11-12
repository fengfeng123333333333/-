using System.ComponentModel;
using System.Windows;
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
