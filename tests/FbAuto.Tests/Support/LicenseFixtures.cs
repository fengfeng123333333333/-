namespace FbAuto.Tests.Support;

/// <summary>
/// 固定测试数据。
///
/// <see cref="GoldenCode"/> 是用**当前这把真实私钥**在 2026-11-12 签发的一张 30 天授权码。
/// 把它钉在这里的意义：只要内置公钥被换错、字节布局被改动、签名字节序被颠倒，
/// 这里立刻变红。它同时证明了「客户端内置的公钥」与「发码方手里的私钥」确实是同一对。
///
/// 换密钥对时必须重新生成这一串，否则所有相关用例会集体失败——这是预期的警报，不是测试坏了。
/// </summary>
public static class LicenseFixtures
{
    /// <summary>用当前私钥签发的真码，120 字符，不可伪造。</summary>
    public const string GoldenCode =
        "040MVG2JP40KP00Y00QD6AR3JMKPBJXQ3PP9039A4KF06QFP5F3PXD89KM9QD6F8MFA64ZDJZ6R72EK47ZZXXA53KPY8HH94" +
        "781MX9TEXCP2YZQRJN0E9PKR";

    /// <summary>金样本里的随机序号。</summary>
    public const uint GoldenSerial = 1304449713u;

    /// <summary>
    /// 第二张真码，同样由当前私钥签发。用于验证「换一张码」时有效期重新计算。
    /// </summary>
    public const string SecondCode =
        "040KHHZ4A00KP00Y0014D8VZ5AKN4YQB0DQ4MSSMXF8RDJM0TRKETTW6DPTYE6QT2MJD65HEK4KETFJY7DWFXT126TWANPY7" +
        "X031BEK8NXV6A7TJYH7JCYN1";

    /// <summary>金样本的签发日。</summary>
    public static readonly DateOnly GoldenIssuedOn = new(2026, 11, 12);

    /// <summary>金样本的有效天数。</summary>
    public const int GoldenValidDays = 30;

    /// <summary>合法的 75 字节空授权码（有正确版本号，其余全零），用于构造异常输入。</summary>
    public static byte[] MakeRawBytes(byte version = 1, byte keyId = 1, int validDays = 30)
    {
        var raw = new byte[FbAuto.Licensing.LicenseCodeLayout.TotalLength];
        raw[0] = version;
        raw[1] = keyId;
        raw[8] = (byte)(validDays >> 8);
        raw[9] = (byte)(validDays & 0xFF);
        return raw;
    }
}
