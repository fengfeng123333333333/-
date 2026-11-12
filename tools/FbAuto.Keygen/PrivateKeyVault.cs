using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace FbAuto.Keygen;

/// <summary>
/// 私钥保管：私钥落地必须加密，口令派生密钥。明文私钥放在磁盘上，
/// 等于把「能签发任意授权码」的能力交给任何能读到这个文件的人。
///
/// 文件头里存了迭代次数，以后调大参数不会让旧文件读不出来。
/// </summary>
public static class PrivateKeyVault
{
    private static readonly byte[] Magic = "FBK1"u8.ToArray();
    private const byte FileVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>默认迭代次数。私钥比凭证更值钱，所以比凭证那侧的 10 万更高。</summary>
    public const int DefaultIterations = 200_000;

    /// <summary>生成一对新的 P-256 密钥。</summary>
    public static ECDsa CreateNew() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>把私钥用口令加密后写入指定路径。</summary>
    public static void Save(string path, ECDsa key, string passphrase, int iterations = DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidatePassphrase(passphrase);

        var pkcs8 = key.ExportPkcs8PrivateKey();
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[pkcs8.Length];
        var tag = new byte[TagSize];

        var derived = DeriveKey(passphrase, salt, iterations);
        try
        {
            using var aes = new AesGcm(derived, TagSize);
            aes.Encrypt(nonce, pkcs8, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
            CryptographicOperations.ZeroMemory(pkcs8);
        }

        var bytes = new byte[Magic.Length + 1 + 4 + SaltSize + NonceSize + TagSize + cipher.Length];
        Magic.CopyTo(bytes, 0);
        var offset = Magic.Length;
        bytes[offset++] = FileVersion;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), iterations);
        offset += 4;
        salt.CopyTo(bytes, offset);
        offset += SaltSize;
        nonce.CopyTo(bytes, offset);
        offset += NonceSize;
        tag.CopyTo(bytes, offset);
        offset += TagSize;
        cipher.CopyTo(bytes, offset);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    /// <summary>用口令解开私钥。口令错或文件被改都会抛 <see cref="CryptographicException"/>。</summary>
    public static ECDsa Load(string path, string passphrase)
    {
        ValidatePassphrase(passphrase);

        var raw = File.ReadAllBytes(path);
        var headerLength = Magic.Length + 1 + 4 + SaltSize + NonceSize + TagSize;
        if (raw.Length < headerLength + 1)
        {
            throw new CryptographicException("私钥文件长度异常，可能已损坏");
        }

        if (!raw.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException("这不是本工具生成的私钥文件");
        }

        var offset = Magic.Length;
        var version = raw[offset++];
        if (version != FileVersion)
        {
            throw new CryptographicException($"私钥文件版本 {version} 不受支持（当前 {FileVersion}）");
        }

        var iterations = BinaryPrimitives.ReadInt32BigEndian(raw.AsSpan(offset, 4));
        offset += 4;
        var salt = raw.AsSpan(offset, SaltSize);
        offset += SaltSize;
        var nonce = raw.AsSpan(offset, NonceSize);
        offset += NonceSize;
        var tag = raw.AsSpan(offset, TagSize);
        offset += TagSize;
        var cipher = raw.AsSpan(offset).ToArray();
        var plain = new byte[cipher.Length];

        var derived = DeriveKey(passphrase, salt, iterations);
        try
        {
            using var aes = new AesGcm(derived, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("口令错误，私钥无法解开");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);
        }

        var key = CreateNew();
        try
        {
            key.ImportPkcs8PrivateKey(plain, out _);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        return key;
    }

    /// <summary>导出公钥的 SubjectPublicKeyInfo 字节。</summary>
    public static byte[] ExportPublicKey(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.ExportSubjectPublicKeyInfo();
    }

    private static byte[] DeriveKey(string passphrase, ReadOnlySpan<byte> salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);

    private static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
        {
            throw new ArgumentException("口令不能为空", nameof(passphrase));
        }

        if (passphrase.Length < 8)
        {
            throw new ArgumentException("口令至少 8 位，否则离线爆破成本太低", nameof(passphrase));
        }
    }
}
