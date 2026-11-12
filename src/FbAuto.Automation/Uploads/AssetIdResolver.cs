using FbAuto.Automation.Abstractions;

namespace FbAuto.Automation.Uploads;

/// <summary>
/// 打开「我的主页」列表页，从里面找出业务主页的 <c>asset_id</c>。
/// </summary>
/// <remarks>
/// 为什么单独一步：批量上传页的地址要 asset_id 才能拼出来，而 asset_id 只在主页列表页里。
/// 原版串里这条路径是完整的：
/// <c>跳转 Facebook...</c> → <c>https://www.facebook.com/pages/?category=your_pages</c>
/// → <c>提取子账号链接...</c> → <c>提取 asset_id...</c> → 跳 composer。
///
/// 列表页地址做成参数是为了能本地验证：真站点要登录态，仿真页只要放一条带 asset_id 的链接，
/// 就能把「导航 → 读整页 → 正则提取」这条链跑通。
/// </remarks>
public static class AssetIdResolver
{
    /// <summary>证据里的列表页地址。</summary>
    public const string DefaultPagesUrl = "https://www.facebook.com/pages/?category=your_pages";

    /// <summary>
    /// 导航到列表页并提取 asset_id。找不到返回未命中，不抛异常。
    /// </summary>
    public static async Task<AssetIdExtraction> ResolveAsync(
        IPageInterop page,
        string? pagesUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        await page.NavigateAsync(pagesUrl ?? DefaultPagesUrl, cancellationToken).ConfigureAwait(false);
        var content = await page.ReadContentAsync(cancellationToken).ConfigureAwait(false);

        return AssetIdExtractor.ExtractDetailed(content);
    }
}
