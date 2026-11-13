using FbAuto.Automation.Abstractions;

namespace FbAuto.Tests.Support;

/// <summary>
/// 记录调用的假页面。用字典声明「哪些选择器可见/可用」，其余一律不可见。
/// </summary>
/// <remarks>
/// 有了它，弹窗决策与执行这段逻辑在没有任何浏览器的情况下也能测完 ——
/// 等 Playwright 实现补上，换的只是这个假类，被测代码一行不动。
///
/// **取消令牌一律检查并抛出**。真实现每个方法第一句都是
/// <c>ThrowIfCancellationRequested()</c>，假实现若默默忽略，就会造出一个
/// 「取消之后动作照样做完」的假绿 —— 取消语义的用例在这种假实现上永远不会红。
/// </remarks>
public sealed class FakePageInterop : IPageInterop
{
    public Dictionary<string, bool> VisibleSelectors { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, bool> EnabledSelectors { get; } = new(StringComparer.Ordinal);

    /// <summary>存在但不可见的元素（如 <c>display:none</c> 的 file input）。</summary>
    public HashSet<string> HiddenButExisting { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public List<string> Clicked { get; } = [];

    /// <summary>每次点击用的序号，与 <see cref="Clicked"/> 一一对应。</summary>
    public List<int> ClickedIndexes { get; } = [];

    public int EscapeCount { get; private set; }

    /// <summary>按键记录：(选择器, 键名, 序号)。</summary>
    public List<(string Selector, string Key, int Index)> PressedKeys { get; } = [];

    /// <summary>
    /// 动作的**交错**时间线，例如 <c>fill:spam</c>、<c>press:Enter</c>。
    /// </summary>
    /// <remarks>
    /// <see cref="Filled"/> 与 <see cref="PressedKeys"/> 各自成表，能数总量但看不出先后。
    /// 「填一个词、按一次回车」这类流程，真正的语义在先后上 —— 先按回车再填，
    /// 填的那个词就留在框里没变成标签，而两个表的长度完全一样。
    /// </remarks>
    public List<string> Timeline { get; } = [];

    public List<(string Selector, string FilePath)> FileInjections { get; } = [];

    /// <summary>导航过的 URL，按顺序。</summary>
    public List<string> NavigatedUrls { get; } = [];

    /// <summary>填写过的文本：(选择器, 内容, 序号)。</summary>
    public List<(string Selector, string Text, int Index)> Filled { get; } = [];

    /// <summary>按选择器声明「读属性」的返回值。</summary>
    public Dictionary<string, List<string>> Attributes { get; } = new(StringComparer.Ordinal);

    /// <summary>按选择器声明「读文本」的返回值。</summary>
    public Dictionary<string, List<string>> Texts { get; } = new(StringComparer.Ordinal);

    /// <summary>整页内容，供 asset_id 提取用。</summary>
    public string PageContent { get; set; } = string.Empty;

    /// <summary>
    /// 点击回调（选择器, 序号）。默认 null。
    /// </summary>
    /// <remarks>
    /// 用来模拟「点了之后页面变了」—— 例如删除消息时点下确认按钮，会话行数要减一。
    /// 没有这个钩子，循环类流程在假页面上永远是同一个读数，
    /// 只能测出「转了几圈」，测不出「每圈都真的推进了」。
    /// </remarks>
    public Action<string, int>? OnClick { get; set; }

    /// <summary>
    /// 注入回调（选择器, 文件路径）。默认 null。
    /// </summary>
    /// <remarks>
    /// 用来模拟「注入之后页面弹了提示」。头像换图的重点在「被拒之后有没有重开入口」，
    /// 没有这个钩子就只能断言「注入了两次」，测不出「第二次注入之前菜单重开过」。
    /// </remarks>
    public Action<string, string>? OnInject { get; set; }

    /// <summary>
    /// 按键回调（选择器, 键名）。默认 null。
    /// </summary>
    /// <remarks>
    /// 用来数「回车按了几次」：屏蔽关键词是「一个词一次回车变成一个标签」，
    /// 漏按一次不会报错，只是少一个词生效 —— 只有把次数钉住才看得出来。
    /// </remarks>
    public Action<string, string>? OnPressKey { get; set; }

    public Task NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NavigatedUrls.Add(url);
        return Task.CompletedTask;
    }

    public Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(VisibleSelectors.TryGetValue(selector, out var visible) && visible);
    }

    public Task<bool> ExistsAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(
            HiddenButExisting.Contains(selector)
            || (VisibleSelectors.TryGetValue(selector, out var visible) && visible));
    }

    public Task<bool> IsEnabledAsync(string selector, int index = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EnabledSelectors.TryGetValue(selector, out var enabled) && enabled);
    }

    public Task ClickAsync(string selector, int index = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clicked.Add(selector);
        ClickedIndexes.Add(index);
        OnClick?.Invoke(selector, index);
        return Task.CompletedTask;
    }

    public Task FillAsync(
        string selector,
        string text,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Filled.Add((selector, text, index));
        Timeline.Add($"fill:{text}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ReadAttributesAsync(
        string selector,
        string attribute,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            Attributes.TryGetValue(selector, out var values) ? values : []);
    }

    public Task<IReadOnlyList<string>> ReadTextsAsync(
        string selector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<string>>(
            Texts.TryGetValue(selector, out var values) ? values : []);
    }

    public Task<string> ReadContentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PageContent);
    }

    public Task PressEscapeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EscapeCount++;
        return Task.CompletedTask;
    }

    public Task PressKeyAsync(
        string selector,
        string key,
        int index = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PressedKeys.Add((selector, key, index));
        Timeline.Add($"press:{key}");
        OnPressKey?.Invoke(selector, key);
        return Task.CompletedTask;
    }

    public Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInjections.Add((selector, filePath));
        OnInject?.Invoke(selector, filePath);
        return Task.CompletedTask;
    }

    public Task SetFileInputsAsync(
        string selector,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var path in filePaths)
        {
            FileInjections.Add((selector, path));
            OnInject?.Invoke(selector, path);
        }

        return Task.CompletedTask;
    }

    public Task<int> CountAsync(string selector, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Counts.TryGetValue(selector, out var count) ? count : 0);
    }
}
