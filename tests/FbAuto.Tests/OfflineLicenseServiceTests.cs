using FbAuto.Core.Licensing;
using FbAuto.Licensing;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 离线授权服务。这里覆盖的是「用户实际会遇到的每一种状态」：
/// 没激活、激活成功、码不对、过期、回拨时间、重装后重新激活。
/// </summary>
public class OfflineLicenseServiceTests
{
    private const string MachineCode = "6AD79298-1111-2222-3333-444455556666";
    private const int FastIterations = 1000;

    private static readonly DateTimeOffset Start = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        OfflineLicenseService Service,
        LicenseStore Store,
        FakeTimeProvider Time,
        TestLogger Logger);

    private static Harness Create(TempDirectory dir, DateTimeOffset? start = null, string machineCode = MachineCode)
    {
        var logger = new TestLogger();
        var store = new LicenseStore(dir.Combine("license.dat"), machineCode, logger, FastIterations);
        var time = new FakeTimeProvider(start ?? Start);
        var service = new OfflineLicenseService(store, logger, time);
        return new Harness(service, store, time, logger);
    }

    [Fact]
    public void 没有凭证时处于未激活状态()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        Assert.Equal(LicenseState.NotActivated, h.Service.State);
        Assert.False(h.Service.HasLocalLicenseKey);
        Assert.Null(h.Service.ExpiresAt);
        Assert.Null(h.Service.DaysRemaining);
    }

    [Fact]
    public async Task 激活金样本成功且剩余三十天()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        var result = await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        Assert.True(result.IsActive);
        Assert.Equal(LicenseState.Active, h.Service.State);
        Assert.Equal(30, h.Service.DaysRemaining);
        Assert.True(h.Service.HasLocalLicenseKey);
        Assert.NotNull(h.Service.ExpiresAt);
        Assert.Equal(Start.UtcDateTime.AddDays(30), h.Service.ExpiresAt!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task 激活后重启仍然是已激活()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        // 新建一个 service 相当于重启程序：只从磁盘读凭证
        var restarted = Create(dir, Start.AddHours(1));

        Assert.Equal(LicenseState.Active, restarted.Service.State);
        Assert.Equal(30, restarted.Service.DaysRemaining);
    }

    [Fact]
    public async Task 激活乱码不会改变状态也不会写凭证()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        var result = await h.Service.ActivateAsync("这不是授权码");

        Assert.False(result.IsActive);
        Assert.Equal(LicenseState.NotActivated, result.State);
        Assert.False(h.Service.HasLocalLicenseKey);
        Assert.False(h.Store.Exists);

        // 长度先于字符检查，所以这里给的是「长度不对」；关键是提示可读、且没有落盘
        Assert.Contains("长度不对", result.Message);
        Assert.True(
            h.Logger.Contains("WRN", "激活失败"),
            "激活失败必须留下日志，否则用户报障时无从查起");
    }

    [Fact]
    public async Task 激活长度不对的码给出重抄提示()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        var result = await h.Service.ActivateAsync(LicenseFixtures.GoldenCode[..^3]);

        Assert.False(result.IsActive);
        Assert.Contains("长度不对", result.Message);
    }

    [Fact]
    public async Task 激活空串时提示先输入()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        var result = await h.Service.ActivateAsync("   ");

        Assert.False(result.IsActive);
        Assert.Contains("请先输入", result.Message);
    }

    [Fact]
    public async Task 别人签的码被拒绝且不落盘()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        // 用另一把钥匙签一张结构完全合法的码
        using var otherKey = FbAuto.Keygen.PrivateKeyVault.CreateNew();
        var foreign = FbAuto.Keygen.CodeIssuer.Issue(
            otherKey, new DateOnly(2026, 11, 12), 30, serial: 999);
        var text = LicenseCodeCodec.Encode(foreign);

        var result = await h.Service.ActivateAsync(text);

        Assert.False(result.IsActive);
        Assert.Contains("校验失败", result.Message);
        Assert.False(h.Store.Exists);
    }

    [Fact]
    public async Task 到期前一天仍然可用()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        h.Time.Advance(TimeSpan.FromDays(29));
        h.Service.Reload();

        Assert.Equal(LicenseState.Active, h.Service.State);
        Assert.Equal(1, h.Service.DaysRemaining);
    }

    [Fact]
    public async Task 满三十天即过期()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        h.Time.Advance(TimeSpan.FromDays(30));
        h.Service.Reload();

        Assert.Equal(LicenseState.Expired, h.Service.State);
        Assert.Equal(0, h.Service.DaysRemaining);
    }

    [Fact]
    public async Task 过期后继续推进时间也不会复活()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        h.Time.Advance(TimeSpan.FromDays(400));
        h.Service.Reload();

        Assert.Equal(LicenseState.Expired, h.Service.State);
    }

    [Fact]
    public async Task 回拨系统时间不能延长有效期()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        // 正常用了 20 天
        h.Time.Advance(TimeSpan.FromDays(20));
        h.Service.TouchLastSeen();

        // 用户把系统时间调回激活当天，企图再白用一个月
        h.Time.SetUtcNow(Start);
        h.Service.Reload();

        // 剩余天数按「见过的最晚时间」算，仍然是 10 天，没有被回拨骗到
        Assert.Equal(LicenseState.Active, h.Service.State);
        Assert.Equal(10, h.Service.DaysRemaining);
    }

    [Fact]
    public async Task 回拨时间超过有效期也不能复活()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        h.Time.Advance(TimeSpan.FromDays(31));
        h.Service.TouchLastSeen();

        h.Time.SetUtcNow(Start.AddDays(1)); // 回拨到有效期内
        h.Service.Reload();

        // 到期判定用的是历史最晚时间，已经过了 30 天，回拨也救不回来
        Assert.Equal(LicenseState.Expired, h.Service.State);
    }

    [Fact]
    public async Task 同一张码重复激活不重置首次激活时间()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);
        var firstExpiry = h.Service.ExpiresAt;

        h.Time.Advance(TimeSpan.FromDays(5));
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        // 重装一次就能白续一个月，是离线授权最容易被钻的洞，这里堵上
        Assert.Equal(firstExpiry, h.Service.ExpiresAt);
        Assert.Equal(25, h.Service.DaysRemaining);
    }

    [Fact]
    public async Task 换一张新码时有效期重新计算()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        h.Time.Advance(TimeSpan.FromDays(5));
        var result = await h.Service.ActivateAsync(LicenseFixtures.SecondCode);

        Assert.True(result.IsActive);
        Assert.Equal(30, h.Service.DaysRemaining);
        Assert.Equal(Start.UtcDateTime.AddDays(5 + 30), h.Service.ExpiresAt!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task 凭证内容被篡改成别的码时回到未激活()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        // 手工写入一个「能解密、但签名对不上」的凭证，模拟有人改了凭证里的码
        var tamperedCode = "A" + LicenseFixtures.GoldenCode[1..];
        h.Store.Save(new LicenseCredential(
            tamperedCode, Start.UtcDateTime, Start.UtcDateTime));

        h.Service.Reload();

        Assert.Equal(LicenseState.NotActivated, h.Service.State);
    }

    [Fact]
    public async Task 换机器后凭证解不开按未激活处理()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);
        await h.Service.ActivateAsync(LicenseFixtures.GoldenCode);

        var otherMachine = Create(dir, Start.AddDays(1), "00000000-0000-0000-0000-000000000000");

        Assert.Equal(LicenseState.NotActivated, otherMachine.Service.State);
    }

    [Fact]
    public async Task 带连字符粘贴的码同样能激活()
    {
        using var dir = new TempDirectory();
        var h = Create(dir);

        var grouped = LicenseCodeCodec.ToGrouped(LicenseFixtures.GoldenCode);
        var result = await h.Service.ActivateAsync(grouped);

        Assert.True(result.IsActive);
    }
}
