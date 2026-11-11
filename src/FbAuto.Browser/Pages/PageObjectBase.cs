using Microsoft.Playwright;

namespace FbAuto.Browser.Pages;

/// <summary>
/// 页面对象基类。只给三个动作：导航、等元素、截图（另加读文本，验收「读到已登录主页名」要用）。
/// 元素级等待保留超时轮询——这是原版就有的行为，不是自动重试，任务级失败不会被它掩盖。
/// </summary>
public abstract class PageObjectBase
{
    protected PageObjectBase(IPage page)
    {
        Page = page ?? throw new ArgumentNullException(nameof(page));
    }

    /// <summary>导航默认上限。</summary>
    protected virtual TimeSpan DefaultNavigationTimeout => TimeSpan.FromSeconds(30);

    /// <summary>等元素默认上限。</summary>
    protected virtual TimeSpan DefaultElementTimeout => TimeSpan.FromSeconds(20);

    protected IPage Page { get; }

    public string Url => Page.Url;

    public Task<string> GetTitleAsync() => Page.TitleAsync();

    /// <summary>导航到指定地址。超时抛中文异常，说明是网络出口问题。</summary>
    public async Task NavigateAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? DefaultNavigationTimeout;
        try
        {
            await Page.GotoAsync(url, new PageGotoOptions
            {
                Timeout = (float)limit.TotalMilliseconds,
                WaitUntil = WaitUntilState.Load,
            });
        }
        catch (TimeoutException ex)
        {
            throw Abstractions.BrowserChannelException.NavigationFailed(url, (int)limit.TotalSeconds, ex);
        }
        catch (PlaywrightException ex)
        {
            throw Abstractions.BrowserChannelException.NavigationFailed(url, (int)limit.TotalSeconds, ex);
        }
    }

    /// <summary>等元素可见。超时不重试整页，只报「元素没出现」，把判断权交给调用方。</summary>
    public async Task WaitForElementAsync(string selector, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var limit = timeout ?? DefaultElementTimeout;
        try
        {
            await Page.Locator(selector).First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = (float)limit.TotalMilliseconds,
            });
        }
        catch (TimeoutException)
        {
            throw Abstractions.BrowserChannelException.ElementNotFound(selector, (int)limit.TotalSeconds);
        }
    }

    /// <summary>读第一个匹配元素的文本（已去首尾空白）。</summary>
    public async Task<string> ReadTextAsync(string selector, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await WaitForElementAsync(selector, timeout, cancellationToken);
        var text = await Page.Locator(selector).First.InnerTextAsync();
        return text.Trim();
    }

    /// <summary>读所有匹配元素的文本。</summary>
    public async Task<IReadOnlyList<string>> ReadTextsAsync(string selector, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await WaitForElementAsync(selector, timeout, cancellationToken);
        var count = await Page.Locator(selector).CountAsync();
        var texts = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            texts.Add((await Page.Locator(selector).Nth(i).InnerTextAsync()).Trim());
        }

        return texts;
    }

    /// <summary>截图落盘，返回绝对路径。目录不存在会自动建。</summary>
    public async Task<string> ScreenshotAsync(string path, bool fullPage = false, CancellationToken cancellationToken = default)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = full,
            FullPage = fullPage,
        });
        return full;
    }
}
