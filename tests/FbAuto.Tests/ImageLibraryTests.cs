using FbAuto.Automation.Media;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 图片目录扫描与账号级分配。
/// 分配的核心约束来自原版提示：「头像和背景图在同一账号内使用同一张随机图片，不同账号随机不同」——
/// 两句话一条都不能少，少第一条就会同账号头像背景不一致，少第二条就是所有账号一张图（更容易被关联）。
/// </summary>
public sealed class ImageLibraryTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Touch(string fileName, int bytes = 64)
    {
        var path = _temp.Combine(fileName);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    // ---------- 目录扫描 ----------

    [Fact]
    public void 白名单扩展名都被扫描到()
    {
        Touch("a.png");
        Touch("b.jpg");
        Touch("c.jpeg");
        Touch("d.webp");

        var scan = ImageDirectoryScanner.Scan(_temp.Path);

        Assert.True(scan.IsUsable);
        Assert.Equal(4, scan.Count);
        Assert.Null(scan.Failure);
    }

    [Fact]
    public void 扩展名不区分大小写()
    {
        Touch("UPPER.PNG");
        Touch("Mixed.JpG");

        var scan = ImageDirectoryScanner.Scan(_temp.Path);

        Assert.Equal(2, scan.Count);
    }

    [Fact]
    public void 空文件被跳过并说明原因()
    {
        Touch("ok.png");
        Touch("broken.png", bytes: 0);

        var scan = ImageDirectoryScanner.Scan(_temp.Path);

        Assert.Single(scan.UsableFiles);
        var skip = Assert.Single(scan.Skipped);
        Assert.Contains("broken.png", skip.Path);
        Assert.Contains("0 字节", skip.Reason);
    }

    [Fact]
    public void 非图片文件静默忽略_不刷警告()
    {
        Touch("ok.png");
        Touch("readme.txt");
        Touch("video.mp4");

        var scan = ImageDirectoryScanner.Scan(_temp.Path);

        Assert.Single(scan.UsableFiles);
        Assert.Empty(scan.Skipped); // 目录里放个说明文件是很正常的事，不该报警
    }

    [Fact]
    public void 结果按文件名排序_与创建顺序无关()
    {
        Touch("z.png");
        Touch("a.png");
        Touch("m.png");

        var scan = ImageDirectoryScanner.Scan(_temp.Path);

        Assert.Equal(["a.png", "m.png", "z.png"], scan.UsableFiles.Select(Path.GetFileName));
    }

    [Fact]
    public void 只有空文件时_整体不可用()
    {
        Touch("empty.png", bytes: 0);

        var scan = ImageDirectoryScanner.Scan(_temp.Path);

        Assert.False(scan.IsUsable);
        Assert.Contains("没有可用图片", scan.Failure);
        Assert.Contains(".webp", scan.Failure); // 提示里要写清支持什么格式
    }

    [Fact]
    public void 目录不存在_说清路径()
    {
        var missing = Path.Combine(_temp.Path, "no-such-dir");

        var scan = ImageDirectoryScanner.Scan(missing);

        Assert.False(scan.IsUsable);
        Assert.Contains("不存在", scan.Failure);
        Assert.Contains("no-such-dir", scan.Failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 未配置目录_给出配置提示(string? directory)
    {
        var scan = ImageDirectoryScanner.Scan(directory);

        Assert.False(scan.IsUsable);
        Assert.Contains("未配置图片目录", scan.Failure);
    }

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.PNG", true)]
    [InlineData("a.jpg", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.gif", false)]
    [InlineData("a.mp4", false)]
    [InlineData("a", false)]
    [InlineData("", false)]
    public void 扩展名判定(string path, bool expected)
    {
        Assert.Equal(expected, ImageDirectoryScanner.IsSupportedExtension(path));
    }

    // ---------- 账号分配 ----------

    [Fact]
    public void 图片够时不同账号拿到不同图片()
    {
        var files = new[] { "a.png", "b.png", "c.png" };

        var plan = AccountImagePlan.Create(["acc01", "acc02", "acc03"], files, seed: 0);

        Assert.Equal(3, plan.Assignments.Count);
        Assert.Equal(3, plan.DistinctImageCount); // 「不同账号随机不同」
    }

    [Fact]
    public void 同一账号反复取_永远是同一张()
    {
        var plan = AccountImagePlan.Create(["acc01", "acc02"], ["a.png", "b.png", "c.png"], seed: 0);

        var first = plan.Pick("acc01");

        Assert.NotNull(first);
        Assert.Equal(first, plan.Pick("acc01"));
        Assert.Equal(first, plan.PickForAvatar("acc01"));
        Assert.Equal(first, plan.PickForCover("acc01"));
    }

    [Fact]
    public void 头像与背景图必须同图()
    {
        var plan = AccountImagePlan.Create(["acc01"], ["a.png", "b.png"], seed: 3);

        Assert.Equal(plan.PickForAvatar("acc01"), plan.PickForCover("acc01"));
    }

    [Fact]
    public void 同一个种子_分配完全可复现()
    {
        string[] aliases = ["acc01", "acc02", "acc03"];
        string[] files = ["a.png", "b.png", "c.png", "d.png"];

        var first = AccountImagePlan.Create(aliases, files, seed: 42);
        var second = AccountImagePlan.Create(aliases, files, seed: 42);

        foreach (var alias in aliases)
        {
            Assert.Equal(first.Pick(alias), second.Pick(alias));
        }
    }

    [Fact]
    public void 图片传入顺序不影响分配结果()
    {
        string[] aliases = ["acc01", "acc02"];
        var forward = AccountImagePlan.Create(aliases, ["a.png", "b.png", "c.png"], seed: 5);
        var reversed = AccountImagePlan.Create(aliases, ["c.png", "b.png", "a.png"], seed: 5);

        Assert.Equal(forward.Pick("acc01"), reversed.Pick("acc01"));
        Assert.Equal(forward.Pick("acc02"), reversed.Pick("acc02"));
    }

    [Fact]
    public void 只有一张图_所有账号同图并给出警告()
    {
        var plan = AccountImagePlan.Create(["acc01", "acc02"], ["only.png"], seed: 0);

        Assert.Equal("only.png", plan.Pick("acc01"));
        Assert.Equal("only.png", plan.Pick("acc02"));
        Assert.Equal(1, plan.DistinctImageCount);
        Assert.Contains(plan.Warnings, w => w.Contains("只有 1 张"));
    }

    [Fact]
    public void 账号多于图片_重复并明确告知几个账号重复()
    {
        var plan = AccountImagePlan.Create(["a1", "a2", "a3", "a4"], ["x.png", "y.png"], seed: 1);

        Assert.Equal(4, plan.Assignments.Count);
        Assert.Equal(2, plan.DistinctImageCount);
        Assert.Contains(plan.Warnings, w => w.Contains("2 个账号重复"));
    }

    [Fact]
    public void 没有账号_空计划并警告()
    {
        var plan = AccountImagePlan.Create([], ["a.png"], seed: 0);

        Assert.True(plan.IsEmpty);
        Assert.Contains(plan.Warnings, w => w.Contains("没有账号"));
    }

    [Fact]
    public void 没有图片_空计划并警告()
    {
        var plan = AccountImagePlan.Create(["acc01"], [], seed: 0);

        Assert.True(plan.IsEmpty);
        Assert.Contains(plan.Warnings, w => w.Contains("没有可用图片"));
    }

    [Fact]
    public void 重复别名只算一次()
    {
        var plan = AccountImagePlan.Create(["acc01", "acc01", "acc02"], ["a.png", "b.png"], seed: 0);

        Assert.Equal(2, plan.Assignments.Count);
    }

    [Fact]
    public void 取不在计划里的账号返回null()
    {
        var plan = AccountImagePlan.Create(["acc01"], ["a.png"], seed: 0);

        Assert.Null(plan.Pick("acc99"));
        Assert.Null(plan.Pick(null));
        Assert.Null(plan.Pick("  "));
    }

    [Fact]
    public void 洗牌后不是简单按顺序取第一张()
    {
        // 若实现退化成「第 i 个账号取第 i 张」，10 张图时 5 个账号全拿到前 5 张，
        // 这里用「至少有一个账号拿到的不是它下标对应那张」来验证真的洗过牌。
        string[] files = Enumerable.Range(0, 10).Select(i => $"img{i}.png").ToArray();
        string[] aliases = Enumerable.Range(0, 5).Select(i => $"acc{i}").ToArray();

        var plan = AccountImagePlan.Create(aliases, files, seed: 7);

        var inOrder = aliases.Select((a, i) => plan.Pick(a) == files[i]).All(x => x);
        Assert.False(inOrder, "分配结果与原始顺序完全一致，洗牌可能没有生效");
    }
}
