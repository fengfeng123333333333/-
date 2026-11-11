namespace FbAuto.Core.Abstractions;

/// <summary>
/// 机器码。原版是完整 5 段 GUID 形式（8-4-4-4-12），此处保持一致，便于与原版对照。
/// </summary>
public interface IMachineCodeProvider
{
    /// <summary>取本机机器码；实现必须保证同一台机器每次结果相同。</summary>
    string GetMachineCode();
}
