namespace FbAuto.Licensing;

/// <summary>
/// 授权码文本与对象之间的转换。文本 = Base32(负载 11 字节 + 签名 64 字节) = 120 字符。
/// </summary>
public static class LicenseCodeCodec
{
    /// <summary>编码成可粘贴的文本。</summary>
    public static string Encode(LicenseCode code)
    {
        ArgumentNullException.ThrowIfNull(code);

        if (code.Signature.Length != LicenseCodeLayout.SignatureLength)
        {
            throw new ArgumentException(
                $"签名必须是 {LicenseCodeLayout.SignatureLength} 字节，当前 {code.Signature.Length}",
                nameof(code));
        }

        var payload = code.ToPayloadBytes();
        var all = new byte[LicenseCodeLayout.TotalLength];
        payload.CopyTo(all, 0);
        code.Signature.CopyTo(all, LicenseCodeLayout.PayloadLength);
        return Base32Crockford.Encode(all);
    }

    /// <summary>
    /// 解析文本。这里只管结构，不管真伪——真伪交给验签。
    /// 失败一律抛 <see cref="FormatException"/>，消息可直接展示给用户。
    /// </summary>
    public static LicenseCode Decode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("授权码为空，请先粘贴完整的授权码");
        }

        // 先按字符数判断，再解码。
        // 顺序很重要：少一个字符时若直接解码，尾部补齐位会非零，用户会看到「校验位异常」这种
        // 完全不知道该怎么办的提示。先数长度才能告诉他「少复制了一段」。
        var compactLength = 0;
        foreach (var c in text)
        {
            if (c is not ('-' or ' ' or '\t' or '\r' or '\n'))
            {
                compactLength++;
            }
        }

        if (compactLength != LicenseCodeLayout.TextLength)
        {
            throw new FormatException(
                $"授权码长度不对：应为 {LicenseCodeLayout.TextLength} 个字符，实际 {compactLength} 个。" +
                "通常是复制时少了一段或多了一段，请整段重新复制。");
        }

        var bytes = Base32Crockford.Decode(text);
        if (bytes.Length != LicenseCodeLayout.TotalLength)
        {
            throw new FormatException(
                $"授权码长度不对：解出 {bytes.Length} 字节，应为 {LicenseCodeLayout.TotalLength}。" +
                "通常是复制时少了一段或多了一段，请整段重新复制。");
        }

        var version = bytes[0];
        if (version != LicenseCodeLayout.CurrentVersion)
        {
            throw new FormatException(
                $"授权码格式版本 {version} 与本程序（{LicenseCodeLayout.CurrentVersion}）不匹配，" +
                "请向发码方索取匹配当前版本的授权码。");
        }

        var payload = bytes.AsSpan(0, LicenseCodeLayout.PayloadLength);
        var signature = bytes.AsSpan(LicenseCodeLayout.PayloadLength);
        var code = LicenseCode.FromPayload(payload, signature);

        if (code.ValidDays <= 0)
        {
            throw new FormatException("授权码的有效天数为 0，这张码没有意义，请联系发码方重新签发");
        }

        return code;
    }

    /// <summary>
    /// 把 120 字符切成每 5 个一组，中间加连字符，便于人工核对首尾。
    /// 只影响显示，<see cref="Decode"/> 会自动忽略连字符。
    /// </summary>
    public static string ToGrouped(string text, int groupSize = 5)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (groupSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(groupSize), groupSize, "分组大小必须大于 0");
        }

        var compact = text.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        var groups = new List<string>(compact.Length / groupSize + 1);
        for (var i = 0; i < compact.Length; i += groupSize)
        {
            groups.Add(compact.Substring(i, Math.Min(groupSize, compact.Length - i)));
        }

        return string.Join('-', groups);
    }
}
