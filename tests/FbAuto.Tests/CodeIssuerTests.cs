using FbAuto.Keygen;
using FbAuto.Licensing;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 批量签发。盯的是「签出来的每一张都必须验得过」以及「序号不重复」。
/// </summary>
public class CodeIssuerTests
{
    private static readonly DateOnly IssuedOn = new(2026, 11, 12);

    [Fact]
    public void 签出的码能被自己的公钥验证通过()
    {
        using var key = PrivateKeyVault.CreateNew();
        var code = CodeIssuer.Issue(key, IssuedOn, 30, serial: 1);

        Assert.True(LicenseVerifier.Verify(code, PrivateKeyVault.ExportPublicKey(key)));
    }

    [Fact]
    public void 签出的码字段与入参一致()
    {
        using var key = PrivateKeyVault.CreateNew();
        var code = CodeIssuer.Issue(key, IssuedOn, 45, flags: 0, serial: 12345);

        Assert.Equal(LicenseCodeLayout.CurrentVersion, code.Version);
        Assert.Equal(LicenseCodeLayout.CurrentKeyId, code.KeyId);
        Assert.Equal(12345u, code.Serial);
        Assert.Equal(IssuedOn, code.IssuedOn);
        Assert.Equal(45, code.ValidDays);
    }

    [Fact]
    public void 批量签发数量正确且序号不重复()
    {
        using var key = PrivateKeyVault.CreateNew();
        var codes = CodeIssuer.IssueBatch(key, 50, IssuedOn, 30);

        Assert.Equal(50, codes.Count);
        Assert.Equal(50, codes.Select(c => c.Serial).Distinct().Count());
    }

    [Fact]
    public void 批量签发的每一张都能验签通过且文本互不相同()
    {
        using var key = PrivateKeyVault.CreateNew();
        var publicKey = PrivateKeyVault.ExportPublicKey(key);
        var codes = CodeIssuer.IssueBatch(key, 20, IssuedOn, 30);

        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            Assert.True(LicenseVerifier.Verify(code, publicKey));

            var text = LicenseCodeCodec.Encode(code);
            Assert.Equal(120, text.Length);
            Assert.True(texts.Add(text), "出现了两张文本完全相同的授权码");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void 数量非正数时应拒绝(int count)
    {
        using var key = PrivateKeyVault.CreateNew();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CodeIssuer.IssueBatch(key, count, IssuedOn, 30));
    }

    [Fact]
    public void 有效天数为零时应拒绝签发()
    {
        using var key = PrivateKeyVault.CreateNew();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CodeIssuer.Issue(key, IssuedOn, 0, serial: 1));
    }

    [Fact]
    public void 不指定序号时自动生成且不为零()
    {
        using var key = PrivateKeyVault.CreateNew();

        var a = CodeIssuer.Issue(key, IssuedOn, 30);
        var b = CodeIssuer.Issue(key, IssuedOn, 30);

        Assert.True(a.Serial > 0);
        Assert.True(b.Serial > 0);
        Assert.NotEqual(a.Serial, b.Serial);
    }

    [Fact]
    public void 签出的码经文本往返后仍能验签()
    {
        using var key = PrivateKeyVault.CreateNew();
        var publicKey = PrivateKeyVault.ExportPublicKey(key);
        var code = CodeIssuer.Issue(key, IssuedOn, 30, serial: 77);

        var text = LicenseCodeCodec.Encode(code);
        var decoded = LicenseCodeCodec.Decode(text);

        Assert.True(LicenseVerifier.Verify(decoded, publicKey));
        Assert.Equal(code.Serial, decoded.Serial);
    }
}
