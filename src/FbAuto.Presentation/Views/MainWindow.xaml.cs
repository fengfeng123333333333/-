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
    /// 关窗时把界面上的配置写回磁盘，对应验收：改 config.json 重启后生效（反向路径也要成立）。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.PersistConfig();
        base.OnClosing(e);
    }
}
