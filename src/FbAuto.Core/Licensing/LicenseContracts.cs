namespace FbAuto.Core.Licensing;

/// <summary>
/// 授权状态。区分「未激活」与「服务不可达」，因为前者要输卡密，后者不该把人挡在门外。
/// </summary>
public enum LicenseState
{
    Unknown = 0,
    NotActivated = 1,
    Active = 2,
    Expired = 3,
    DeviceMismatch = 4,
    ServiceUnavailable = 5,
}

/// <summary>
/// 一次激活或校验的结果。
/// </summary>
/// <param name="State">结果状态。</param>
/// <param name="Message">给用户看的中文提示，直接显示在激活窗错误区。</param>
/// <param name="ExpiresAt">租约到期时间，仅 <see cref="LicenseState.Active"/> 时有值。</param>
public sealed record LicenseResult(LicenseState State, string Message, DateTime? ExpiresAt = null)
{
    public bool IsActive => State == LicenseState.Active;

    public static LicenseResult NotActivated(string message) => new(LicenseState.NotActivated, message);

    public static LicenseResult Active(DateTime? expiresAt) =>
        new(LicenseState.Active, "激活成功", expiresAt);

    public static LicenseResult Unavailable(string message) => new(LicenseState.ServiceUnavailable, message);
}

/// <summary>
/// 客户端授权服务。S1 只提供占位实现（S3 接入在线激活与心跳续租）。
/// </summary>
public interface ILicenseService
{
    /// <summary>当前已知状态，启动时由本地缓存决定。</summary>
    LicenseState State { get; }

    /// <summary>租约到期时间，未激活为 null。</summary>
    DateTime? ExpiresAt { get; }

    /// <summary>本地是否已有卡密（原版启动即自动尝试激活，失败才弹窗）。</summary>
    bool HasLocalLicenseKey { get; }

    /// <summary>尝试激活；不改动 UI 线程模型，调用方负责回到 UI 线程。</summary>
    Task<LicenseResult> ActivateAsync(string licenseKey, CancellationToken cancellationToken = default);
}
