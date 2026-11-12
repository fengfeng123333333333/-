namespace FbAuto.Core.Licensing;

/// <summary>
/// 授权状态。保留 <see cref="ServiceUnavailable"/> 是为了将来若改回在线校验时能复用，
/// 离线授权模式下不会产生该状态。
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
/// <param name="ExpiresAt">到期时间，仅 <see cref="LicenseState.Active"/> 时有值。</param>
public sealed record LicenseResult(LicenseState State, string Message, DateTime? ExpiresAt = null)
{
    public bool IsActive => State == LicenseState.Active;

    public static LicenseResult NotActivated(string message) => new(LicenseState.NotActivated, message);

    public static LicenseResult Active(DateTime? expiresAt) =>
        new(LicenseState.Active, "激活成功", expiresAt);

    public static LicenseResult Expired(string message, DateTime? expiresAt = null) =>
        new(LicenseState.Expired, message, expiresAt);

    public static LicenseResult Unavailable(string message) => new(LicenseState.ServiceUnavailable, message);
}

/// <summary>
/// 客户端授权服务。
///
/// S3 起采用**离线授权码**：不联网、不心跳、不需要服务器。
/// 发码方持有私钥批量签发，客户端内置公钥验签，激活后把到期信息加密落在本机。
/// 随之而来的两条硬限制（无法通过写代码绕过）：
/// ① 无法远程吊销，只能靠有效期到期自然失效；
/// ② 用户删掉本地凭证文件可以重新激活一次，等于续命一个周期。
/// </summary>
public interface ILicenseService
{
    /// <summary>当前状态，构造时由本地凭证决定。</summary>
    LicenseState State { get; }

    /// <summary>到期时间（本地时区表示），未激活为 null。</summary>
    DateTime? ExpiresAt { get; }

    /// <summary>本地是否已有授权凭证。</summary>
    bool HasLocalLicenseKey { get; }

    /// <summary>剩余天数，未激活为 null，已过期为 0。</summary>
    int? DaysRemaining { get; }

    /// <summary>尝试用授权码激活；不改动 UI 线程模型，调用方负责回到 UI 线程。</summary>
    Task<LicenseResult> ActivateAsync(string licenseCode, CancellationToken cancellationToken = default);
}
