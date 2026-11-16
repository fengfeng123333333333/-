using FbAuto.Automation.Profiles;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 类别值的解析：配置里留空时到底算「跳过」还是算「按原版戏剧改」。
/// </summary>
/// <remarks>
/// 这个判断很容易写反，而写反的表现**两个方向都是静默的**：
/// <list type="bullet">
/// <item>写成「留空即跳过」：勾了「修改类别」的任务永远不改类别，界面和日志都看不出异常；</item>
/// <item>写成「留空用戏剧」但原版其实会跳过：所有账号被改成戏剧，这是不可逆的账号级改动。</item>
/// </list>
/// 所以它必须由用例钉死，不能只靠注释。
/// </remarks>
public sealed class CategoryEditorTests
{
    /// <summary>
    /// 原版把类别写死在代码里（<c>has-text('戏剧')</c>、<c>未找到【戏剧】选项</c>、
    /// <c>已选择【戏剧】</c>），两份 settings.json 的 22 个键里都没有类别。
    /// 谁把默认值改掉，行为就跟原版不一致了 —— 这条盯着它。
    /// </summary>
    [Fact]
    public void 原版默认类别是戏剧()
    {
        Assert.Equal("戏剧", CategoryEditor.DefaultCategory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void 留空按原版默认类别处理而不是跳过(string? configured)
    {
        Assert.Equal("戏剧", CategoryEditor.ResolveTarget(configured));
    }

    [Fact]
    public void 填了就用填的值()
    {
        Assert.Equal("教育", CategoryEditor.ResolveTarget("教育"));
    }

    [Fact]
    public void 首尾空白被去掉()
    {
        // 从别处复制粘贴常带首尾空格，直接填进 FB 的搜索框会搜不到选项，
        // 表现为「未找到【戏剧 】选项」这种看着一模一样却匹配不上的怪现象。
        Assert.Equal("戏剧", CategoryEditor.ResolveTarget("  戏剧  "));
    }

    [Fact]
    public void 只有中间空格的类别值原样保留()
    {
        // 类别名内部有空格是可能的（FB 上确实有「电视节目」这类多词类别），
        // 不能顺手把所有空白都吃掉。
        Assert.Equal("电视 节目", CategoryEditor.ResolveTarget("电视 节目"));
    }

    /// <summary>
    /// 执行器与编辑器两层语义不同，不能混：
    /// <see cref="CategoryEditor.ResolveTarget"/> 负责「配置空 → 戏剧」，
    /// <see cref="CategoryEditor.RunAsync"/> 收到空仍然跳过（防御直接调用方）。
    /// 这条保证改动 ResolveTarget 时没有顺手把 RunAsync 的守卫删掉。
    /// </summary>
    [Fact]
    public async Task 直接调用编辑器时空类别仍然跳过()
    {
        var page = new FakePageInterop();

        var result = await CategoryEditor.RunAsync(page, "100012345678", category: "  ");

        Assert.Equal(FieldEditStatus.Skipped, result.Status);
        Assert.Empty(page.NavigatedUrls);
    }
}
