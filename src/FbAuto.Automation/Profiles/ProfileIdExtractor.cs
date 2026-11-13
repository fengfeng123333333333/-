using System.Text.RegularExpressions;

namespace FbAuto.Automation.Profiles;

/// <summary>profile id 是从哪种链接里找到的。</summary>
public enum ProfileIdSource
{
    /// <summary>没找到。</summary>
    None,

    /// <summary>完整的 <c>https://www.facebook.com/profile.php?id=</c> 链接。</summary>
    AbsoluteUrl,

    /// <summary>相对/转义后的 <c>profile.php?id=</c>。</summary>
    Loose,
}

/// <summary>提取结果。</summary>
/// <param name="ProfileId">纯数字 id，没找到为 null。</param>
/// <param name="Source">来源。</param>
public sealed record ProfileIdExtraction(string? ProfileId, ProfileIdSource Source)
{
    public bool Found => !string.IsNullOrEmpty(ProfileId);

    public string LogLine => Source switch
    {
        ProfileIdSource.AbsoluteUrl => $"已获取主页 profile id: {ProfileId}",
        ProfileIdSource.Loose => $"已获取主页 profile id: {ProfileId}（相对链接兜底）",
        _ => "没有找到主页 profile id，跳过该账号",
    };
}

/// <summary>
/// 从页面内容里提取业务主页的 <c>profile.php?id=</c>。
/// </summary>
/// <remarks>
/// 资料修改的三个字段都要靠这个 id 拼地址（签名页、类别页都是
/// <c>profile.php?id={id}&amp;sk=...</c>）。
///
/// 两级正则的理由来自原版串：它先用
/// <c>a[href*='https://www.facebook.com/profile.php?id=']</c> 这个**完整前缀**定位，
/// 说明页面上还有别的 <c>profile.php?id=</c>（比如别人的主页）——
/// 直接扫 <c>profile.php?id=(\d+)</c> 有取错人的风险。
/// 所以优先完整链接，取不到再放宽，并把「用了兜底」写进日志。
/// </remarks>
public static class ProfileIdExtractor
{
    private const string IdPattern = @"(?<id>\d{5,})";

    private static readonly Regex AbsolutePattern = new(
        $@"https://www\.facebook\.com/profile\.php\?id={IdPattern}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    private static readonly Regex LoosePattern = new(
        $@"profile\.php\?(?:amp;)?id={IdPattern}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromSeconds(2));

    /// <summary>只要 id 的简化入口。</summary>
    public static string? Extract(string? content) => ExtractDetailed(content).ProfileId;

    /// <summary>带来源的提取。</summary>
    public static ProfileIdExtraction ExtractDetailed(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ProfileIdExtraction(null, ProfileIdSource.None);
        }

        if (TryMatch(AbsolutePattern, content, out var id))
        {
            return new ProfileIdExtraction(id, ProfileIdSource.AbsoluteUrl);
        }

        if (TryMatch(LoosePattern, content, out id))
        {
            return new ProfileIdExtraction(id, ProfileIdSource.Loose);
        }

        return new ProfileIdExtraction(null, ProfileIdSource.None);
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
