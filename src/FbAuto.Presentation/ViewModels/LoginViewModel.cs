using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Licensing;

namespace FbAuto.Presentation.ViewModels;

/// <summary>
/// 激活窗 ViewModel。离线授权：输入授权码后本机验签，通过即写入加密凭证，不联网。
/// </summary>
public sealed partial class LoginViewModel : ObservableObject
{
    private readonly ILicenseService _license;
    private readonly IAppLogger _logger;

    public LoginViewModel(IMachineCodeProvider machineCodeProvider, ILicenseService license, IAppLogger logger)
    {
        _logger = logger;
        _license = license;
        MachineCode = machineCodeProvider.GetMachineCode();

        // 过期后来到激活窗，必须说清为什么——否则用户以为程序抽风了
        if (_license.State == LicenseState.Expired && _license.ExpiresAt is DateTime expiredAt)
        {
            ErrorMessage = $"授权已于 {expiredAt:yyyy-MM-dd} 到期，请输入新的授权码";
        }
    }

    /// <summary>激活成功事件，由窗口订阅后打开主界面。</summary>
    public event EventHandler? ActivationSucceeded;

    /// <summary>本机机器码，8-4-4-4-12 形式，与原版一致。</summary>
    public string MachineCode { get; }

    /// <summary>本地已有卡密时显示提示行，对应原版「已自动读取本地卡密」。</summary>
    public bool HasLocalLicenseKey => _license.HasLocalLicenseKey;

    [ObservableProperty]
    private string _licenseKey = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    private async Task ActivateAsync()
    {
        if (string.IsNullOrWhiteSpace(LicenseKey))
        {
            ErrorMessage = "请输入卡密";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await _license.ActivateAsync(LicenseKey.Trim()).ConfigureAwait(true);
            if (result.IsActive)
            {
                _logger.Info("激活成功");
                ActivationSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = result.Message;
                _logger.Warn($"激活失败：{result.State} · {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("激活过程异常", ex);
            ErrorMessage = $"激活失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CopyMachineCode()
    {
        try
        {
            Clipboard.SetText(MachineCode);
            _logger.Info("机器码已复制到剪贴板");
        }
        catch (Exception ex)
        {
            _logger.Error("复制机器码失败", ex);
        }
    }
}
