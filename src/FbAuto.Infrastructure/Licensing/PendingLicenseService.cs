using FbAuto.Core.Abstractions;
using FbAuto.Core.Licensing;

namespace FbAuto.Infrastructure.Licensing;

/// <summary>
/// 授权服务占位实现。S1 只保证：没有租约时窗口不崩、提示可读。
/// S3 换成真实实现：POST /activate 拿签名租约，每 6 小时 /heartbeat 续期，连续 72 小时失败才降只读。
/// </summary>
public sealed class PendingLicenseService : ILicenseService
{
    private readonly IAppLogger _logger;
    private readonly string _licenseFilePath;

    public PendingLicenseService(string baseDirectory, IAppLogger logger)
    {
        _logger = logger;
        _licenseFilePath = Path.Combine(baseDirectory, "license.dat");
    }

    public LicenseState State => LicenseState.NotActivated;

    public DateTime? ExpiresAt => null;

    /// <summary>
    /// 原版启动即读取本地卡密并自动尝试激活，失败才停在激活窗。此处同语义：只判断文件在不在。
    /// </summary>
    public bool HasLocalLicenseKey => File.Exists(_licenseFilePath);

    public Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default)
    {
        _logger.Warn("激活请求被拒：授权服务尚未接入（S3 阶段实现）");
        return Task.FromResult(
            LicenseResult.Unavailable("授权服务尚未接入，当前版本无法激活（S3 阶段实现）"));
    }
}
