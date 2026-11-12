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
public sealed record ChannelProfile(
    BrowserChannelKind Channel,
    string VideoDirectory,
    string DescriptionFile,
    string SignatureFile,
    string ProfileCategory,
    bool UpdatePostingSettings,
    IReadOnlyList<string> CommentsBlockWords)
{
    /// <summary>通道的显示名，写日志用。</summary>
    public string DisplayName => Channel.ToDisplay();
}
