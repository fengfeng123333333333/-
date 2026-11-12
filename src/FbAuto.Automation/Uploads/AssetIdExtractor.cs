using System.Text.RegularExpressions;

namespace FbAuto.Automation.Uploads;

/// <summary>asset_id 是从哪儿找到的。写进日志，排查时能区分「正常命中」和「靠兜底猜的」。</summary>
public enum AssetIdSource
{
    /// <summary>没找到。</summary>
    None,

    /// <summary>从 <c>leads_center?asset_id=</c> 链接里直接取到。</summary>
    LeadsCenter,

    /// <summary>从 <c>LEGACY_</c> 前缀的标识里兜底取到。</summary>
    Legacy,

    /// <summary>从 <c>_boosted_post_</c> 前缀的标识里兜底取到。</summary>
    BoostedPost,
}

/// <summary>提取结果。</summary>
/// <param name="AssetId">纯数字的 asset_id，没找到为 null。</param>
/// <param name="Source">来源。</param>
public sealed record AssetIdExtraction(string? AssetId, AssetIdSource Source)
{
    public bool Found => !string.IsNullOrEmpty(AssetId);

    /// <summary>写进任务日志的行，文案对齐原版。</summary>
    public string LogLine => Source switch
    {
        AssetIdSource.LeadsCenter => $"asset_id 提取成功: {AssetId}",
        AssetIdSource.Legacy => $"asset_id 通过 fallback 方式提取成功: {AssetId}（LEGACY_）",
        AssetIdSource.BoostedPost => $"asset_id 通过 fallback 方式提取成功: {AssetId}（_boosted_post_）",
        _ => "没有找到任何 asset_id，跳过该账号",
    };
}

/// <summary>
/// 从页面内容里提取业务主页的 <c>asset_id</c>。
/// </summary>
/// <remarks>
/// 这一步绕不过去：批量上传页的地址是
/// <c>//business.facebook.com/latest/bulk_upload_composer?asset_id={id}</c>，
/// 没有 id 就拼不出地址。原版的做法是先进 <c>/pages/?category=your_pages</c> 拿子账号链接，
/// 再进个人主页找 id，并留了 <c>LEGACY_</c> 与 <c>_boosted_post_</c> 两条兜底 ——
/// 串 <c>asset_id 通过 fallback 方式提取成功</c> 就是兜底成功时的日志。
///
/// 做成纯函数是因为它只依赖字符串：真机验收时把页面 HTML 存下来，
/// 就能离线验证这三条规则还准不准，不必重跑整个流程。
/// </remarks>
public static class AssetIdExtractor
{
    /// <summary>asset_id 的长度下界。太短的命中基本都是别的数字 ID（帖子号、时间戳片段）。</summary>
    private const int MinLength = 6;

    private const string IdPattern = @"\d{6,}";

    private static readonly Regex LeadsCenterPattern = new(
        $@"asset_id(?:=|%3D)(?<id>{IdPattern})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex LegacyPattern = new(
        $@"LEGACY_(?<id>{IdPattern})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex BoostedPostPattern = new(
        $@"_boosted_post_(?<id>{IdPattern})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    /// <summary>只要 id 的简化入口。</summary>
    public static string? Extract(string? content) => ExtractDetailed(content).AssetId;

    /// <summary>带来源的提取。</summary>
    public static AssetIdExtraction ExtractDetailed(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new AssetIdExtraction(null, AssetIdSource.None);
        }

        // 顺序即优先级：直接链接最可信，两条兜底次之。
        if (TryMatch(LeadsCenterPattern, content, out var id))
        {
            return new AssetIdExtraction(id, AssetIdSource.LeadsCenter);
        }

        if (TryMatch(LegacyPattern, content, out id))
        {
            return new AssetIdExtraction(id, AssetIdSource.Legacy);
        }

        if (TryMatch(BoostedPostPattern, content, out id))
        {
            return new AssetIdExtraction(id, AssetIdSource.BoostedPost);
        }

        return new AssetIdExtraction(null, AssetIdSource.None);
    }

    private static bool TryMatch(Regex regex, string content, out string? id)
    {
        var match = regex.Match(content);
        if (match.Success)
        {
            id = match.Groups["id"].Value;
            return true;
        }

        id = null;
        return false;
    }
}
