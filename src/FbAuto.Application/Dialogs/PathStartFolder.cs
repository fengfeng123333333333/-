using System.IO;

namespace FbAuto.Application.Dialogs;

/// <summary>
/// 把输入框里现在那段文字，变成对话框能用的起始目录。
/// </summary>
/// <remarks>
/// 看起来只是个小工具，但它是「点第二次选择...还停在上次的地方」这件事的全部实现，
/// 而输入框里可能是三种完全不同的东西：一个存在的目录、一个存在的文件、一个已经不存在的路径
/// （U 盘拔了、目录被改名、从别的机器拷来的配置）。三种都得有确定行为，所以放在可测的层里。
///
/// 认不出来时返回 <c>null</c> 而不是硬塞：给对话框一个不存在的路径，它会退到「此电脑」根上，
/// 比系统的默认位置还难用。
/// </remarks>
public static class PathStartFolder
{
    /// <summary>推导起始目录；<c>null</c> 表示交给系统默认。</summary>
    public static string? Resolve(string? current)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return null;
        }

        var trimmed = current.Trim();

        if (Directory.Exists(trimmed))
        {
            return trimmed;
        }

        if (File.Exists(trimmed))
        {
            // 选的是文件，就停在它所在的目录，并把文件名带上（见调用方）。
            return Path.GetDirectoryName(trimmed);
        }

        return null;
    }
}
