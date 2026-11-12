using System.Text;
using FbAuto.Licensing;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 凭证加密存储。要防的是「手改到期时间」和「拷文件到别的机器」这两条最省事的绕过路径。
/// </summary>
public class LicenseStoreTests
{
    private const string MachineCode = "6AD79298-1111-2222-3333-444455556666";

    // 测试用低迭代次数：默认 10 万次在这里会跑成几十秒，安全性不靠测试的迭代次数体现
    private const int FastIterations = 1000;

    private static LicenseStore CreateStore(TempDirectory dir, string machineCode = MachineCode)
        => new(dir.Combine("license.dat"), machineCode, new TestLogger(), FastIterations);

    private static LicenseCredential SampleCredential() => new(
        LicenseFixtures.GoldenCode,
        new DateTime(2026, 11, 12, 1, 2, 3, DateTimeKind.Utc),
        new DateTime(2026, 11, 13, 4, 5, 6, DateTimeKind.Utc));

    [Fact]
    public void 保存后能原样读回()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        var credential = SampleCredential();

        store.Save(credential);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(credential.CodeText, loaded!.CodeText);
        Assert.Equal(credential.FirstActivatedUtc, loaded.FirstActivatedUtc);
        Assert.Equal(credential.LastSeenUtc, loaded.LastSeenUtc);
    }

    [Fact]
    public void 文件不存在时返回空而不是抛异常()
    {
        using var dir = new TempDirectory();

        Assert.Null(CreateStore(dir).Load());
        Assert.False(CreateStore(dir).Exists);
    }

    [Fact]
    public void 换一台机器读不出来()
    {
        using var dir = new TempDirectory();
        CreateStore(dir).Save(SampleCredential());

        // 机器码变了，派生出的密钥就不同，解密必然失败
        var otherMachine = CreateStore(dir, "00000000-0000-0000-0000-000000000000");

        Assert.Null(otherMachine.Load());
    }

    [Fact]
    public void 磁盘上不出现授权码明文()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        store.Save(SampleCredential());

        var raw = File.ReadAllBytes(store.FilePath);
        var asText = Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain(LicenseFixtures.GoldenCode, asText, StringComparison.Ordinal);
        // 也顺手确认时间戳没有以明文形式躺着
        Assert.DoesNotContain("2026", asText, StringComparison.Ordinal);
    }

    [Fact]
    public void 文件被截断时按未激活处理()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        store.Save(SampleCredential());

        var raw = File.ReadAllBytes(store.FilePath);
        File.WriteAllBytes(store.FilePath, raw[..10]);

        Assert.Null(store.Load());
    }

    [Fact]
    public void 文件魔数被改时按未激活处理()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        store.Save(SampleCredential());

        var raw = File.ReadAllBytes(store.FilePath);
        raw[0] = 0x00;
        File.WriteAllBytes(store.FilePath, raw);

        Assert.Null(store.Load());
    }

    [Fact]
    public void 密文被改一个字节时按未激活处理()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        store.Save(SampleCredential());

        var raw = File.ReadAllBytes(store.FilePath);
        raw[^1] ^= 0x01; // 改密文最后一个字节，AES-GCM 的 tag 校验会失败
        File.WriteAllBytes(store.FilePath, raw);

        Assert.Null(store.Load());
    }

    [Fact]
    public void 覆盖保存后读到的是新内容()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        store.Save(SampleCredential());

        var updated = SampleCredential() with { LastSeenUtc = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) };
        store.Save(updated);

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal(updated.LastSeenUtc, loaded!.LastSeenUtc);
    }

    [Fact]
    public void 删除后文件不再存在()
    {
        using var dir = new TempDirectory();
        var store = CreateStore(dir);
        store.Save(SampleCredential());

        store.Delete();

        Assert.False(store.Exists);
        Assert.Null(store.Load());
    }

    [Fact]
    public void 迭代次数不同就解不开()
    {
        using var dir = new TempDirectory();
        var path = dir.Combine("license.dat");
        new LicenseStore(path, MachineCode, new TestLogger(), 1000).Save(SampleCredential());

        var different = new LicenseStore(path, MachineCode, new TestLogger(), 2000);

        Assert.Null(different.Load());
    }

    [Theory]
    [InlineData("", MachineCode)]
    [InlineData("   ", MachineCode)]
    [InlineData("license.dat", "")]
    [InlineData("license.dat", "  ")]
    public void 路径或机器码为空时构造即失败(string path, string machineCode)
    {
        Assert.Throws<ArgumentException>(
            () => new LicenseStore(path, machineCode, new TestLogger(), FastIterations));
    }

    [Fact]
    public void 保存时会自动建目录()
    {
        using var dir = new TempDirectory();
        var nested = dir.Combine("a", "b", "license.dat");
        var store = new LicenseStore(nested, MachineCode, new TestLogger(), FastIterations);

        store.Save(SampleCredential());

        Assert.True(File.Exists(nested));
    }
}
