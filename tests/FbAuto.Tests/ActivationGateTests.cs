using FbAuto.Core.Licensing;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 「开发期跳过激活」闸门的用例。
///
/// 背景：这个开关原本从 config.json 读、默认 true，发布包里就带着 true ——
/// 任何人拿到发布包，不输授权码就能用，签名、密钥、机器绑码全部作废。
/// 修法是把闸门放到编译期（Release 恒不允许），这里钉住三件事：
/// 判定逻辑本身、调用点没有绕开闸门、默认值没有又写成 true。
/// </summary>
public class ActivationGateTests
{
    [Fact]
    public void 发布构建即使配置写了跳过也不跳()
    {
        // buildAllowsSkip=false 就是 Release 构建。这条一旦变红，等于授权可以被
        // 「把 config.json 改成 true」绕过 —— 这是整个授权体系的地基。
        Assert.False(ActivationGate.ShouldSkip(configuredSkip: true, buildAllowsSkip: false));
    }

    [Fact]
    public void 调试构建且配置写了跳过才跳()
    {
        Assert.True(ActivationGate.ShouldSkip(configuredSkip: true, buildAllowsSkip: true));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void 配置没写跳过就不跳(bool configuredSkip, bool buildAllowsSkip)
    {
        Assert.False(ActivationGate.ShouldSkip(configuredSkip, buildAllowsSkip));
    }

    [Fact]
    public void 单参重载走的是当前构建的闸门而不是只看配置()
    {
        // 单参版本必须与「显式传入当前构建能力」的结果一致，
        // 否则 App 里那句调用可能悄悄退化成「只看配置」。
        Assert.Equal(
            ActivationGate.ShouldSkip(true, ActivationGate.BuildAllowsSkip),
            ActivationGate.ShouldSkip(true));
    }

    [Fact]
    public void 启动流程必须经由闸门判定而不是直接读配置()
    {
        var source = File.ReadAllText(
            RepoFiles.Resolve("src", "FbAuto.Presentation", "App.xaml.cs"));

        Assert.Contains("ActivationGate.ShouldSkip(", source, StringComparison.Ordinal);

        // 直接拿配置值当条件就是回退到修之前的写法：发布包里照样能跳过。
        Assert.DoesNotContain(
            "if (configService.Current.SkipActivationInDevelopment)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void 配置项的默认值不是跳过()
    {
        var source = File.ReadAllText(
            RepoFiles.Resolve("src", "FbAuto.Core", "Configuration", "AppConfig.cs"));

        // 默认 true 会让发布包首次生成的 config.json 里就写着「跳过激活」，
        // 即便代码不认这个键，也是给使用者一个错误的暗示，且容易被当成「功能」投诉。
        var line = source
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .FirstOrDefault(l => l.Contains("public bool SkipActivationInDevelopment", StringComparison.Ordinal));

        Assert.True(line is not null, "AppConfig 里没有 SkipActivationInDevelopment 的属性声明");

        // 初值器写在访问器后面：`{ get; set; } = true;`。
        // 第一版按「从属性名找到第一个分号」截断，结果截到的是 `{ get;` 里的分号，
        // `= true` 压根没进比对范围 —— 注入 G3 把这个假绿打出来。所以先把访问器剥掉。
        var withoutAccessors = line!
            .Replace("{ get; set; }", string.Empty, StringComparison.Ordinal)
            .Replace("{ get; init; }", string.Empty, StringComparison.Ordinal);

        Assert.DoesNotContain("=", withoutAccessors, StringComparison.Ordinal);
    }
}
