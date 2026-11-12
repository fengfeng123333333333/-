using FbAuto.Licensing;

namespace FbAuto.Tests;

/// <summary>
/// Base32 编解码。这是整条授权链的最底层，它错一位，所有上层结论都不作数。
/// </summary>
public class Base32CrockfordTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(11)]
    [InlineData(64)]
    [InlineData(75)]
    [InlineData(100)]
    public void 任意长度往返后内容一致(int length)
    {
        var original = new byte[length];
        Random.Shared.NextBytes(original);

        var encoded = Base32Crockford.Encode(original);
        var decoded = Base32Crockford.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void 七十五字节编码成一百二十字符()
    {
        var bytes = new byte[75];
        var encoded = Base32Crockford.Encode(bytes);

        // 75 * 8 = 600 位，正好 120 个 5 位组，不需要填充
        Assert.Equal(120, encoded.Length);
    }

    [Fact]
    public void 空输入编码为空串解码为空数组()
    {
        Assert.Equal(string.Empty, Base32Crockford.Encode([]));
        Assert.Empty(Base32Crockford.Decode(string.Empty));
    }

    [Fact]
    public void 编码结果只含字母表内字符且无小写()
    {
        var bytes = new byte[75];
        Random.Shared.NextBytes(bytes);

        var encoded = Base32Crockford.Encode(bytes);

        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        Assert.All(encoded, c => Assert.Contains(c, alphabet));
    }

    [Fact]
    public void 解码忽略连字符与空白()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var encoded = Base32Crockford.Encode(bytes);
        var spaced = string.Join("-", encoded.ToCharArray()) + "\r\n ";

        Assert.Equal(bytes, Base32Crockford.Decode(spaced));
    }

    [Fact]
    public void 解码时小写等价于大写()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var encoded = Base32Crockford.Encode(bytes);

        Assert.Equal(bytes, Base32Crockford.Decode(encoded.ToLowerInvariant()));
    }

    [Fact]
    public void 解码把字母I和L当作数字1把O当作数字0()
    {
        // 0x00 编码为 "00"，0x08 编码为 "10"，正好只含 0 和 1 两个字符
        Assert.Equal("00", Base32Crockford.Encode(new byte[] { 0x00 }));
        Assert.Equal("10", Base32Crockford.Encode(new byte[] { 0x08 }));

        Assert.Equal(Base32Crockford.Decode("00"), Base32Crockford.Decode("OO"));
        Assert.Equal(Base32Crockford.Decode("10"), Base32Crockford.Decode("I0"));
        Assert.Equal(Base32Crockford.Decode("10"), Base32Crockford.Decode("L0"));
        Assert.Equal(Base32Crockford.Decode("10"), Base32Crockford.Decode("l0"));
    }

    [Fact]
    public void 字母U不在字母表内应报错()
    {
        var ex = Assert.Throws<FormatException>(() => Base32Crockford.Decode("ABUCD"));
        Assert.Contains("非法字符", ex.Message);
    }

    [Fact]
    public void 字母表之外的符号应报错()
    {
        Assert.Throws<FormatException>(() => Base32Crockford.Decode("AB#CD"));
        Assert.Throws<FormatException>(() => Base32Crockford.Decode("AB@CD"));
    }

    [Fact]
    public void 尾部填充位非零说明内容被截断应报错()
    {
        // 单个字符只提供 5 位，不足一个字节，剩余位必须为 0；'1' 的值是 1，非零
        var ex = Assert.Throws<FormatException>(() => Base32Crockford.Decode("1"));
        Assert.Contains("校验位异常", ex.Message);
    }

    [Fact]
    public void 尾部填充位为零的残缺串不报错但内容不足()
    {
        // '0' 的值是 0，剩余 5 位全零，按规则合法，但只有 0 个完整字节
        Assert.Empty(Base32Crockford.Decode("0"));
    }
}
