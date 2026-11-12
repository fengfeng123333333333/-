namespace FbAuto.Licensing;

/// <summary>
/// 本机已激活的凭证。三个字段缺一不可：
/// <para><see cref="CodeText"/> 是授权码原文，每次启动重新验签，防止凭证被手改成一张不存在的码。</para>
/// <para><see cref="FirstActivatedUtc"/> 决定到期日。离线签发时无法预知何时激活，只能激活时落一次。</para>
/// <para><see cref="LastSeenUtc"/> 用于识别时间回拨。每次启动取 max(当前时间, 本字段) 作为计算基准。</para>
/// </summary>
/// <param name="CodeText">授权码原文。</param>
/// <param name="FirstActivatedUtc">首次激活时间（UTC）。</param>
/// <param name="LastSeenUtc">最后一次运行时间（UTC）。</param>
public sealed record LicenseCredential(
    string CodeText,
    DateTime FirstActivatedUtc,
    DateTime LastSeenUtc)
{
    /// <summary>按到期日计算，判断在给定时间点是否仍然有效。</summary>
    public bool IsExpiredAt(DateTime effectiveNowUtc, int validDays) =>
        effectiveNowUtc >= FirstActivatedUtc.AddDays(validDays);

    /// <summary>剩余天数，已过期为 0，用向上取整让最后一天仍显示 1 天。</summary>
    public int RemainingDays(DateTime effectiveNowUtc, int validDays)
    {
        var remaining = (FirstActivatedUtc.AddDays(validDays) - effectiveNowUtc).TotalDays;
        return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
    }
}
