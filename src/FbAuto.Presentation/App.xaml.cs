using System.IO;
using System.Windows;
using System.Windows.Threading;
using FbAuto.Application.Execution;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Licensing;
using FbAuto.Core.Models;
using FbAuto.Infrastructure.Accounts;
using FbAuto.Infrastructure.Configuration;
using FbAuto.Infrastructure.Logging;
using FbAuto.Infrastructure.System;
using FbAuto.Licensing;
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
        var machineCode = machineCodeProvider.GetMachineCode();
        logger.Info($"机器码：{machineCode}");

        // 离线授权：凭证用机器码派生的密钥加密，文件被拷到别的机器上解不开
        var licenseStore = new LicenseStore(
            Path.Combine(baseDirectory, "license.dat"), machineCode, logger);
        var licenseService = new OfflineLicenseService(licenseStore, logger);

        if (configService.Current.SkipActivationInDevelopment)
        {
            logger.Warn("开发模式：跳过激活直接进入主界面（SkipActivationInDevelopment=true，发布前必须置 false）");
            ShowMainWindow(configService, logger);
            return;
        }

        // 已激活且未到期就直接进主界面，不必每次开机重新输码
        if (licenseService.State == LicenseState.Active)
        {
            logger.Info($"本地授权有效，剩余 {licenseService.DaysRemaining} 天，直接进入主界面");
            licenseService.TouchLastSeen();
            ShowMainWindow(configService, logger);
            return;
        }

        if (licenseService.State == LicenseState.Expired)
        {
            logger.Warn($"本地授权已到期（{licenseService.ExpiresAt:yyyy-MM-dd}），需要新的授权码");
        }

        ShowLoginWindow(configService, machineCodeProvider, licenseService, logger);
    }

    private void ShowMainWindow(IConfigService configService, IAppLogger logger)
    {
        var window = new MainWindow(CreateMainViewModel(configService, logger));
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// 主界面的依赖装配。执行链路的三件套（执行器表 / 通道提供者 / 账号清单）在这里落地，
    /// 界面上只见到 <see cref="TaskQueueRunner"/>，不直接碰浏览器实现。
    /// </summary>
    private static MainViewModel CreateMainViewModel(IConfigService configService, IAppLogger logger)
    {
        var config = configService.Current;

        var accountsFile = string.IsNullOrWhiteSpace(config.AccountsFile)
            ? Path.Combine(AppContext.BaseDirectory, "accounts.csv")
            : config.AccountsFile;

        var accounts = new AccountListCsv(accountsFile, logger);
        var channels = new ConfigBrowserChannelProvider(logger, config);

        // S4 阶段五类任务都还没接页面动作，一律注册「未接入」执行器：
        // 明确记失败并说明原因，而不是假装成功或悄悄跳过。
        var executors = new TaskExecutorRegistry(
            TaskTypeCatalog.All.Select(t => new NotWiredTaskExecutor(t.Kind)));

        var runner = new TaskQueueRunner(executors, channels, accounts, logger);

        return new MainViewModel(configService, logger, runner, accounts, channels);
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
            // 先开主窗口，再关激活窗。
            // 反过来的话，激活窗一关就没有任何窗口了，默认 ShutdownMode.OnLastWindowClose
            // 会让整个进程退出，主窗口根本没机会出现——现象就是「激活成功后程序直接消失」。
            ShowMainWindow(configService, logger);
            login.Close();
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
