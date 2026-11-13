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
/// <param name="ProfileCategory">资料修改要设置的主页类别，空表示不改。</param>
/// <param name="UpdatePostingSettings">是否把发帖权限改成仅限自己。</param>
/// <param name="CommentsBlockWords">评论屏蔽词。</param>
/// <param name="ImageDirectory">
/// 头像与背景图共用的图片目录。**一个目录、一张图**：原版提示是
/// 「头像和背景图均从该目录随机选取」+「头像和背景图在同一账号内使用同一张随机图片，不同账号随机不同」。
/// 默认空串，排在最后 —— 老配置反序列化时缺这个键，落到空串正好是「没配就不改」。
/// </param>
public sealed record ChannelProfile(
    BrowserChannelKind Channel,
    string VideoDirectory,
    string DescriptionFile,
    string SignatureFile,
    string ProfileCategory,
    bool UpdatePostingSettings,
    IReadOnlyList<string> CommentsBlockWords,
    string ImageDirectory = "")
{
    /// <summary>通道的显示名，写日志用。</summary>
    public string DisplayName => Channel.ToDisplay();
}
