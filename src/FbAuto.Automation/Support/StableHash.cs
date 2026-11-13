namespace FbAuto.Automation.Support;

/// <summary>
/// 稳定哈希：同一个字符串在任何进程、任何机器上都算出同一个值。
/// </summary>
/// <remarks>
/// 专门存在一个类型里，是因为它解决的是同一类麻烦：
/// 「按名字分配资源」在**逐任务执行**时只能看到自己那一个名字，
/// 用列表下标会恒为 0（所有账号拿到同一份），用 <c>string.GetHashCode()</c> 又每进程不同
/// （断点续跑会换一份）。两处都踩过：头像图片与主页名字。
///
/// 实现是 FNV-1a 32 位，结果取低 31 位保证非负：取绝对值会让 <c>int.MinValue</c> 溢出，
/// 而取模运算在负数上又是另一套规则，索性一开始就只留非负。
/// </remarks>
public static class StableHash
{
    public static int Of(string? value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty))
        {
            hash ^= b;
            hash *= prime;
        }

        return unchecked((int)(hash & 0x7FFF_FFFF));
    }
}
