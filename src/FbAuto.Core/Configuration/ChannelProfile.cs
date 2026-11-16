using FbAuto.Core.Models;

namespace FbAuto.Core.Configuration;

/// <summary>
/// 一条通道（谷歌版 / 比特版）的全部业务参数。
/// </summary>
/// <remarks>
/// 为什么要有这层：原版 settings.json 里，凡是业务路径都是**成对**的 ——
/// <c>GoogleVideoPath</c> / <c>BitVideoPath</c>、<c>GoogleSignatureFilePath</c> / <c>BitSignatureFilePath</c>，
/// 连评论屏蔽词都是两份。也就是说同一个使用者可能让谷歌版跑 A 目录、比特版跑 B 目录。
///
/// 页面流程本身**没有差异**（DLL 里找不到任何「比特版特有」的日志或选择器），
/// 差异全在「读哪一份配置」和「用哪个浏览器通道」。所以这一层只装数据，不放行为。
/// </remarks>
/// <param name="Channel">这条配置属于哪个通道。</param>
/// <param name="VideoDirectory">视频目录。</param>
/// <param name="DescriptionFile">描述文件（.txt，整段一份）。</param>
/// <param name="SignatureFile">签名文件（.txt，整段一份）。</param>
/// <param name="ProfileCategory">
/// 资料修改要设置的主页类别。**留空不是「不改」**：原版把类别写死在代码里，
/// 执行器会回落到那个词（<c>CategoryEditor.ResolveTarget</c>）。
/// </param>
/// <param name="UpdatePostingSettings">是否把发帖权限改成仅限自己。</param>
/// <param name="CommentsBlockWords">
/// 屏蔽关键词，「资料修改 → 粉丝公开内容」用（原版 <c>Google/BitCommentsBlockText</c>）。
/// 默认那五个词与原版串里的 <c>.com https pn dn inbox</c> 一致。
/// </param>
/// <param name="ImageDirectory">
/// 头像与背景图共用的图片目录。**一个目录、一张图**：原版提示是
/// 「头像和背景图均从该目录随机选取」+「头像和背景图在同一账号内使用同一张随机图片，不同账号随机不同」。
/// 默认空串，排在最后 —— 老配置反序列化时缺这个键，落到空串正好是「没配就不改」。
/// </param>
/// <param name="NamesFile">
/// 主页名称文件（.txt，一行一个名字）。原版键名 <c>Google/BitNamesFilePath</c>。
/// </param>
/// <param name="PageNamePassword">
/// 改主页名字时要输的主账号密码。原版把密码也存在 settings.json 的明文字段里
/// （<c>Google/BitPageNamePassword</c>）；本实现沿用同一位置，但界面上是密码框。
/// </param>
/// <param name="EditCoverImage">是否改背景图（原版 <c>Google/BitEditCoverImage</c>）。</param>
/// <param name="EditAvatar">是否改头像（原版 <c>Google/BitEditAvatar</c>）。</param>
/// <param name="EditFollowersContent">是否改粉丝公开内容（原版 <c>Google/BitEditFollowersContent</c>）。</param>
/// <param name="EditSignature">是否改个性签名（原版 <c>Google/BitEditSignature</c>）。</param>
/// <param name="EditCategory">是否改类别（原版 <c>Google/BitEditCategory</c>）。</param>
/// <param name="EditPageName">是否改主页名字（原版 <c>Google/BitEditPageName</c>）。</param>
/// <param name="ProfileEditResultExportDir">
/// 「资料修改结果保存目录」（原版 <c>Google/BitProfileEditResultExportDir</c>）。
/// 空表示不导出 —— 原版两份 settings.json 里这个键的值都是空串，即默认就是关的。
/// </param>
/// <remarks>
/// **七项开关的默认值一律 <c>true</c>，这不是随手写的**。原版这两份 <c>settings.json</c>
/// （发行包自带的与实测跑出来的）里都**没有** <c>EditXxx</c> 这七个键 ——
/// 也就是说原版没把它们持久化，只在界面上作为当次勾选。
///
/// 我们这边要持久化（界面是配置页，勾了下次还在才符合预期），于是「老配置缺键」就必须落到
/// 「照旧改」：<c>false</c> 会让升级后的使用者看到任务全部报成功、实际一项都没改，
/// 而且没有任何失败记录可查。默认 <c>true</c> = 老配置行为不变。
/// 这条由用例 `老配置缺开关键时七项照改` 钉住（把初值改成 false 会立刻变红）。
///
/// **发帖设置那一项的开关是 <see cref="UpdatePostingSettings"/>，不是 <c>EditPagePostSettings</c>。**
/// 原版键名是后者，本实现 S5 就用了前者（语义一样：这一项做不做，目标值固定「仅限自己」），
/// 改名会让已有 config.json 里的值静默丢失 —— 而这个文件里的值都是使用者手填的。名字对不上的事记在
/// <c>docs/PITFALLS.md</c>，这里不再另起一个键造出两个真相。
/// </remarks>
public sealed record ChannelProfile(
    BrowserChannelKind Channel,
    string VideoDirectory,
    string DescriptionFile,
    string SignatureFile,
    string ProfileCategory,
    bool UpdatePostingSettings,
    IReadOnlyList<string> CommentsBlockWords,
    string ImageDirectory = "",
    string NamesFile = "",
    string PageNamePassword = "",
    bool EditCoverImage = true,
    bool EditAvatar = true,
    bool EditFollowersContent = true,
    bool EditSignature = true,
    bool EditCategory = true,
    bool EditPageName = true,
    string ProfileEditResultExportDir = "")
{
    /// <summary>通道的显示名，写日志用。</summary>
    public string DisplayName => Channel.ToDisplay();

    /// <summary>
    /// 本次要跑哪几项，按原版分发顺序给出的中文名。写日志用。
    /// </summary>
    /// <remarks>
    /// 顺序与 <c>ProfileEditFlow</c> 的执行顺序一致。有意做成「按同一顺序手写一遍」而不是共享一份枚举：
    /// 那边是执行层、没有 Core 依赖，为了共用清单把配置层拉进执行层不划算。
    /// 对不齐时由用例「开关清单与执行顺序一致」兜住。
    /// </remarks>
    public IReadOnlyList<string> EnabledEditItems
    {
        get
        {
            var items = new List<string>(7);
            if (EditCoverImage)
            {
                items.Add("背景图");
            }

            if (EditAvatar)
            {
                items.Add("头像");
            }

            if (UpdatePostingSettings)
            {
                items.Add("发帖设置");
            }

            if (EditFollowersContent)
            {
                items.Add("粉丝公开内容");
            }

            if (EditSignature)
            {
                items.Add("个性签名");
            }

            if (EditCategory)
            {
                items.Add("类别");
            }

            if (EditPageName)
            {
                items.Add("主页名字");
            }

            return items;
        }
    }
}
