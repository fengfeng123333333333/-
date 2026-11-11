using System.Security.Cryptography;
using System.Text;
using FbAuto.Core.Abstractions;
using Microsoft.Win32;

namespace FbAuto.Infrastructure.System;

/// <summary>
/// 生成机器码。原版为 8-4-4-4-12 的 GUID 形式，此处保持同样格式，便于与原版输出对照。
/// 取 Windows MachineGuid 做 SHA256 后取前 16 字节，换机器即变、同机器恒定。
/// </summary>
public sealed class MachineCodeProvider : IMachineCodeProvider
{
    private string? _cached;

    public string GetMachineCode()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var seed = ReadMachineGuid() ?? $"{Environment.MachineName}|{Environment.UserName}|(no-machineguid)";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var guid = new Guid(hash.AsSpan(0, 16));
        _cached = guid.ToString("D").ToUpperInvariant();
        return _cached;
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
