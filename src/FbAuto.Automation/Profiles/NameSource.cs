using FbAuto.Automation.Support;

namespace FbAuto.Automation.Profiles;

/// <summary>为一个账号挑到的名字。</summary>
/// <param name="Name">要改成的名字；null 表示这一项跳过。</param>
/// <param name="LogLines">要写进任务日志的说明行。</param>
public sealed record NameSelection(string? Name, IReadOnlyList<string> LogLines)
{
    public static NameSelection Empty { get; } = new(null, []);

    public bool Found => !string.IsNullOrWhiteSpace(Name);
}

/// <summary>
/// 从主页名称文件里为一个账号挑一个名字。
/// </summary>
/// <remarks>
/// **按行分配，一个账号一个名字**。依据是原版界面只有一个「主页名称文件」选择框，
/// 而主页名字是逐账号改的（区别于签名：签名是整段 txt 一份，所有账号共用）。
/// 文件里一行一个名字，空行与 <c>#</c> 开头的行忽略 —— 后者是留给自己写备注的，
/// 名称文件是手工维护的，没有注释行迟早会有人在里面写「这个号改过名」。
///
/// **挑哪一个由别名的稳定哈希决定**，不是按顺序取第 N 个：
/// 执行器是逐任务跑的，手上只有当前账号一个别名，按序号取会永远是第一行 ——
/// 表现是「所有账号都改成同一个名字」，而这一点在单账号试跑时完全看不出来。
/// 头像图片踩过同一个坑（见 <c>AccountImageCandidates</c>）。
/// </remarks>
public static class NameSource
{
    /// <summary>
    /// 读文件并挑名字。<paramref name="path"/> 为空、文件不存在、文件里没有有效行时
    /// 都返回「没有名字」，由调用方记一句跳过。
    /// </summary>
    /// <param name="path">名称文件路径。</param>
    /// <param name="alias">账号别名，决定挑第几个。</param>
    public static NameSelection Pick(string? path, string? alias)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new NameSelection(null, ["没有配置主页名称文件，主页名字项将跳过"]);
        }

        if (!File.Exists(path))
        {
            return new NameSelection(null, [$"主页名称文件不存在（{path}），主页名字项将跳过"]);
        }

        string[] raw;
        try
        {
            raw = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            return new NameSelection(null, [$"读取主页名称文件失败（{ex.Message}），主页名字项将跳过"]);
        }

        // 去 BOM 残留、去空白、去注释行：文件是记事本手写的，这三种情况都遇到过。
        // BOM 用 Replace 而不是 TrimStart：记事本把 BOM 写在行首、某些编辑器会把它写在
        // 空白之后（`\uFEFF  名字`），TrimStart 只能去掉紧贴行首的那一个，去掉后残留的空格
        // 又会被写进页面上的名字里 —— 用例「行的首尾空白与BOM被清掉」就是这么红的。
        var names = raw
            .Select(line => line.Replace("\uFEFF", string.Empty, StringComparison.Ordinal).Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        if (names.Length == 0)
        {
            return new NameSelection(null, [$"主页名称文件里没有可用名字（{path}），主页名字项将跳过"]);
        }

        var index = StableHash.Of(alias) % names.Length;
        var picked = names[index];
        return new NameSelection(picked, [$"主页名称文件已加载: {path}（{names.Length} 个名字），本账号使用：{picked}"]);
    }
}
