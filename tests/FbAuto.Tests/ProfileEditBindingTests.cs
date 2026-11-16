using System.Text.RegularExpressions;

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

    [Fact]
    public void 资料修改页的绑定都对应ViewModel属性()
    {
        var block = ProfileEditTab();

        // {Binding Xxx} / {Binding Xxx, ...} —— 只取属性名，忽略后面的 UpdateSourceTrigger 之类。
        var bound = Regex.Matches(block, @"\{Binding\s+([A-Za-z_][A-Za-z0-9_]*)")
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
    }

    /// <summary>取出「资料修改」这一页的 XAML 原文。</summary>
    private static string ProfileEditTab()
    {
        var xaml = File.ReadAllText(RepoFile("src", "FbAuto.Presentation", "Views", "MainWindow.xaml"));

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
        var source = File.ReadAllText(RepoFile("src", "FbAuto.Presentation", "ViewModels", "MainViewModel.cs"));

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

    /// <summary>
    /// 按仓库根拼路径。用 <c>FbAuto.slnx</c> 当锚点往上找 ——
    /// 测试输出在 <c>build/out/...</c>，改输出路径也不会失效。
    /// 找不到就直接失败并打印起点：静默跳过等于这条用例不存在。
    /// </summary>
    private static string RepoFile(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FbAuto.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, $"从 {AppContext.BaseDirectory} 往上没找到 FbAuto.slnx");

        var path = Path.Combine([dir!.FullName, .. relative]);
        Assert.True(File.Exists(path), $"没找到 {path}");
        return path;
    }
}
