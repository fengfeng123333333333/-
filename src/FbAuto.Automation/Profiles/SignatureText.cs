namespace FbAuto.Automation.Profiles;

/// <summary>签名文本的校验结论。</summary>
public enum SignatureVerdict
{
    /// <summary>可以写入。</summary>
    Ok,

    /// <summary>内容为空，跳过这一项（不算失败）。</summary>
    Empty,

    /// <summary>超过长度上限。</summary>
    TooLong,
}

/// <summary>校验结果。</summary>
/// <param name="Verdict">结论。</param>
/// <param name="Text">清理后的文本（已去首尾空白）。</param>
/// <param name="Message">写进日志的中文说明。</param>
public sealed record SignatureCheck(SignatureVerdict Verdict, string Text, string Message)
{
    public bool ShouldWrite => Verdict == SignatureVerdict.Ok;
}

/// <summary>
/// 签名的长度与空值校验。
/// </summary>
/// <remarks>
/// 为什么要单独校验：签名输入框是 <c>textarea[maxlength='255']</c>，
/// **超长时浏览器直接截断，不报错**。程序会以为写进去了，结果页面上是半句话。
/// 这种「静默截断」比报错难查得多，所以在写入前先拦。
///
/// 空内容不当失败：签名文件是可选输入，没配就跳过这一项，
/// 不该让整条资料修改任务因此判失败。
/// </remarks>
public static class SignatureText
{
    /// <summary>与页面 <c>maxlength</c> 一致。</summary>
    public const int MaxLength = 255;

    public static SignatureCheck Check(string? raw)
    {
        var text = (raw ?? string.Empty).Trim();

        if (text.Length == 0)
        {
            return new SignatureCheck(SignatureVerdict.Empty, string.Empty, "签名为空，跳过该项");
        }

        if (text.Length > MaxLength)
        {
            return new SignatureCheck(
                SignatureVerdict.TooLong,
                text,
                $"签名长度 {text.Length} 超过上限 {MaxLength}，已跳过（页面会静默截断，不能硬写）");
        }

        return new SignatureCheck(SignatureVerdict.Ok, text, "签名内容校验通过");
    }
}
