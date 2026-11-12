namespace FbAuto.Core.Models;

/// <summary>
/// 账号清单里的一行。字段与 <c>docs/accounts.sample.csv</c> 的列一一对应。
/// 凭证不在这里：只存引用名（cred://acc01），真实密码由 DPAPI 存在仓库外。
/// </summary>
public sealed record AccountEntry
{
    /// <summary>别名（全表唯一），如 acc01。</summary>
    public required string Alias { get; init; }

    public required BrowserChannelKind Channel { get; init; }

    /// <summary>Chrome 的 user-data-dir。bit 通道为空。</summary>
    public string ProfileDirectory { get; init; } = string.Empty;

    /// <summary>比特窗口 ID。google 通道为空。</summary>
    public string BitProfileId { get; init; } = string.Empty;

    /// <summary>代理，形如 http://127.0.0.1:7890。空表示直连。</summary>
    public string Proxy { get; init; } = string.Empty;

    public string HomePageId { get; init; } = string.Empty;

    public string HomePageName { get; init; } = string.Empty;

    /// <summary>凭证引用名，如 cred://acc01。S4 不解析。</summary>
    public string CredentialRef { get; init; } = string.Empty;

    /// <summary>状态列原值：待接入 / 正常 / 停用。</summary>
    public string Status { get; init; } = string.Empty;

    public string Remark { get; init; } = string.Empty;

    /// <summary>
    /// 是否允许执行。「停用」排除；其余（含「待接入」）允许——
    /// 待接入只是还没验过登录态，不是禁止跑，拦住它会让首次使用全部卡死。
    /// </summary>
    public bool IsUsable => !Status.Contains("停用", StringComparison.Ordinal);

    /// <summary>界面「浏览器」列的显示名：Google 用别名，Bit 用窗口 ID 尾号。</summary>
    public string BrowserDisplayName => Channel == BrowserChannelKind.Google
        ? $"Chrome-{Alias}"
        : $"Bit-{Alias}";
}
