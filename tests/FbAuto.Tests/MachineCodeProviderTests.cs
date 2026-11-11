using FbAuto.Infrastructure.System;

namespace FbAuto.Tests;

/// <summary>
/// 冒烟测试 3：机器码格式与稳定性。
/// 授权租约绑机器码，格式一变原有的激活记录全部作废，所以这里盯死 8-4-4-4-12 大写 GUID。
/// </summary>
public sealed class MachineCodeProviderTests
{
    [Fact]
    public void 机器码是八四四四十二位的大写GUID()
    {
        var code = new MachineCodeProvider().GetMachineCode();

        Assert.Matches(
            "^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$",
            code);
        Assert.Equal(36, code.Length);
        Assert.NotEqual(Guid.Empty.ToString("D").ToUpperInvariant(), code);
    }

    [Fact]
    public void 同一实例多次调用结果恒定()
    {
        var provider = new MachineCodeProvider();

        var first = provider.GetMachineCode();
        var second = provider.GetMachineCode();

        Assert.Equal(first, second);
    }

    [Fact]
    public void 不同实例在同一台机器上结果一致()
    {
        // 授权服务按机器码发租约，进程重启后拿到不同机器码等于每隔一次就要重新激活
        var a = new MachineCodeProvider().GetMachineCode();
        var b = new MachineCodeProvider().GetMachineCode();

        Assert.Equal(a, b);
    }

    [Fact]
    public void 机器码可被授权服务解析回GUID()
    {
        var code = new MachineCodeProvider().GetMachineCode();

        Assert.True(Guid.TryParse(code, out var parsed));
        Assert.Equal(code, parsed.ToString("D").ToUpperInvariant());
    }
}
