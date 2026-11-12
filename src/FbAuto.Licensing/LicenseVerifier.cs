using System.Security.Cryptography;

namespace FbAuto.Licensing;

/// <summary>
/// 验签。只依赖公钥，没有任何签发能力——这是整套授权「不可伪造」的根基。
/// </summary>
public static class LicenseVerifier
{
    /// <summary>
    /// 校验授权码是否由对应私钥签发。
    /// 公钥本身有问题时抛出 <see cref="CryptographicException"/>，那属于程序配置错误，不是用户的错。
    /// </summary>
    public static bool Verify(LicenseCode code, ReadOnlySpan<byte> publicKeySpki)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (code.Signature.Length != LicenseCodeLayout.SignatureLength)
        {
            return false;
        }

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);

        return ecdsa.VerifyData(
            code.ToPayloadBytes(),
            code.Signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>用内置公钥验签，客户端唯一该走的入口。</summary>
    public static bool VerifyWithEmbeddedKey(LicenseCode code) =>
        Verify(code, LicensePublicKey.GetSpki());
}
