using System.Text.RegularExpressions;
using FbAuto.Application.Dialogs;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 「选择...」按钮的接线守卫：XAML 的按钮、对照表的 key、ViewModel 的属性，三者必须一一对上。
/// </summary>
/// <remarks>
/// 十五个按钮共用同一个命令，靠 <c>CommandParameter</c> 区分。这个设计省掉了十五份一模一样的
/// <c>RelayCommand</c>，但也带来一个静默失效点：参数拼错、属性改名没同步、
/// 复制粘贴时把按钮接到了上一行的输入框 —— **WPF 一律不报错**，表现只是「按下去没反应」。
///
/// 所以这些只能当文本对一遍。顺带钉住「表里有、界面没有」和「界面有、写回漏了」两个方向。
/// </remarks>
public sealed class PathPickerWiringTests
{
    private const string PickCommand = @"Command=""\{Binding PickPathCommand\}""";

    /// <summary>
    /// XAML 里每个「选择...」按钮都必须接了命令，且数量与对照表一致。
    /// </summary>
    /// <remarks>
    /// 「没接命令」的按钮在 WPF 里就是一块纯装饰：能点、有按下动画、什么都不发生。
    /// 这比选错路径更让人摸不着头脑 —— 使用者会以为程序卡了。
    /// </remarks>
    [Fact]
    public void 每个选择按钮都接了命令()
    {
        var xaml = Xaml();

        var buttons = Regex.Matches(xaml, @"Content=""选择\.\.\.""").Count;
        var wired = Regex.Matches(xaml, PickCommand).Count;

        Assert.Equal(PathPickerCatalog.Keys.Count, buttons);
        Assert.Equal(buttons, wired);
    }

    /// <summary>按钮的参数必须能在对照表里查到，否则点下去只会留一行警告。</summary>
    [Fact]
    public void 按钮的参数都能在对照表里查到()
    {
        var parameters = CommandParameters();

        Assert.Equal(PathPickerCatalog.Keys.Count, parameters.Count);

        var unknown = parameters
            .Where(p => PathPickerCatalog.TryResolve(p) is null)
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            $"这些按钮参数在 PathPickerCatalog 里没登记（点了不会生效）：{string.Join("、", unknown)}");
    }

    /// <summary>反过来：表里登记的每一项都得有按钮，不能只在代码里存在。</summary>
    [Fact]
    public void 对照表里每一项都有按钮()
    {
        var parameters = CommandParameters();
        var missing = PathPickerCatalog.Keys.Where(k => !parameters.Contains(k)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"这些路径在对照表里有、界面上没有按钮（只能手打）：{string.Join("、", missing)}");
    }

    /// <summary>
    /// 按钮必须挨着自己那一行的输入框 —— 它选的路径要写回 <c>CommandParameter</c> 指定的属性，
    /// 参数与旁边文本框绑的不是同一个属性，就是「选完填到了别的格子里」。
    /// </summary>
    [Fact]
    public void 按钮参数与旁边输入框绑定同名()
    {
        var xaml = Xaml();

        foreach (Match match in Regex.Matches(xaml, @"Command=""\{Binding PickPathCommand\}"" CommandParameter=""(\w+)"""))
        {
            var key = match.Groups[1].Value;
            var buttonStart = xaml.LastIndexOf("<Button", match.Index, StringComparison.Ordinal);

            Assert.True(buttonStart >= 0, $"{key} 的参数不在 Button 里");

            // 往前找最近的那个 TextBox 绑定 —— 三列 Grid 里按钮左边紧挨着它要填的那一格。
            var textStart = xaml.LastIndexOf("Text=\"{Binding ", buttonStart, StringComparison.Ordinal);
            Assert.True(textStart >= 0, $"{key} 的按钮左边没有输入框");

            var binding = Regex.Match(xaml[textStart..buttonStart], @"Text=""\{Binding (\w+)");

            Assert.True(binding.Success, $"{key} 的按钮左边那格的绑定解析不出来");
            Assert.Equal(binding.Groups[1].Value, key);
        }
    }

    /// <summary>
    /// 命令必须真的生成出来：<c>[RelayCommand]</c> 挨着 <c>private void PickPath(</c>。
    /// 少了特性，<c>PickPathCommand</c> 这个属性就不存在，XAML 里所有绑定一起静默失效。
    /// </summary>
    [Fact]
    public void 选择命令在ViewModel上真的生成了()
    {
        var source = ViewModelSource();

        Assert.Matches(@"\[RelayCommand\]\s*private\s+void\s+PickPath\s*\(", source);
        Assert.Contains("IPathPicker", source);
    }

    /// <summary>
    /// 选中之后要能写回属性：<c>ApplyPath</c> 的每个 case 都得覆盖对照表的 key。
    /// 漏一个的表现是「对话框弹了、也选了，输入框还是空的」。
    /// </summary>
    [Fact]
    public void 每个参数都能写回属性()
    {
        var body = CsSource.MethodBody(ViewModelSource(), "private void ApplyPath(");

        var cases = Regex.Matches(body, @"case\s+nameof\(\s*(\w+)\s*\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // 解析器自证
        Assert.True(body.Length > 200, $"ApplyPath 方法体只取到 {body.Length} 个字符，提取规则大概坏了");

        var missing = PathPickerCatalog.Keys.Where(k => !cases.Contains(k)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"这些 key 能弹对话框，但 ApplyPath 里没有对应分支，选完不写回：{string.Join("、", missing)}");

        // 每个 case 都要真的把值赋进去，不能只写个空 case。
        foreach (var key in PathPickerCatalog.Keys)
        {
            Assert.Matches($@"case\s+nameof\(\s*{key}\s*\)\s*:\s*{key}\s*=\s*value", body);
        }
    }

    /// <summary>
    /// 打开对话框时要知道「现在的值是什么」才能停在上次的位置，所以每个 key 在
    /// <c>CurrentPathOf</c> 里也得有一支。漏了只是每次从默认位置开始，功能不坏但很难用。
    /// </summary>
    [Fact]
    public void 每个参数都能取到当前值()
    {
        var body = CsSource.MethodBody(ViewModelSource(), "private string? CurrentPathOf(");
        var covered = CsSource.Nameofs(body);

        Assert.True(body.Length > 200, $"CurrentPathOf 方法体只取到 {body.Length} 个字符，提取规则大概坏了");

        var missing = PathPickerCatalog.Keys.Where(k => !covered.Contains(k)).ToArray();

        Assert.True(
            missing.Length == 0,
            $"这些 key 取不到当前值，对话框每次都从默认位置开始：{string.Join("、", missing)}");
    }

    /// <summary>
    /// 选完的路径要能存进 config.json，否则关掉程序再打开就没了。
    /// </summary>
    /// <remarks>
    /// 这条专门为 <c>ChromePath</c> / <c>AccountsFile</c> 而写：它们此前连输入框都没有，
    /// 加界面时最容易忘了加写回 —— 界面上改得动、看着生效，重启就变回原样。
    /// </remarks>
    [Fact]
    public void 每个参数都被保存写回()
    {
        var body = CsSource.MethodBody(ViewModelSource(), "public void PersistConfig()");

        Assert.True(body.Length > 500, $"PersistConfig 方法体只取到 {body.Length} 个字符，提取规则大概坏了");

        var missing = PathPickerCatalog.Keys
            .Where(k => !Regex.IsMatch(body, @"config\." + Regex.Escape(k) + @"\s*="))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"这些路径界面上能选，但 PersistConfig 没有写回，重启就丢：{string.Join("、", missing)}");
    }

    /// <summary>
    /// 点「开始任务」之前必须先把界面值写回配置。
    /// </summary>
    /// <remarks>
    /// 真机首轮踩到：填完视频路径直接点开始，任务立刻失败，结果列写「没有配置视频目录」。
    /// 根因不是路径没填对 —— 执行器持有的是启动时那份 AppConfig 实例，
    /// 而界面值原先只在窗口关闭时才同步，于是第一次填的东西根本没进这一轮执行。
    /// 表现极具误导性：界面上明明填着，日志说没配。
    /// </remarks>
    [Fact]
    public void 开始任务前把界面值写回配置()
    {
        var body = CsSource.MethodBody(ViewModelSource(), "private async Task StartTasksAsync()");

        Assert.True(body.Length > 200, $"StartTasksAsync 方法体只取到 {body.Length} 个字符，提取规则大概坏了");

        Assert.Contains("PersistConfig()", body);
    }

    /// <summary>XAML 里所有「选择...」按钮的参数值，按出现顺序。</summary>
    private static List<string> CommandParameters() =>
        Regex.Matches(Xaml(), @"Command=""\{Binding PickPathCommand\}"" CommandParameter=""(\w+)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

    private static string Xaml() =>
        File.ReadAllText(RepoFiles.Resolve("src", "FbAuto.Presentation", "Views", "MainWindow.xaml"));

    private static string ViewModelSource() =>
        File.ReadAllText(RepoFiles.Resolve("src", "FbAuto.Presentation", "ViewModels", "MainViewModel.cs"));
}
