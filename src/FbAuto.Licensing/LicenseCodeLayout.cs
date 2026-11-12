namespace FbAuto.Licensing;

/// <summary>
/// 授权码的二进制布局常量。改这里等于换代，必须同步 <see cref="LicenseCodeLayout.CurrentVersion"/>，
/// 让旧码在客户端被明确拒绝而不是被误解析成乱码。
///
/// 布局（大端）：
///   0      version   1 字节   格式版本
///   1      keyId     1 字节   签发用的公钥编号，为将来轮换密钥留位
///   2..5   serial    4 字节   随机序号，仅用于区分批次，不参与验签以外的逻辑
///   6..7   issuedDay 2 字节   签发日，自 Epoch 起的天数
///   8..9   validDays 2 字节   激活后的有效天数
///   10     flags     1 字节   预留功能位
///   11..74 signature 64 字节  ECDSA P-256 / SHA-256 / IEEE P1363 定长签名
/// </summary>
public static class LicenseCodeLayout
{
    /// <summary>当前格式版本。</summary>
    public const byte CurrentVersion = 1;

    /// <summary>当前公钥编号。</summary>
    public const byte CurrentKeyId = 1;

    /// <summary>负载（被签名的部分）长度。</summary>
    public const int PayloadLength = 11;

    /// <summary>签名长度。P-256 的 IEEE P1363 格式恒为 r(32)+s(32)。</summary>
    public const int SignatureLength = 64;

    /// <summary>授权码解码后的总字节数。</summary>
    public const int TotalLength = PayloadLength + SignatureLength;

    /// <summary>
    /// 授权码文本的字符数。75 字节 × 8 = 600 位，正好 120 个 5 位组，无需填充，
    /// 所以长度是精确值而不是上限。
    /// </summary>
    public const int TextLength = TotalLength * 8 / 5;

    /// <summary>
    /// 签发日的计算起点。用固定纪元而不是 1970，是为了让 2 字节装下 179 年（到 2205 年）。
    /// 这个值一旦发布就不能改，否则所有旧码的签发日会整体偏移。
    /// </summary>
    public static readonly DateOnly Epoch = new(2026, 1, 1);

    /// <summary>把日期换算成自纪元起的天数。</summary>
    public static ushort ToDayNumber(DateOnly date)
    {
        var days = date.DayNumber - Epoch.DayNumber;
        if (days is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date), date, $"签发日超出可表示范围（{Epoch:yyyy-MM-dd} 起 179 年内）");
        }

        return (ushort)days;
    }

    /// <summary>把天数换算回日期。</summary>
    public static DateOnly FromDayNumber(ushort dayNumber) => Epoch.AddDays(dayNumber);
}
