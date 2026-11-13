using FbAuto.Core.Models;

namespace FbAuto.Core.Configuration;

/// <summary>
/// 应用配置，字段名与原版 settings.json 对应的语义保持一致；值取自原版实测默认（并发数 1、启动间隔 3 秒）。
/// 密码一类凭据不落此文件，只存 cred:// 引用。
/// </summary>
public sealed class AppConfig
{
    /// <summary>并发数。原版默认 1，不是拍脑袋值，是按风控换来的。</summary>
    public int ConcurrentCount { get; set; } = 1;

    /// <summary>浏览器启动间隔（秒）。原版默认 3。</summary>
    public int BrowserOpenIntervalSeconds { get; set; } = 3;

    /// <summary>视频所在目录（**谷歌版**；比特版见 <see cref="BitVideoDirectory"/>）。</summary>
    public string VideoDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 视频描述文件路径（.txt）。整段作为一段描述，不是一行一条。
    /// </summary>
    /// <remarks>
    /// 早先这里写的是「Excel/CSV」，与证据不符：原版的选择框是
    /// <c>文本文件 (*.txt)|*.txt</c>，且缺文件时的提示是「将以空描述上传」。
    /// </remarks>
    public string DescriptionFile { get; set; } = string.Empty;

    /// <summary>
    /// 签名文件路径（.txt）。整段作为个性签名，不是一行一条。
    /// </summary>
    public string SignatureFile { get; set; } = string.Empty;

    /// <summary>资料修改要设置的主页类别，留空表示不改类别。</summary>
    public string ProfileCategory { get; set; } = string.Empty;

    /// <summary>资料修改时是否把「谁能在你的公共主页发帖」改为仅限自己。</summary>
    public bool UpdatePostingSettings { get; set; } = true;

    /// <summary>
    /// 头像与背景图共用的图片目录（**谷歌版**）。
    /// </summary>
    /// <remarks>
    /// 一个目录而不是两个：原版提示「头像和背景图均从该目录随机选取」，
    /// 以及「头像和背景图在同一账号内使用同一张随机图片，不同账号随机不同」。
    /// 早先界面摆了两个选择框（头像 / 背景图），那是照着「两个功能」想当然画的，
    /// 与证据不符，S11 已合成一个。
    /// </remarks>
    public string ImageDirectory { get; set; } = string.Empty;

    /// <summary>结果保存目录。</summary>
    public string SaveDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 浏览器类型下拉的当前值：谷歌版 / 比特版。
    /// </summary>
    public string BrowserType { get; set; } = "谷歌版";

    /// <summary>Chrome 可执行文件路径。留空则自动查找（注册表 + 常见安装位置）。</summary>
    public string ChromePath { get; set; } = string.Empty;

    /// <summary>
    /// 账号清单 CSV 路径。留空则用程序目录下的 accounts.csv。
    /// 清单是手工维护的，放在程序目录便于发给使用者后自己改。
    /// </summary>
    public string AccountsFile { get; set; } = string.Empty;

    /// <summary>比特浏览器本地接口地址。端口写错会表现为「客户端未启动」。</summary>
    public string BitBrowserApiBaseUrl { get; set; } = "http://127.0.0.1:54345";

    /// <summary>定时发布日期，对应原版 ScheduledDate。</summary>
    public DateTime? ScheduledDate { get; set; }

    /// <summary>定时起始小时，对应原版 ScheduledStartHour。0 表示未启用定时。</summary>
    public int ScheduledStartHour { get; set; }

    /// <summary>
    /// 屏蔽关键词，默认与原版一致。**它属于「资料修改 → 粉丝公开内容」**，不是评论、也跟视频上传无关。
    /// </summary>
    /// <remarks>
    /// 这一项前后错了两次，都记在这里：
    /// <list type="number">
    /// <item>先叫 <c>MessageBlockWords</c> 并注释成「删除消息的屏蔽词」——看着字段名猜的。
    /// 原版键名是 <c>GoogleCommentsBlockText</c> / <c>BitCommentsBlockText</c>。</item>
    /// <item>改名成 <c>CommentsBlockWords</c> 后又被理解成「评论」，界面放进了视频上传页。
    /// 回证据才看清它出现在**粉丝公开内容**那一段串里：
    /// 「跳转到粉丝公开内容设置页...」→ <c>textarea[id='CometUSFTagInputFreeformDialogTagEditor']</c>
    /// → 「输入屏蔽词...」→ <c>.com https pn dn inbox</c> → 「共 N 个屏蔽词:」→ <c>Enter</c>
    /// → 「已添加屏蔽词:」→ 「粉丝公开内容设置完成」。
    /// 默认值那五个词与这段串一字不差，就是这条的证据。</item>
    /// </list>
    /// 删除消息在原版里唯一的专属参数是 <see cref="DeleteMessageCookieRootPath"/>。
    /// </remarks>
    public string[] CommentsBlockWords { get; set; } = [".com", "https", "pn", "dn", "inbox"];

    // ── 比特版 ─────────────────────────────────────────────────────────────
    //
    // 原版 settings.json 里业务路径全是成对的（GoogleXxx / BitXxx），比特版有自己的一套。
    // 字段名沿用「不带前缀的那份就是谷歌版」这个约定 —— **不改名**是因为改名会让已有的
    // config.json 静默读不到值，而里面是使用者手填的目录路径，丢了要重填。
    //
    // 页面流程没有差异（DLL 里找不到任何比特版特有的日志或选择器），
    // 差异只在「读哪一份」和「用哪个浏览器通道」。取配置统一走 ForChannel。

    /// <summary>比特版的视频目录。</summary>
    public string BitVideoDirectory { get; set; } = string.Empty;

    /// <summary>比特版的描述文件。</summary>
    public string BitDescriptionFile { get; set; } = string.Empty;

    /// <summary>比特版的签名文件。</summary>
    public string BitSignatureFile { get; set; } = string.Empty;

    /// <summary>比特版资料修改要设置的主页类别，空表示不改。</summary>
    public string BitProfileCategory { get; set; } = string.Empty;

    /// <summary>比特版是否把发帖权限改成仅限自己。</summary>
    public bool BitUpdatePostingSettings { get; set; } = true;

    /// <summary>比特版的评论屏蔽词。</summary>
    public string[] BitCommentsBlockWords { get; set; } = [".com", "https", "pn", "dn", "inbox"];

    /// <summary>比特版的头像与背景图图片目录。</summary>
    public string BitImageDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 按通道取业务参数。执行器只调这一个方法，不直接读字段 ——
    /// 这样「比特版读成了谷歌版的目录」这类错误只有一处可能发生，且能被单测钉住。
    /// </summary>
    public ChannelProfile ForChannel(BrowserChannelKind channel) => channel switch
    {
        BrowserChannelKind.Bit => new ChannelProfile(
            BrowserChannelKind.Bit,
            BitVideoDirectory,
            BitDescriptionFile,
            BitSignatureFile,
            BitProfileCategory,
            BitUpdatePostingSettings,
            BitCommentsBlockWords,
            BitImageDirectory),
        _ => new ChannelProfile(
            BrowserChannelKind.Google,
            VideoDirectory,
            DescriptionFile,
            SignatureFile,
            ProfileCategory,
            UpdatePostingSettings,
            CommentsBlockWords,
            ImageDirectory),
    };

    /// <summary>
    /// 每个账号最多删几条消息。0 表示不限，一直删到会话列表空。
    /// </summary>
    /// <remarks>
    /// 默认 20：删掉的对话在**对方那边也会消失**，不可恢复，批量账号上不该一把梭全清。
    /// 原版没有这个参数（它删到什么程度无从得知），这条是我们按需求加的。
    /// </remarks>
    public int DeleteMessageLimit { get; set; } = 20;

    /// <summary>
    /// 删除消息专用 Cookie 根目录（原版 <c>DeleteMsgCookieRootPath</c>）。
    /// </summary>
    /// <remarks>
    /// 原版这条任务的登录态**只从 cookie 来**，不像其他任务靠已登录的浏览器配置目录。
    /// 当前实现沿用浏览器的已登录态，这里的值只读出来写日志，不注入 ——
    /// cookie 注入是另一条登录路线，要和浏览器通道一起改，不在这一步开。
    /// 配了却没生效这件事必须让人看见，否则会以为自己在用 cookie 跑。
    /// </remarks>
    public string DeleteMessageCookieRootPath { get; set; } = string.Empty;

    /// <summary>
    /// 授权服务地址。S3 落地前为空，客户端据此判定为「服务未接入」。
    /// </summary>
    public string LicenseServerBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 开发期跳过激活直接进主界面。S3 完成后默认改 false，发布版必须为 false。
    /// </summary>
    public bool SkipActivationInDevelopment { get; set; } = true;

    /// <summary>日志保留天数，S9 部署时按此滚动。</summary>
    public int LogRetentionDays { get; set; } = 30;
}
