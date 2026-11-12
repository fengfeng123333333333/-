using System.Security.Cryptography;
using FbAuto.Licensing;

namespace FbAuto.Keygen;

/// <summary>
/// 签发授权码。这是唯一持有私钥能力的地方，客户端程序集里没有对应代码。
/// </summary>
public static class CodeIssuer
{
    /// <summary>签发一张授权码。序号随机生成，避免批次之间可预测。</summary>
    public static LicenseCode Issue(
        ECDsa privateKey,
        DateOnly issuedOn,
        int validDays,
        byte flags = 0,
        byte keyId = LicenseCodeLayout.CurrentKeyId,
        uint? serial = null)
    {
        ArgumentNullException.ThrowIfNull(privateKey);

        var actualSerial = serial ?? (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

        var payload = LicenseCode.BuildPayload(
            LicenseCodeLayout.CurrentVersion, keyId, actualSerial, issuedOn, validDays, flags);

        var signature = privateKey.SignData(
            payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        // 底层若换了算法或格式，长度会变；宁可签发时炸掉，也不要发出一批验不过的码
        if (signature.Length != LicenseCodeLayout.SignatureLength)
        {
            throw new InvalidOperationException(
                $"签名长度 {signature.Length} 字节，期望 {LicenseCodeLayout.SignatureLength}。" +
                "请确认签名格式已显式指定为 IEEE P1363 定长格式（不能依赖默认值）。");
        }

        return LicenseCode.FromPayload(payload, signature);
    }

    /// <summary>批量签发，序号保证不重复。</summary>
    public static IReadOnlyList<LicenseCode> IssueBatch(
        ECDsa privateKey, int count, DateOnly issuedOn, int validDays)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "生成数量必须大于 0");
        }

        var serials = new HashSet<uint>(count);
        var results = new List<LicenseCode>(count);

        while (results.Count < count)
        {
            var serial = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            if (!serials.Add(serial))
            {
                continue;
            }

            results.Add(Issue(privateKey, issuedOn, validDays, serial: serial));
        }

        return results;
    }
}
