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
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ReadAttributesAsync(
        string selector,
        string attribute,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            Attributes.TryGetValue(selector, out var values) ? values : []);

    public Task<IReadOnlyList<string>> ReadTextsAsync(
        string selector,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(
            Texts.TryGetValue(selector, out var values) ? values : []);

    public Task<string> ReadContentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PageContent);
    }

    public Task PressEscapeAsync(CancellationToken cancellationToken = default)
    {
        EscapeCount++;
        return Task.CompletedTask;
    }

    public Task SetFileInputAsync(string selector, string filePath, CancellationToken cancellationToken = default)
    {
        FileInjections.Add((selector, filePath));
        return Task.CompletedTask;
    }

    public Task SetFileInputsAsync(
        string selector,
        IReadOnlyList<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        foreach (var path in filePaths)
        {
            FileInjections.Add((selector, path));
        }

        return Task.CompletedTask;
    }

    public Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(Counts.TryGetValue(selector, out var count) ? count : 0);
}
