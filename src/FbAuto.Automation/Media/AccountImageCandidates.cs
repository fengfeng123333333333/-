namespace FbAuto.Automation.Media;

/// <summary>一个账号的选图结果。</summary>
/// <param name="Candidates">候选图路径，**第一个是本账号应得的那张**，其余作换图备选。</param>
/// <param name="Warnings">值得记一笔的情况（只有一张图、账号数超过图片数等）。</param>
public sealed record AccountImageSelection(IReadOnlyList<string> Candidates, IReadOnlyList<string> Warnings)
{
    public static AccountImageSelection Empty { get; } = new([], []);

    /// <summary>本账号那张图；没有候选时为 null。</summary>
    public string? Primary => Candidates.Count == 0 ? null : Candidates[0];

    /// <summary>可换的备选数量（不含第一张）。</summary>
    public int AlternateCount => Candidates.Count == 0 ? 0 : Candidates.Count - 1;

    public bool IsEmpty => Candidates.Count == 0;
}

/// <summary>
/// 把「图片目录扫描结果」变成「这个账号的候选图列表」。
/// </summary>
/// <remarks>
/// 顺序有意义，所以单独一个类型而不是塞在执行器里：
/// <list type="number">
/// <item>第一位是**这个账号应得的那张**（<see cref="AccountImagePlan"/> 保证同账号同图、
/// 不同账号尽量不同）。</item>
/// <item>其余是换图备选 —— 页面判定尺寸不合格时依次换下一张，这正是原版
/// 「背景图尺寸不符合要求（太大或太小），将尝试其他图片」的意思。</item>
/// </list>
/// 若直接用「目录里第一个文件」，所有账号会拿到同一张图，
/// 与原版提示「不同账号随机不同」矛盾 —— 表现是一批账号头像全一样。
/// </remarks>
public static class AccountImageCandidates
{
    /// <summary>别名缺失时代替用的键。不能传空串 —— <see cref="AccountImagePlan"/> 会把空别名过滤掉。</summary>
    private const string DefaultAlias = "__default__";

    /// <summary>
    /// 生成候选列表。目录不可用时返回空选择（调用方据此跳过头像与背景图两项）。
    /// </summary>
    /// <param name="alias">账号别名。决定分到哪一张。</param>
    /// <param name="scan">目录扫描结果。</param>
    /// <remarks>
    /// **洗牌种子由别名算出来**，这一点和批量分配不同，原因是调用方式不同：
    /// <see cref="AccountImagePlan.Create"/> 的设计前提是「一次拿到全部账号」，
    /// 它按「别名在列表里的下标」取图，前 min(账号数, 图片数) 个保证互不相同。
    /// 但执行器是**逐任务**跑的，每次只知道自己那一个别名，下标永远是 0 ——
    /// 于是所有账号都会拿到洗牌后的第一张，也就是头像全一样。
    /// 实测就是这么错的（用例「不同账号拿到不同的第一张」把它抓出来了）。
    ///
    /// 改成「种子 = 别名的稳定哈希」之后：同别名恒定同一张（可复现、可断点续跑），
    /// 不同别名的洗牌顺序不同，第一张大概率不同。
    ///
    /// **代价要说清**：这是「大概率不同」而不是「保证不同」。哈希会撞，
    /// 严格不重复需要一次拿到全部别名（批量入口仍用 <see cref="AccountImagePlan.Create(IEnumerable{string}?, IReadOnlyList{string}?, int)"/>）。
    /// </remarks>
    public static AccountImageSelection Build(string? alias, ImageDirectoryScan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);

        if (!scan.IsUsable)
        {
            return AccountImageSelection.Empty;
        }

        // 别名缺失时不能直接返回空：那样「账号清单少了一列」会表现成「头像莫名不换了」。
        var key = string.IsNullOrWhiteSpace(alias) ? DefaultAlias : alias;
        var plan = AccountImagePlan.Create([key], scan.UsableFiles, StableSeed(key));
        var primary = plan.Pick(key);
        if (primary is null)
        {
            return new AccountImageSelection([], plan.Warnings);
        }

        var ordered = new List<string>(scan.Count) { primary };
        foreach (var file in scan.UsableFiles)
        {
            if (!string.Equals(file, primary, StringComparison.OrdinalIgnoreCase))
            {
                ordered.Add(file);
            }
        }

        return new AccountImageSelection(ordered, plan.Warnings);
    }

    /// <summary>
    /// 别名 → 洗牌种子。
    /// </summary>
    /// <remarks>
    /// 实现移到 <see cref="Support.StableHash"/>：主页名字也要按别名挑，
    /// 两处各写一份 FNV-1a 迟早在某一次修改后算出不同的值，表现为「同一账号的头像与名字对不上人」。
    /// 具体理由（为什么不能用 <c>string.GetHashCode()</c>）见那里。
    /// </remarks>
    private static int StableSeed(string value) => Support.StableHash.Of(value);
}
