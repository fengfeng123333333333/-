using System.Security.Cryptography;
using FbAuto.Keygen;
using FbAuto.Licensing;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 验签是整个授权的安全边界：它必须对**任何**字节改动说不，同时承认自己签的码。
/// </summary>
public class LicenseVerifierTests
{
    [Fact]
    public void 金样本用内置公钥验签通过()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);

        Assert.True(LicenseVerifier.VerifyWithEmbeddedKey(code));
    }

    [Fact]
    public void 内置公钥已配置且能正常导入()
    {
        Assert.True(LicensePublicKey.IsConfigured, "客户端未内置公钥，激活功能不可用");

        var spki = LicensePublicKey.GetSpki();
        Assert.Equal(91, spki.Length); // P-256 SPKI 定长 91 字节

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
    }

    [Fact]
    public void 金样本负载每一位被改动都必须验签失败()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);
        var payload = code.ToPayloadBytes();

        for (var i = 0; i < payload.Length; i++)
        {
            var tampered = (byte[])payload.Clone();
            tampered[i] ^= 0x01;

            var forged = LicenseCode.FromPayload(tampered, code.Signature);

            Assert.False(
                LicenseVerifier.VerifyWithEmbeddedKey(forged),
                $"改动负载第 {i} 字节后仍然验签通过，说明签名没有覆盖该字段");
        }
    }

    [Fact]
    public void 金样本签名每一位被改动都必须验签失败()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);

        // 每个字节都换一次签名字节，不必 64 次全跑，抽样即可覆盖 r 与 s 两段
        foreach (var position in new[] { 0, 1, 15, 31, 32, 47, 62, 63 })
        {
            var tampered = (byte[])code.Signature.Clone();
            tampered[position] ^= 0x01;

            var forged = LicenseCode.FromPayload(code.ToPayloadBytes(), tampered);

            Assert.False(
                LicenseVerifier.VerifyWithEmbeddedKey(forged),
                $"改动签名第 {position} 字节后仍然验签通过");
        }
    }

    [Fact]
    public void 别人用自己的密钥签的码用内置公钥验不过()
    {
        using var otherKey = PrivateKeyVault.CreateNew();
        var otherCode = CodeIssuer.Issue(
            otherKey, new DateOnly(2026, 11, 12), 30, serial: 42);

        Assert.False(LicenseVerifier.VerifyWithEmbeddedKey(otherCode));

        // 但它用自己那把公钥验是通的，说明失败原因是「不是这把私钥」，不是码本身坏了
        Assert.True(LicenseVerifier.Verify(otherCode, PrivateKeyVault.ExportPublicKey(otherKey)));
    }

    [Fact]
    public void 自签自验链路成立()
    {
        using var key = PrivateKeyVault.CreateNew();
        var code = CodeIssuer.Issue(key, new DateOnly(2026, 11, 12), 30, serial: 7);

        Assert.True(LicenseVerifier.Verify(code, PrivateKeyVault.ExportPublicKey(key)));
    }

    [Fact]
    public void 公钥无法签发新码()
    {
        // 这是整套授权成立的前提：客户端里只有公钥，反编译也造不出码
        using var publicOnly = ECDsa.Create();
        publicOnly.ImportSubjectPublicKeyInfo(LicensePublicKey.GetSpki(), out _);

        var payload = LicenseCode.BuildPayload(1, 1, 1, new DateOnly(2026, 11, 12), 30, 0);

        Assert.ThrowsAny<CryptographicException>(() =>
            publicOnly.SignData(
                payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void 签名长度不是六十四字节的对象构造不出来()
    {
        var code = LicenseCodeCodec.Decode(LicenseFixtures.GoldenCode);
        var shortSignature = code.Signature[..63];

        // FromPayload 是唯一入口，它直接挡掉；Verify 里那句长度判断只是最后一道防线
        var ex = Assert.Throws<ArgumentException>(
            () => LicenseCode.FromPayload(code.ToPayloadBytes(), shortSignature));
        Assert.Contains("签名长度", ex.Message);
    }

    [Fact]
    public void 签发工具产出的签名长度恒为六十四字节()
    {
        using var key = PrivateKeyVault.CreateNew();

        // 连续签 5 张，长度必须稳定；一旦底层换了格式（比如变成 DER 变长）这里立刻红
        for (var i = 0; i < 5; i++)
        {
            var code = CodeIssuer.Issue(key, new DateOnly(2026, 11, 12), 30, serial: (uint)(i + 1));
            Assert.Equal(64, code.Signature.Length);
        }
    }
}
