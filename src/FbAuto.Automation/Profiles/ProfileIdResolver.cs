using FbAuto.Automation.Abstractions;

namespace FbAuto.Automation.Profiles;

/// <summary>
/// 打开自己的主页，从页面里找出 <c>profile.php?id=</c>。
/// </summary>
/// <remarks>
/// 资料修改的签名页、类别页都要这个 id。原版的做法是「访问 Facebook 主页 →
/// 找 <c>a[href*='https://www.facebook.com/profile.php?id=']</c>」，
/// 找不到还有一条「等待主账号链接超时」的兜底提示。
///
/// 页面地址做成参数，理由和 asset_id 那边一样：真站点要登录态，本地验证要能指向 file://。
/// </remarks>
public static class ProfileIdResolver
{
    /// <summary>自己的主页。会由 FB 重定向到真实主页地址。</summary>
    public const string DefaultHomeUrl = "https://www.facebook.com/profile.php";

    public static async Task<ProfileIdExtraction> ResolveAsync(
        IPageInterop page,
        string? homeUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        await page.NavigateAsync(homeUrl ?? DefaultHomeUrl, cancellationToken).ConfigureAwait(false);
        var content = await page.ReadContentAsync(cancellationToken).ConfigureAwait(false);

        return ProfileIdExtractor.ExtractDetailed(content);
    }
}
