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

    /// <summary>视频所在目录。</summary>
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

    /// <summary>删除消息的屏蔽词，默认与原版一致。</summary>
    public string[] MessageBlockWords { get; set; } = [".com", "https", "pn", "dn", "inbox"];

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
