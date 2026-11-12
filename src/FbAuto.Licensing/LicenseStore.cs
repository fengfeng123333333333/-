using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FbAuto.Core.Abstractions;

namespace FbAuto.Licensing;

/// <summary>
/// 凭证磁盘存储：AES-GCM 加密，密钥由机器码派生。
///
/// 为什么不直接存明文 JSON：明文等于把「首次激活时间」和「最后运行时间」摆给用户改，
/// 改一下到期日就能白用。加密密钥绑机器码，文件被拷到别的机器上解不开，顺手也挡住了「拷一份给朋友」。
/// 必须承认的边界：这是防普通用户，不是防逆向——本机内存里的密钥总能被拿到。
/// 离线授权的天花板就在这里，想真正防住只能联网。
/// </summary>
public sealed class LicenseStore
{
    private static readonly byte[] Magic = "FBAL"u8.ToArray();
    private const byte FileVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MinCipherLength = 4 + 8 + 8;

    /// <summary>PBKDF2 迭代次数。默认 10 万，测试可调低以缩短耗时。</summary>
    public const int DefaultIterations = 100_000;

    private static readonly byte[] Salt = Encoding.UTF8.GetBytes("FbAuto.License.Credential.v1");

    private readonly string _filePath;
    private readonly string _machineCode;
    private readonly IAppLogger _logger;
    private readonly int _iterations;

    public LicenseStore(string filePath, string machineCode, IAppLogger logger, int iterations = DefaultIterations)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("凭证路径不能为空", nameof(filePath));
        }

        if (string.IsNullOrWhiteSpace(machineCode))
        {
            throw new ArgumentException("机器码不能为空，凭证加密依赖它", nameof(machineCode));
        }

        if (iterations <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations), iterations, "迭代次数必须大于 0");
        }

        _filePath = filePath;
        _machineCode = machineCode;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _iterations = iterations;
    }

    /// <summary>凭证文件完整路径。</summary>
    public string FilePath => _filePath;

    /// <summary>文件是否存在。</summary>
    public bool Exists => File.Exists(_filePath);

    /// <summary>
    /// 读取凭证。文件不存在、损坏、机器码变更一律返回 null 并记日志，不抛异常——
    /// 这个方法在程序启动路径上，抛出去就是一个白屏或者静默退出。
    /// </summary>
    public LicenseCredential? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var raw = File.ReadAllBytes(_filePath);
            if (raw.Length < Magic.Length + 1 + NonceSize + TagSize + MinCipherLength)
            {
                _logger.Warn("授权凭证文件长度异常，按未激活处理");
                return null;
            }

            if (!raw.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                _logger.Warn("授权凭证文件标识不匹配，按未激活处理");
                return null;
            }

            var version = raw[Magic.Length];
            if (version != FileVersion)
            {
                _logger.Warn($"授权凭证版本 {version} 与当前程序（{FileVersion}）不符，按未激活处理");
                return null;
            }

            var offset = Magic.Length + 1;
            var nonce = raw.AsSpan(offset, NonceSize);
            offset += NonceSize;
            var tag = raw.AsSpan(offset, TagSize);
            offset += TagSize;
            var cipher = raw.AsSpan(offset);

            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(DeriveKey(), TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);

            return Deserialize(plain);
        }
        catch (CryptographicException)
        {
            // 机器码变了（换机器）或文件被改动，两种情况都只能要求重新激活
            _logger.Warn("授权凭证无法解密：可能更换了机器或文件被修改，需要重新激活");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            _logger.Warn($"读取授权凭证失败：{ex.Message}，按未激活处理");
            return null;
        }
    }

    /// <summary>
    /// 写入凭证。写不进去必须抛——用户以为激活成功了，重启却又要激活，比直接报错更糟。
    /// </summary>
    public void Save(LicenseCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var plain = Serialize(credential);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(DeriveKey(), TagSize))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = new byte[Magic.Length + 1 + NonceSize + TagSize + cipher.Length];
        Magic.CopyTo(bytes, 0);
        bytes[Magic.Length] = FileVersion;
        var offset = Magic.Length + 1;
        nonce.CopyTo(bytes, offset);
        offset += NonceSize;
        tag.CopyTo(bytes, offset);
        offset += TagSize;
        cipher.CopyTo(bytes, offset);

        // 先写临时文件再替换，避免写到一半断电留下半个文件
        var temp = _filePath + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, _filePath, overwrite: true);
    }

    /// <summary>删除凭证，用于停用或重新激活。</summary>
    public void Delete()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private byte[] DeriveKey() =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(_machineCode), Salt, _iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Serialize(LicenseCredential credential)
    {
        var codeBytes = Encoding.UTF8.GetBytes(credential.CodeText);
        var buffer = new byte[4 + codeBytes.Length + 8 + 8];
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(0, 4), codeBytes.Length);
        codeBytes.CopyTo(buffer, 4);
        var offset = 4 + codeBytes.Length;
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, 8), credential.FirstActivatedUtc.Ticks);
        offset += 8;
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(offset, 8), credential.LastSeenUtc.Ticks);
        return buffer;
    }

    private static LicenseCredential Deserialize(byte[] plain)
    {
        if (plain.Length < 4)
        {
            throw new FormatException("凭证内容过短");
        }

        var codeLength = BinaryPrimitives.ReadInt32BigEndian(plain.AsSpan(0, 4));
        if (codeLength <= 0 || plain.Length < 4 + codeLength + 16)
        {
            throw new FormatException("凭证内容长度字段异常");
        }

        var code = Encoding.UTF8.GetString(plain, 4, codeLength);
        var offset = 4 + codeLength;
        var firstTicks = BinaryPrimitives.ReadInt64BigEndian(plain.AsSpan(offset, 8));
        offset += 8;
        var lastTicks = BinaryPrimitives.ReadInt64BigEndian(plain.AsSpan(offset, 8));

        return new LicenseCredential(
            code,
            new DateTime(firstTicks, DateTimeKind.Utc),
            new DateTime(lastTicks, DateTimeKind.Utc));
    }
}
