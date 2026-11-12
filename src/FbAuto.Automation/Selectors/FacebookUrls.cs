namespace FbAuto.Automation.Selectors;

/// <summary>
/// 页面地址目录。全部来自原版 DLL 的 UTF-16 串。
/// </summary>
/// <remarks>
/// 和 <see cref="FacebookSelectors"/> 分开：这里是「去哪儿」，那里是「点哪个」。
/// 混在一起会让「改域名」和「改选择器」两件事看起来一样。
///
/// <c>{0}</c> 一律是业务主页的 profile id（纯数字），不是账号别名。
/// </remarks>
public static class FacebookUrls
{
    /// <summary>签名所在页。原版串：<c>&amp;sk=directory_intro</c> 挂在 profile.php 后面。</summary>
    public const string ProfileIntro = "https://www.facebook.com/profile.php?id={0}&sk=directory_intro";

    /// <summary>类别所在页。原版串：<c>&amp;sk=directory_category</c></summary>
    public const string ProfileCategory = "https://www.facebook.com/profile.php?id={0}&sk=directory_category";

    /// <summary>发帖与标记设置页。原版串：<c>https://www.facebook.com/settings/?tab=profile_and_tagging</c></summary>
    public const string PostingSettings = "https://www.facebook.com/settings/?tab=profile_and_tagging";

    /// <summary>
    /// 主页名称设置页。原版串：<c>https://www.facebook.com/settings?tab=profile&amp;section=name&amp;view</c>
    /// </summary>
    /// <remarks>
    /// 这一页要**在 iframe 里**找 <c>form[action='/settings/name_change_preview/']</c>，
    /// 提交后还要主账号密码（<c>#save_password</c> + <c>button[value='1'].layerConfirm</c>）。
    /// 这一步还没实现主页名称，地址先留在这里，别在别处重写一遍。
    /// </remarks>
    public const string NameSettings = "https://www.facebook.com/settings?tab=profile&section=name&view";

    /// <summary>把 profile id 填进模板。</summary>
    public static string Format(string template, string profileId) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, template, profileId);
}
