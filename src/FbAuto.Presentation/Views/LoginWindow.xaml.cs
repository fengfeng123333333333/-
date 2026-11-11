using System.Windows;
using FbAuto.Presentation.ViewModels;

namespace FbAuto.Presentation.Views;

public partial class LoginWindow : Window
{
    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ActivationSucceeded += (_, _) => ActivationSucceeded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>激活成功，由 App 接管切换到主界面。</summary>
    public event EventHandler? ActivationSucceeded;
}
