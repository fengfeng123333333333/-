using System.Text.RegularExpressions;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 资料修改页的 XAML 绑定必须都能在 MainViewModel 上找到对应属性。
/// </summary>
/// <remarks>
/// 为什么专门为这件事写用例：**WPF 的绑定名写错是静默的** —— 不抛异常、不报编译错误，
/// 运行时只是「绑不上」：勾选框照样显示、点了没反应，保存时写回的还是旧值。
/// 这类错人工点界面时极难发现（看着都对），而它恰好是复制粘贴 14 个勾选框时最容易犯的。
///
/// 顺带钉住「控件还在」：只删控件不改用例的话，删掉的那一项会静默变成「永远不改」。
///
/// <para>
/// **不引用 FbAuto.Presentation**（测试项目刻意不依赖 WPF 层，好让整组用例无头运行），
/// 所以属性名从 ViewModel 源码里取，而不是反射。取法只有两条规则：
/// <c>[ObservableProperty] private T _name;</c> → <c>Name</c>，
/// 以及显式写的 <c>public T Name { get; }</c> / <c>public T Name =&gt;</c>。
/// </para>
/// </remarks>
public sealed class ProfileEditBindingTests
{
    /// <summary>资料修改页必须出现的绑定名（七项 × 两个通道）。</summary>
    private static readonly string[] SwitchBindings =
    [
        "EditCoverImage", "EditAvatar", "UpdatePostingSettings", "EditFollowersContent",
        "EditSignature", "EditCategory", "EditPageName",
        "BitEditCoverImage", "BitEditAvatar", "BitUpdatePostingSettings", "BitEditFollowersContent",
        "BitEditSignature", "BitEditCategory", "BitEditPageName",
    ];

    /// <summary>
    /// 结果保存目录的两个绑定。与开关分开列：它们绑的是文本框，
    /// 写错的表现是「填了目录不生效、任务结束后目录里什么都没有」，比勾选框更不显眼。
    /// </summary>
    private static readonly string[] ExportDirBindings =
    [
        "ProfileEditResultExportDir", "BitProfileEditResultExportDir",
    ];

    /// <summary>
    /// 资料修改页的「填值」类绑定：目标类别与两个通道各自的屏蔽词。
    /// </summary>
    /// <remarks>
    /// 这四个是专门补上的缺口：<c>ProfileCategory</c> / <c>BitProfileCategory</c> /
    /// <c>BitCommentsBlockWords</c> / 原先**只有配置键、没有界面入口**，
    /// 表现是「修改类别勾了却永远跳过」「比特版粉丝公开内容永远用默认词」。
    /// 列在这里的每一项都必须同时满足：控件在、属性在、保存时写回。
    /// </remarks>
    private static readonly string[] ValueBindings =
    [
        "ProfileCategory", "BitProfileCategory",
        "CommentsBlockWords", "BitCommentsBlockWords",
    ];

    [Fact]
    public void 资料修改页的绑定都对应ViewModel属性()
    {
        var block = ProfileEditTab();

        // {Binding Xxx} / {Binding Xxx, ...} —— 只取属性名，忽略后面的 UpdateSourceTrigger 之类。
        // 排除 Command="{Binding XxxCommand}"：命令是行为，不是配置值，
        // 既不该要求它「在 ViewModel 上有对应属性」，也不该要求它「写回 config.json」。
        // 这条是 S17 接上「选择...」按钮之后暴露的 —— 原来那一页一个 Command 绑定都没有。
        var bound = Regex.Matches(block, @"(?<!Command="")\{Binding\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(bound);

        var known = ViewModelPropertyNames();

        // 解析器自己先自证：一份正常的 ViewModel 不可能只剩几个属性。
        // 少了这句，正则哪天写崩了，这条用例会变成「什么都不检查还全绿」。
        Assert.True(known.Count > 25, $"从 MainViewModel 只解析出 {known.Count} 个属性，解析规则大概坏了");

        var missing = bound.Where(name => !known.Contains(name)).ToArray();
        Assert.True(
            missing.Length == 0,
            $"资料修改页里这些绑定在 MainViewModel 上没有对应属性：{string.Join("、", missing)}");

        foreach (var expected in SwitchBindings)
        {
            Assert.True(
                bound.Contains(expected, StringComparer.Ordinal),
                $"资料修改页少了 {expected} 这个绑定 —— 控件被删了，这一项会永远改不了");
            Assert.True(
                known.Contains(expected),
                $"MainViewModel 上没有 {expected} 属性");
        }

        foreach (var expected in ExportDirBindings)
        {
            Assert.True(
                bound.Contains(expected, StringComparer.Ordinal),
                $"资料修改页少了 {expected} 这个绑定 —— 填了目录也不会生效");
            Assert.True(
                known.Contains(expected),
                $"MainViewModel 上没有 {expected} 属性");
        }

        foreach (var expected in ValueBindings)
        {
            Assert.True(
                bound.Contains(expected, StringComparer.Ordinal),
                $"资料修改页少了 {expected} 这个绑定 —— 这一项没有界面入口，只能手改 config.json");
            Assert.True(
                known.Contains(expected),
                $"MainViewModel 上没有 {expected} 属性");
        }
    }

    /// <summary>
    /// 资料修改页绑定的每一项都必须在 <c>PersistConfig</c> 里写回配置。
    /// </summary>
    /// <remarks>
    /// 「绑定写错」会静默失效，「绑定对了但忘了写回」同样静默 —— 后者更坑：
    /// 界面上改得动、看着生效了，关掉程序再打开又变回旧值。这类漏项靠人点界面发现不了，
    /// 只能靠把「界面上的名字」和「写回配置的名字」对一遍。
    /// </remarks>
    [Fact]
    public void 资料修改页的每个绑定都被保存写回()
    {
        var block = ProfileEditTab();

        // 排除 Command="{Binding XxxCommand}"：命令是行为，不是配置值，
        // 既不该要求它「在 ViewModel 上有对应属性」，也不该要求它「写回 config.json」。
        // 这条是 S17 接上「选择...」按钮之后暴露的 —— 原来那一页一个 Command 绑定都没有。
        var bound = Regex.Matches(block, @"(?<!Command="")\{Binding\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var body = PersistConfigBody();

        // 解析器自证：正常的方法体不可能只有几行。
        Assert.True(body.Length > 500, $"PersistConfig 方法体只取到 {body.Length} 个字符，提取规则大概坏了");

        var missing = bound
            .Where(name => !Regex.IsMatch(body, @"config\." + Regex.Escape(name) + @"\s*="))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"这些绑定在界面上能改，但 PersistConfig 没有写回，重启就会丢：{string.Join("、", missing)}");
    }

    /// <summary>
    /// 取出 <c>MainViewModel.PersistConfig</c> 的方法体（按大括号配对切，不靠缩进），并**去掉注释**。
    /// </summary>
    /// <remarks>
    /// 去注释与切方法体都交给 <see cref="CsSource.MethodBody"/>，两个守卫类（本类与
    /// <c>PathPickerWiringTests</c>）共用同一套规则 —— 免得一边修好了、另一边还留着同样的漏洞。
    /// </remarks>
    private static string PersistConfigBody()
    {
        var source = File.ReadAllText(RepoFiles.Resolve("src", "FbAuto.Presentation", "ViewModels", "MainViewModel.cs"));
        var body = CsSource.MethodBody(source, "public void PersistConfig()");

        // 解析器自己先证伪：一份正常的 PersistConfig 不可能只剩几个字符。
        Assert.True(body.Length > 500, $"PersistConfig 方法体只取到 {body.Length} 个字符，提取规则大概坏了");

        return body;
    }

    /// <summary>取出「资料修改」这一页的 XAML 原文。</summary>
    private static string ProfileEditTab()
    {
        var xaml = File.ReadAllText(RepoFiles.Resolve("src", "FbAuto.Presentation", "Views", "MainWindow.xaml"));

        const string head = "<TabItem Header=\"资料修改\">";
        var start = xaml.IndexOf(head, StringComparison.Ordinal);
        Assert.True(start >= 0, "MainWindow.xaml 里没有「资料修改」这一页");

        start += head.Length;
        var end = xaml.IndexOf("</TabItem>", start, StringComparison.Ordinal);
        Assert.True(end > start, "「资料修改」这一页没有闭合");

        return xaml[start..end];
    }

    /// <summary>
    /// 从 MainViewModel 源码里取属性名（含 <c>[ObservableProperty]</c> 生成的那些）。
    /// </summary>
    private static HashSet<string> ViewModelPropertyNames()
    {
        var source = File.ReadAllText(RepoFiles.Resolve("src", "FbAuto.Presentation", "ViewModels", "MainViewModel.cs"));

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in Regex.Matches(
                     source,
                     @"\[ObservableProperty\]\s*private\s+[\w<>\[\]?,.]+\s+_(\w+)\s*;"))
        {
            var field = m.Groups[1].Value;
            names.Add(char.ToUpperInvariant(field[0]) + field[1..]);
        }

        foreach (Match m in Regex.Matches(
                     source,
                     @"public\s+(?:static\s+)?[\w<>\[\]?,.]+\s+(\w+)\s*(\{|=>)"))
        {
            names.Add(m.Groups[1].Value);
        }

        return names;
    }
}
