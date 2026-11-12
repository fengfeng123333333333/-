using FbAuto.Core.Abstractions;
using FbAuto.Core.Licensing;

namespace FbAuto.Licensing;

/// <summary>
/// 离线授权服务：验签授权码、把到期信息加密存本机、启动时判断是否仍有效。
/// 全程不联网，因此没有心跳、没有宽限期——到期就是到期。
/// </summary>
public sealed class OfflineLicenseService : ILicenseService
{
    /// <summary>时间回拨超过这个幅度就记一条可疑日志（不阻断使用）。</summary>
    private static readonly TimeSpan ClockRollbackWarnThreshold = TimeSpan.FromHours(3);

    private readonly LicenseStore _store;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;

    private LicenseCredential? _credential;
    private LicenseCode? _code;

    public OfflineLicenseService(LicenseStore store, IAppLogger logger, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;

        Evaluate();
    }

    public LicenseState State { get; private set; } = LicenseState.NotActivated;

    public DateTime? ExpiresAt { get; private set; }

    public bool HasLocalLicenseKey => _credential is not null;

    public int? DaysRemaining { get; private set; }

    /// <summary>激活。参数是授权码原文（可带连字符与空白，解码时会忽略）。</summary>
    public Task<LicenseResult> ActivateAsync(string licenseCode, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(licenseCode))
        {
            return Task.FromResult(LicenseResult.NotActivated("请先输入或粘贴授权码"));
        }

        // 一、结构解析：字符、长度、版本
        LicenseCode code;
        try
        {
            code = LicenseCodeCodec.Decode(licenseCode);
        }
        catch (FormatException ex)
        {
            _logger.Warn($"激活失败（格式）：{ex.Message}");
            return Task.FromResult(LicenseResult.NotActivated(ex.Message));
        }

        // 二、验签：证明这张码确实由我们的私钥签发，且一个字节都没被改过
        bool verified;
        try
        {
            verified = LicenseVerifier.VerifyWithEmbeddedKey(code);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            _logger.Error("激活失败：客户端内置公钥不可用", ex);
            return Task.FromResult(
                LicenseResult.Unavailable("客户端授权组件异常（内置公钥不可用），请联系发码方"));
        }

        if (!verified)
        {
            _logger.Warn($"激活失败（验签不过）：{code}");
            return Task.FromResult(LicenseResult.NotActivated(
                "授权码校验失败：这不是有效的授权码，或内容被改动过。请整段重新复制。"));
        }

        // 三、落地。同一张码重复激活不重置首次激活时间，否则重装一次就能白续一个周期
        var nowUtc = _time.GetUtcNow().UtcDateTime;
        var normalized = LicenseCodeCodec.Encode(code);

        var firstActivated = nowUtc;
        if (_credential is not null)
        {
            var sameCode = string.Equals(
                Normalize(_credential.CodeText), normalized, StringComparison.Ordinal);
            firstActivated = sameCode ? _credential.FirstActivatedUtc : nowUtc;

            if (!sameCode)
            {
                _logger.Info("检测到更换授权码，有效期从本次激活重新计算");
            }
        }

        var credential = new LicenseCredential(normalized, firstActivated, nowUtc);

        try
        {
            _store.Save(credential);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("授权凭证写入失败", ex);
            return Task.FromResult(LicenseResult.Unavailable(
                $"激活信息无法保存到本机（{ex.Message}）。请检查程序目录权限后重试。"));
        }

        _credential = credential;
        _code = code;
        Evaluate();

        _logger.Info($"激活成功：{code}，到期 {ExpiresAt:yyyy-MM-dd}，剩余 {DaysRemaining} 天");
        return Task.FromResult(LicenseResult.Active(ExpiresAt));
    }

    /// <summary>
    /// 重新从磁盘读取并评估。启动时调用一次；界面刷新许可状态时也可调用。
    /// </summary>
    public void Reload() => Evaluate();

    /// <summary>尽力刷新「最后运行时间」，失败不影响使用。</summary>
    public void TouchLastSeen()
    {
        if (_credential is null)
        {
            return;
        }

        var nowUtc = _time.GetUtcNow().UtcDateTime;
        if (nowUtc <= _credential.LastSeenUtc)
        {
            return;
        }

        var updated = _credential with { LastSeenUtc = nowUtc };
        try
        {
            _store.Save(updated);
            _credential = updated;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn($"更新最后运行时间失败：{ex.Message}（不影响本次使用）");
        }
    }

    private void Evaluate()
    {
        _credential = _store.Load();
        if (_credential is null)
        {
            State = LicenseState.NotActivated;
            ExpiresAt = null;
            DaysRemaining = null;
            return;
        }

        LicenseCode code;
        try
        {
            code = LicenseCodeCodec.Decode(_credential.CodeText);
        }
        catch (FormatException ex)
        {
            _logger.Warn($"本地授权凭证内容无法解析，按未激活处理：{ex.Message}");
            State = LicenseState.NotActivated;
            ExpiresAt = null;
            DaysRemaining = null;
            return;
        }

        bool verified;
        try
        {
            verified = LicenseVerifier.VerifyWithEmbeddedKey(code);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.Cryptography.CryptographicException)
        {
            _logger.Error("本地授权凭证验签时公钥不可用", ex);
            State = LicenseState.NotActivated;
            ExpiresAt = null;
            DaysRemaining = null;
            return;
        }

        if (!verified)
        {
            // 凭证文件虽然能解密（说明是本机），但内容与签名对不上，只能当作未激活
            _logger.Warn("本地授权凭证验签不通过，按未激活处理（需要重新激活）");
            State = LicenseState.NotActivated;
            ExpiresAt = null;
            DaysRemaining = null;
            return;
        }

        _code = code;

        var nowUtc = _time.GetUtcNow().UtcDateTime;

        // 关键防回拨：用「当前时间」和「历史上见过的最晚时间」取较大者。
        // 用户把系统时间调回上月也不会延长有效期，而且不会误伤——只是不给他便利。
        if (_credential.LastSeenUtc - nowUtc > ClockRollbackWarnThreshold)
        {
            _logger.Warn(
                $"系统时间早于上次运行记录约 {(_credential.LastSeenUtc - nowUtc).TotalDays:F1} 天，" +
                "有效期按较晚的时间计算");
        }

        var effectiveNow = _credential.LastSeenUtc > nowUtc ? _credential.LastSeenUtc : nowUtc;
        var expiresUtc = _credential.FirstActivatedUtc.AddDays(code.ValidDays);

        ExpiresAt = ToLocal(expiresUtc);

        var remaining = _credential.RemainingDays(effectiveNow, code.ValidDays);
        DaysRemaining = remaining;

        State = effectiveNow >= expiresUtc ? LicenseState.Expired : LicenseState.Active;

        _logger.Info(
            $"授权状态：{State}，签发 {code.IssuedOn:yyyy-MM-dd}，首次激活 {ToLocal(_credential.FirstActivatedUtc):yyyy-MM-dd HH:mm}，" +
            $"到期 {ExpiresAt:yyyy-MM-dd}，剩余 {remaining} 天");
    }

    private static string Normalize(string text)
    {
        try
        {
            return LicenseCodeCodec.Encode(LicenseCodeCodec.Decode(text));
        }
        catch (FormatException)
        {
            return text;
        }
    }

    private static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    /// <summary>当前生效的租约信息，供界面显示。</summary>
    public LicenseCode? CurrentCode => _code;
}
