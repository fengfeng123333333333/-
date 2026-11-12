using FbAuto.Licensing;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 授权码文本与对象之间的转换。重点看两件事：长度是否稳定、坏输入是否给出可读的中文提示。
/// </summary>
public class LicenseCodeCodecTests
{
    [Fact]
    public void 金样本解码后各字段正确()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);

        Assert.Equal(LicenseCodeLayout.CurrentVersion, code.Version);
        Assert.Equal(LicenseCodeLayout.CurrentKeyId, code.KeyId);
        Assert.Equal(LicenseFixtures.GoldenSerial, code.Serial);
        Assert.Equal(LicenseFixtures.GoldenIssuedOn, code.IssuedOn);
        Assert.Equal(LicenseFixtures.GoldenValidDays, code.ValidDays);
        Assert.Equal(0, code.Flags);
        Assert.Equal(LicenseCodeLayout.SignatureLength, code.Signature.Length);
    }

    [Fact]
    public void 金样本长度是一百二十字符()
    {
        Assert.Equal(120, LicenseFixtures.GoldenCode.Length);
    }

    [Fact]
    public void 金样本重新编码后与原串完全一致()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);

        Assert.Equal(LicenseFixtures.GoldenCode, LicenseCodeCodec.Encode(code));
    }

    [Fact]
    public void 加了连字符和空格仍能解码()
    {
        var grouped = LicenseCodeCodec.ToGrouped(LicenseFixtures.GoldenCode);
        Assert.Contains('-', grouped);

        var code = LicenseCodeCodec.Decode(grouped);
        Assert.Equal(LicenseFixtures.GoldenSerial, code.Serial);
    }

    [Fact]
    public void 分组后字符数因连字符增加但内容不变()
    {
        var grouped = LicenseCodeCodec.ToGrouped(LicenseFixtures.GoldenCode, 5);

        Assert.Equal(120 / 5 - 1 + 120, grouped.Length);
        Assert.Equal(
            LicenseFixtures.GoldenCode,
            grouped.Replace("-", string.Empty, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void 空输入给出可读提示(string input)
    {
        var ex = Assert.Throws<FormatException>(() => LicenseCodeCodec.Decode(input));
        Assert.Contains("授权码为空", ex.Message);
    }

    [Fact]
    public void 长度不足时提示少复制了一段()
    {
        // 少一个字符 → 解出 74 字节
        var truncated = LicenseFixtures.GoldenCode[..^1];

        var ex = Assert.Throws<FormatException>(() => LicenseCodeCodec.Decode(truncated));
        Assert.Contains("长度不对", ex.Message);
        Assert.Contains("重新复制", ex.Message);
    }

    [Fact]
    public void 长度过多时同样报长度不对()
    {
        var extended = LicenseFixtures.GoldenCode + "00";

        var ex = Assert.Throws<FormatException>(() => LicenseCodeCodec.Decode(extended));
        Assert.Contains("长度不对", ex.Message);
    }

    [Fact]
    public void 版本号不匹配时明确说是版本问题()
    {
        var bytes = LicenseFixtures.MakeRawBytes(version: 9);
        var text = Base32Crockford.Encode(bytes);

        var ex = Assert.Throws<FormatException>(() => LicenseCodeCodec.Decode(text));
        Assert.Contains("格式版本", ex.Message);
        Assert.Contains("9", ex.Message);
    }

    [Fact]
    public void 有效天数为零的码被拒绝()
    {
        var bytes = LicenseFixtures.MakeRawBytes(version: 1, validDays: 0);
        var text = Base32Crockford.Encode(bytes);

        var ex = Assert.Throws<FormatException>(() => LicenseCodeCodec.Decode(text));
        Assert.Contains("有效天数为 0", ex.Message);
    }

    [Fact]
    public void 含非法字符时提示具体字符()
    {
        var broken = "U" + LicenseFixtures.GoldenCode[1..];

        var ex = Assert.Throws<FormatException>(() => LicenseCodeCodec.Decode(broken));
        Assert.Contains("非法字符", ex.Message);
    }

    [Fact]
    public void 负载字节必须能被重构回来()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);
        var payload = code.ToPayloadBytes();

        Assert.Equal(LicenseCodeLayout.PayloadLength, payload.Length);
        Assert.Equal(LicenseCodeLayout.CurrentVersion, payload[0]);
        Assert.Equal(LicenseCodeLayout.CurrentKeyId, payload[1]);
    }

    [Fact]
    public void 签发日超过可表示范围应报错()
    {
        // 纪元起点之前
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LicenseCodeLayout.ToDayNumber(new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void 签发日纪元边界可正常往返()
    {
        var day = LicenseCodeLayout.Epoch;
        var number = LicenseCodeLayout.ToDayNumber(day);

        Assert.Equal(0, number);
        Assert.Equal(day, LicenseCodeLayout.FromDayNumber(number));
    }
}
