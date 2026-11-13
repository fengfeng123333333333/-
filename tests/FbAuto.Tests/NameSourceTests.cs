using FbAuto.Automation.Profiles;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 主页名称文件的读取与分配。
/// </summary>
/// <remarks>
/// 重点只有一个：**挑哪一行由别名决定，不是永远第一行**。
/// 执行器逐任务跑，手上只有当前账号一个别名；若实现写成「取第一行」，
/// 单账号试跑时完全正常，一上批量就变成所有账号改成同一个名字 ——
/// 头像图片踩过同一个坑，所以这里直接把「不同别名会挑到不同行」钉住。
/// </remarks>
public sealed class NameSourceTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string WriteNames(params string[] lines)
    {
        var path = _dir.Combine("names.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void 没配路径时没有名字()
    {
        var selection = NameSource.Pick(null, "acc01");

        Assert.False(selection.Found);
        Assert.Contains("没有配置主页名称文件", selection.LogLines[0]);
    }

    [Fact]
    public void 文件不存在时没有名字()
    {
        var selection = NameSource.Pick(_dir.Combine("missing.txt"), "acc01");

        Assert.False(selection.Found);
        Assert.Contains("不存在", selection.LogLines[0]);
    }

    [Fact]
    public void 只有空行与注释时没有名字()
    {
        var path = WriteNames("", "   ", "# acc01 改过名，跳过", "");

        var selection = NameSource.Pick(path, "acc01");

        Assert.False(selection.Found);
        Assert.Contains("没有可用名字", selection.LogLines[0]);
    }

    [Fact]
    public void 正常文件会挑到其中一个并说明数量()
    {
        var path = WriteNames("名字甲", "名字乙", "名字丙");

        var selection = NameSource.Pick(path, "acc01");

        Assert.True(selection.Found);
        Assert.Contains(selection.Name, new[] { "名字甲", "名字乙", "名字丙" });
        Assert.Contains("3 个名字", selection.LogLines[0]);
        Assert.Contains(selection.Name!, selection.LogLines[0]);
    }

    [Fact]
    public void 行的首尾空白与BOM被清掉()
    {
        var path = WriteNames("\uFEFF  名字甲  ", "名字乙");

        var selection = NameSource.Pick(path, "acc01");

        Assert.NotNull(selection.Name);
        // 用 char 的 Contains 而不是 Assert.DoesNotContain(string, string)：
        // xunit 那个重载走**文化相关**比较，而 U+FEFF（零宽不换行空格）在文化比较里是
        // 零权重字符，会被忽略 —— 于是它对任何字符串都报「在位置 0 找到」，
        // 明明干净的字符串也会红。这个坑值得记一笔：不是数据脏，是断言选错了比较方式。
        Assert.False(selection.Name!.Contains('\uFEFF'));
        Assert.Equal(selection.Name!.Trim(), selection.Name);
    }

    [Fact]
    public void 同一账号每次挑到同一个名字()
    {
        var path = WriteNames("甲", "乙", "丙", "丁");

        var first = NameSource.Pick(path, "acc01").Name;
        var second = NameSource.Pick(path, "acc01").Name;

        // 断点续跑、重跑同一条任务时名字不能变，否则页面上会看到「上一次改成这个、这一次又改回去」。
        Assert.Equal(first, second);
    }

    [Fact]
    public void 不同账号不会全挑到第一行()
    {
        var path = WriteNames("甲", "乙", "丙", "丁");

        var picked = Enumerable.Range(1, 20)
            .Select(i => NameSource.Pick(path, $"acc{i:00}").Name)
            .Distinct()
            .Count();

        // 不要求「互不相同」（哈希会撞），要求的是「不退化成一」。只取第一行的实现这里必红。
        Assert.True(picked >= 3, $"20 个账号只挑到 {picked} 种名字，等于按序号取第一行");
    }
}
