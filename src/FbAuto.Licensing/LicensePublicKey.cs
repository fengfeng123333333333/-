namespace FbAuto.Licensing;

/// <summary>
/// 客户端内置的公钥。它能验签、不能签名——即便被反编译拿走，也造不出新授权码。
///
/// 这里的值由签发工具 <c>fbauto-keygen initkey</c> 生成后用 <c>emit</c> 命令输出并粘贴进来。
/// 换密钥 = 换这一行 + 重新发码，所以密钥对要长期保管好。
/// </summary>
public static class LicensePublicKey
{
    /// <summary>P-256 公钥的 SubjectPublicKeyInfo（DER）的 Base64。</summary>
    private const string SpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEoRtCdoe2FeeEGUvmrE3EA8dMKmo2q7sz0IOR5IQof/PqgKiDirqCILs1BdEW9ZrJA+XB9g2y/pyG1tblYzIj9w==";

    /// <summary>公钥是否已被替换成真实值。</summary>
    public static bool IsConfigured =>
        !string.Equals(SpkiBase64, "REPLACE_WITH_GENERATED_PUBLIC_KEY", StringComparison.Ordinal);

    /// <summary>
    /// 取公钥 DER 字节。未配置时抛出明确异常，而不是让 Convert 抛一个看不懂的格式错误。
    /// </summary>
    public static byte[] GetSpki()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "客户端尚未内置公钥：请先用 fbauto-keygen initkey 生成密钥对，" +
                "再把 emit 输出的值填入 LicensePublicKey.SpkiBase64。");
        }

        return Convert.FromBase64String(SpkiBase64);
    }

    /// <summary>供签发工具生成代码行用，仅测试读取。</summary>
    public static string RawSpkiBase64 => SpkiBase64;
}
