namespace FbAuto.Licensing;

/// <summary>
/// Crockford Base32 编解码。
/// 选它而不是 Base64 的理由：字母表里去掉了 I、L、O、U 四个易混字符，
/// 用户抄码时不会把 1 看成 I、把 0 看成 O；解码时这几个字符仍按 1 和 0 接受，容错。
/// 全程不产生小写，Windows 上复制粘贴不会被输入法或剪贴板改写。
/// </summary>
public static class Base32Crockford
{
    /// <summary>编码字母表：0-9 加去掉 ILOU 的 22 个字母，共 32 个。</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>把字节编码成 Base32 文本，不含分隔符。</summary>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        // 每 5 位一个字符，向上取整
        var charCount = (data.Length * 8 + 4) / 5;
        var chars = new char[charCount];
        var buffer = 0;
        var bits = 0;
        var index = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                chars[index++] = Alphabet[(buffer >> bits) & 0x1F];
            }
        }

        if (bits > 0)
        {
            chars[index++] = Alphabet[(buffer << (5 - bits)) & 0x1F];
        }

        // index 恒等于 charCount；这里不裁剪，长度不对外撒谎
        return new string(chars, 0, index);
    }

    /// <summary>
    /// 解码 Base32 文本。忽略连字符、空白与大小写，I/L 按 1、O 按 0 处理。
    /// </summary>
    /// <exception cref="FormatException">出现字母表以外的字符，或尾部填充位非零。</exception>
    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = new List<byte>(text.Length * 5 / 8 + 1);
        var buffer = 0;
        var bits = 0;

        foreach (var raw in text)
        {
            // 连字符与空白只是给人看的分隔，不影响内容
            if (raw is '-' or ' ' or '\t' or '\r' or '\n')
            {
                continue;
            }

            var value = CharValue(raw);
            if (value < 0)
            {
                throw new FormatException($"授权码含有非法字符：'{raw}'");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        // 尾部不足 8 位的部分必须全为 0，否则说明这串字符本身是残缺的
        if (bits > 0 && (buffer & ((1 << bits) - 1)) != 0)
        {
            throw new FormatException("授权码尾部校验位异常，可能被截断或抄错");
        }

        return bytes.ToArray();
    }

    /// <summary>返回字符对应的 5 位值，非法字符返回 -1。</summary>
    private static int CharValue(char c)
    {
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }

        // 容错：O 按 0，I / L 按 1
        if (c is 'O' or 'o')
        {
            return 0;
        }

        if (c is 'I' or 'i' or 'L' or 'l')
        {
            return 1;
        }

        var upper = char.ToUpperInvariant(c);
        var index = Alphabet.IndexOf(upper);
        return index;
    }
}
