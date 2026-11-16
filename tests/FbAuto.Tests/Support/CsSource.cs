using System.Text.RegularExpressions;

namespace FbAuto.Tests.Support;

/// <summary>
/// 从 C# 源码文本里取一个方法的方法体，并去掉注释。
/// </summary>
/// <remarks>
/// 用途是「文本级守卫」：WPF 的绑定写错、写回漏项、命令参数拼错，全都不报编译错误，
/// 只能把 XAML 和 ViewModel 源码当文本对一遍。而把源码当文本读就必须处理两件事：
///
/// <list type="number">
/// <item>**按大括号配对切方法体，不按缩进** —— 缩进会被人重排。</item>
/// <item>**先剥注释再匹配** —— 这条是注入实验逼出来的（见 S15 的 J5）：
/// 把 <c>// config.Xxx = ...;</c> 注释掉之后，正则照样匹配那行注释，守卫全绿。
/// 守卫分不清「写了」和「写着但注释了」，等于在最该管用的那一次不管用。</item>
/// </list>
/// </remarks>
public static class CsSource
{
    /// <summary>
    /// 取出以 <paramref name="signature"/> 开头的方法体（含外侧大括号），并剥掉注释。
    /// </summary>
    /// <param name="source">整个源文件的内容。</param>
    /// <param name="signature">方法签名里足够唯一定位的一段，如 <c>private void ApplyPath(</c>。</param>
    public static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException($"源码里找不到方法签名「{signature}」");
        }

        var open = source.IndexOf('{', start);

        if (open < 0)
        {
            throw new InvalidOperationException($"方法「{signature}」后面没有方法体");
        }

        var depth = 0;
        var end = -1;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    break;
                }
            }
        }

        if (end < 0)
        {
            throw new InvalidOperationException($"方法「{signature}」的大括号没有配对");
        }

        return StripComments(source[open..(end + 1)]);
    }

    /// <summary>
    /// 逐行去注释：整行注释直接丢，行尾注释切掉。
    /// 方法体里没有含 <c>//</c> 的字符串字面量（路径都由参数传进来），所以按 <c>//</c> 切是安全的。
    /// </summary>
    private static string StripComments(string body)
    {
        var lines = body
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Select(line =>
            {
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                return comment >= 0 ? line[..comment] : line;
            });

        return string.Join('\n', lines);
    }

    /// <summary>收一个方法体里所有 <c>nameof(Xxx)</c> 取到的名字。</summary>
    public static HashSet<string> Nameofs(string body) =>
        Regex.Matches(body, @"nameof\(\s*(\w+)\s*\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
}
