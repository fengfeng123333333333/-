using FbAuto.Automation.Abstractions;

namespace FbAuto.Tests.Support;

/// <summary>
/// 记录调用的假页面。用字典声明「哪些选择器可见/可用」，其余一律不可见。
/// </summary>
/// <remarks>
/// 有了它，弹窗决策与执行这段逻辑在没有任何浏览器的情况下也能测完 ——
/// 等 Playwright 实现补上，换的只是这个假类，被测代码一行不动。
/// </remarks>
public sealed class FakePageInterop : IPageInterop
{
    public Dictionary<string, bool> VisibleSelectors { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, bool> EnabledSelectors { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

    public List<string> Clicked { get; } = [];

    /// <summary>每次点击用的序号，与 <see cref="Clicked"/> 一一对应。</summary>
    public List<int> ClickedIndexes { get; } = [];

    public int EscapeCount { get; private set; }

    public List<(string Selector, string FilePath)> FileInjections { get; } = [];

    public Task<bool> IsVisibleAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(VisibleSelectors.TryGetValue(selector, out var visible) && visible);

    public Task<bool> IsEnabledAsync(string selector, int index = 0, CancellationToken cancellationToken = default) =>
        Task.FromResult(EnabledSelectors.TryGetValue(selector, out var enabled) && enabled);

    public Task ClickAsync(string selector, int index = 0, CancellationToken cancellationToken = default)
    {
        Clicked.Add(selector);
        ClickedIndexes.Add(index);
        return Task.CompletedTask;
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

    public Task<int> CountAsync(string selector, CancellationToken cancellationToken = default) =>
        Task.FromResult(Counts.TryGetValue(selector, out var count) ? count : 0);
}
