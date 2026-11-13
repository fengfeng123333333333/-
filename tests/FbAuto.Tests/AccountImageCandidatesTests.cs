using FbAuto.Automation.Media;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 账号选图：把目录扫描结果变成「本账号那张在最前 + 其余作备选」。
/// </summary>
/// <remarks>
/// 这一层不起浏览器也不读像素，只做顺序与分配。它压的是两件容易想当然写错的事：
/// <list type="number">
/// <item>候选顺序 —— 第一张必须是本账号应得的那张，否则所有账号头像会一样。</item>
/// <item>目录不可用时返回空 —— 空会让流程跳过这两项，而不是拿一张不存在的图去注入。</item>
/// </list>
/// </remarks>
public sealed class AccountImageCandidatesTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private ImageDirectoryScan ScanOf(params string[] names)
    {
        foreach (var name in names)
        {
            ImageFixtures.WritePng(_dir.Path, name, 400, 400);
        }

        return ImageDirectoryScanner.Scan(_dir.Path);
    }

    [Fact]
    public void 目录不可用时返回空选择()
    {
        var scan = ImageDirectoryScanner.Scan(_dir.Combine("不存在"));

        var selection = AccountImageCandidates.Build("acc01", scan);

        Assert.True(selection.IsEmpty);
        Assert.Null(selection.Primary);
        Assert.Equal(0, selection.AlternateCount);
    }

    [Fact]
    public void 第一张是本账号应得的那张其余作备选()
    {
        var scan = ScanOf("a.png", "b.png", "c.png");

        var selection = AccountImageCandidates.Build("acc02", scan);

        Assert.Equal(3, selection.Candidates.Count);
        Assert.Contains(selection.Primary, scan.UsableFiles);

        // 备选里不能再出现第一张 —— 否则「换下一张」会先重试同一张，
        // 白白消耗一次尝试次数（上限只有 3 次）。
        Assert.DoesNotContain(selection.Primary, selection.Candidates.Skip(1));
        Assert.Equal(selection.Candidates.Count, selection.Candidates.Distinct().Count());
    }

    [Fact]
    public void 同一账号每次得到的顺序完全一样()
    {
        var scan = ScanOf("a.png", "b.png", "c.png");

        var first = AccountImageCandidates.Build("acc03", scan);
        var second = AccountImageCandidates.Build("acc03", scan);

        Assert.Equal(first.Candidates, second.Candidates);
    }

    /// <summary>
    /// 不同账号不能全部拿到同一张。
    /// </summary>
    /// <remarks>
    /// 这条是原版提示「不同账号随机不同」的最小验证，也是**真的抓到过 bug** 的一条：
    /// 第一版直接用 <c>AccountImagePlan.Create([alias], files)</c>，它按「别名在列表里的下标」取图，
    /// 而执行器每次只有一个别名、下标恒为 0 —— 结果所有账号都拿到洗牌后的第一张，
    /// 也就是头像全一样。改成「种子 = 别名的稳定哈希」之后才不同。
    ///
    /// 只断言「不是全都一样」而不是「四个全不同」：哈希会撞（图片 4 张、账号 4 个时实测 4 个里有 3 种），
    /// 把它写成「全不同」等于给未来埋一条随机变红的用例。真正保证互不相同的是批量分配，
    /// 见 <see cref="批量分配时前N个账号互不相同"/>。
    /// </remarks>
    [Fact]
    public void 不同账号不应全部拿到同一张()
    {
        var scan = ScanOf("a.png", "b.png", "c.png", "d.png");

        var primaries = new[] { "acc01", "acc02", "acc03", "acc04" }
            .Select(alias => AccountImageCandidates.Build(alias, scan).Primary)
            .ToArray();

        Assert.True(primaries.Distinct().Count() > 1, $"四个账号都拿到了 {primaries[0]}");
    }

    /// <summary>
    /// 一次拿到全部别名时，前 min(账号数, 图片数) 个账号互不相同。
    /// </summary>
    /// <remarks>
    /// 这条压的是 <see cref="AccountImagePlan"/> 原本的承诺，用来对照「逐任务分配只能做到大概率不同」。
    /// 将来若给执行器接一个「整批传别名」的入口，就该走这条路径而不是逐任务那条。
    /// </remarks>
    [Fact]
    public void 批量分配时前N个账号互不相同()
    {
        var scan = ScanOf("a.png", "b.png", "c.png", "d.png");
        string[] aliases = ["acc01", "acc02", "acc03", "acc04"];

        var plan = AccountImagePlan.Create(aliases, scan.UsableFiles);

        Assert.Equal(4, aliases.Select(a => plan.Pick(a)).Distinct().Count());
    }

    [Fact]
    public void 只有一张图时给出告警且候选只有一张()
    {
        var scan = ScanOf("only.png");

        var selection = AccountImageCandidates.Build("acc01", scan);

        Assert.Single(selection.Candidates);
        Assert.Equal(0, selection.AlternateCount);
        Assert.Contains(selection.Warnings, w => w.Contains("只有 1 张"));
    }

    [Fact]
    public void 别名缺失时仍然分到一张图而不是空()
    {
        var scan = ScanOf("a.png", "b.png");

        var selection = AccountImageCandidates.Build(null, scan);

        // 「账号清单少了一列」不能表现成「头像莫名不换了」。
        Assert.False(selection.IsEmpty);
        Assert.Contains(selection.Primary, scan.UsableFiles);
    }
}
