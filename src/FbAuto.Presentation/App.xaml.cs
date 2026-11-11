using System.IO;
using System.Windows;
using System.Windows.Threading;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Licensing;
using FbAuto.Infrastructure.Configuration;
using FbAuto.Infrastructure.Licensing;
using FbAuto.Infrastructure.Logging;
using FbAuto.Infrastructure.System;
using FbAuto.Presentation.ViewModels;
using FbAuto.Presentation.Views;

namespace FbAuto.Presentation;

/// <summary>
/// 组合根。S1 不引 DI 容器，依赖手工装配（对象图很短，多一个容器就多一份看不懂）。
/// 注意：基类要写全名，因为引入了 FbAuto.Application 命名空间后，裸写 Application 会解析成那个命名空间。
/// </summary>
public partial class App : System.Windows.Application
{
    private IAppLogger? _logger;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var baseDirectory = AppContext.BaseDirectory;

        // 日志必须最早落盘。原版缺运行时时静默退出、一行日志都没有，排障只能靠猜。
        var logger = new FileAppLogger(Path.Combine(baseDirectory, "Logs"));
        _logger = logger;
        logger.Info($"应用程序启动：v{typeof(App).Assembly.GetName().Version}，机器 {Environment.MachineName}");
        logger.Info($"程序目录：{baseDirectory}");
        logger.Info($"日志文件：{logger.LogFilePath}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Error("未处理的后台异常", args.ExceptionObject as Exception);

        var configService = new JsonConfigService(baseDirectory, logger);
        configService.Load();

        var machineCodeProvider = new MachineCodeProvider();
        var licenseService = new PendingLicenseService(baseDirectory, logger);
        logger.Info($"机器码：{machineCodeProvider.GetMachineCode()}");

        // 原版语义：启动先读本地卡密并自动尝试激活，失败才停在激活窗。
        if (configService.Current.SkipActivationInDevelopment)
        {
            logger.Warn("开发模式：跳过激活直接进入主界面（SkipActivationInDevelopment=true，发布前必须置 false）");
            ShowMainWindow(configService, logger);
            return;
        }

        ShowLoginWindow(configService, machineCodeProvider, licenseService, logger);
    }

    private void ShowMainWindow(IConfigService configService, IAppLogger logger)
    {
        var window = new MainWindow(new MainViewModel(configService, logger));
        MainWindow = window;
        window.Show();
    }

    private void ShowLoginWindow(
        IConfigService configService,
        IMachineCodeProvider machineCodeProvider,
        ILicenseService licenseService,
        IAppLogger logger)
    {
        var login = new LoginWindow(new LoginViewModel(machineCodeProvider, licenseService, logger));
        login.ActivationSucceeded += (_, _) =>
        {
            login.Close();
            ShowMainWindow(configService, logger);
        };
        MainWindow = login;
        login.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.Error("界面线程未处理异常", e.Exception);
        MessageBox.Show(
            $"程序遇到错误，已记录日志：{Environment.NewLine}{e.Exception.Message}",
            "错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
