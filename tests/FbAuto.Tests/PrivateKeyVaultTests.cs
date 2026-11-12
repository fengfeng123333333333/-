using System.Security.Cryptography;
using FbAuto.Keygen;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 私钥保管。私钥明文落盘等于把发码权送人，所以这组用例盯的是「口令不对一定解不开」。
/// </summary>
public class PrivateKeyVaultTests
{
    private const string Passphrase = "test-passphrase-2026";
    private const int FastIterations = 1000;

    [Fact]
    public void 保存后能用同一口令读回且公钥一致()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("private.key");

        using var original = PrivateKeyVault.CreateNew();
        var expectedPublic = PrivateKeyVault.ExportPublicKey(original);
        PrivateKeyVault.Save(path, original, Passphrase, FastIterations);

        using var loaded = PrivateKeyVault.Load(path, Passphrase);

        Assert.Equal(expectedPublic, PrivateKeyVault.ExportPublicKey(loaded));
    }

    [Fact]
    public void 口令错误时拒绝解开()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("private.key");

        using var key = PrivateKeyVault.CreateNew();
        PrivateKeyVault.Save(path, key, Passphrase, FastIterations);

        var ex = Assert.Throws<CryptographicException>(
            () => PrivateKeyVault.Load(path, "wrong-passphrase-2026"));

        Assert.Contains("口令错误", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234567")]
    public void 口令过短或为空时拒绝保存(string passphrase)
    {
        using var dir = new TempDirectory();
        using var key = PrivateKeyVault.CreateNew();

        Assert.Throws<ArgumentException>(
            () => PrivateKeyVault.Save(dir.Combine("private.key"), key, passphrase, FastIterations));
    }

    [Fact]
    public void 文件魔数被改时拒绝读取()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("private.key");

        using var key = PrivateKeyVault.CreateNew();
        PrivateKeyVault.Save(path, key, Passphrase, FastIterations);

        var raw = File.ReadAllBytes(path);
        raw[0] = 0x00;
        File.WriteAllBytes(path, raw);

        var ex = Assert.Throws<CryptographicException>(() => PrivateKeyVault.Load(path, Passphrase));
        Assert.Contains("不是本工具生成", ex.Message);
    }

    [Fact]
    public void 文件被截断时拒绝读取()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("private.key");

        using var key = PrivateKeyVault.CreateNew();
        PrivateKeyVault.Save(path, key, Passphrase, FastIterations);

        var raw = File.ReadAllBytes(path);
        File.WriteAllBytes(path, raw[..40]);

        Assert.Throws<CryptographicException>(() => PrivateKeyVault.Load(path, Passphrase));
    }

    [Fact]
    public void 私钥文件里不出现明文私钥字节()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("private.key");

        using var key = PrivateKeyVault.CreateNew();
        var plainPkcs8 = key.ExportPkcs8PrivateKey();
        PrivateKeyVault.Save(path, key, Passphrase, FastIterations);

        var fileBytes = File.ReadAllBytes(path);

        // 明文 PKCS8 序列不应作为连续子串出现在文件里
        Assert.False(
            ContainsSequence(fileBytes, plainPkcs8),
            "私钥文件里出现了明文私钥，加密没生效");
    }

    [Fact]
    public void 不同口令保存的两份私钥互不能解()
    {
        using var dir = new TempDirectory();
        var pathA = dir.Combine("a.key");
        var pathB = dir.Combine("b.key");

        using var keyA = PrivateKeyVault.CreateNew();
        using var keyB = PrivateKeyVault.CreateNew();
        PrivateKeyVault.Save(pathA, keyA, "passphrase-aaaa-2026", FastIterations);
        PrivateKeyVault.Save(pathB, keyB, "passphrase-bbbb-2026", FastIterations);

        Assert.Throws<CryptographicException>(() => PrivateKeyVault.Load(pathA, "passphrase-bbbb-2026"));
    }

    [Fact]
    public void 迭代次数记在文件头因此换参数仍能读旧文件()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("private.key");

        using var key = PrivateKeyVault.CreateNew();
        PrivateKeyVault.Save(path, key, Passphrase, iterations: 777);

        // Load 不接收迭代次数参数，只能从文件头读——这条用例保证它确实读了
        using var loaded = PrivateKeyVault.Load(path, Passphrase);

        Assert.Equal(
            PrivateKeyVault.ExportPublicKey(key),
            PrivateKeyVault.ExportPublicKey(loaded));
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
