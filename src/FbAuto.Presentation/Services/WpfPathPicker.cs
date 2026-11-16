using System.IO;
using FbAuto.Application.Dialogs;
using Microsoft.Win32;

namespace FbAuto.Presentation.Services;

/// <summary>
/// 用系统原生对话框实现路径选择。整个项目里唯一碰 <c>Microsoft.Win32</c> 的地方。
/// </summary>
/// <remarks>
/// 起始位置的处理是这一层的全部价值：使用者点第二次「选择...」时，应该停在上次选的地方，
/// 而不是每次都从「文档」开始翻。所以传进来的是输入框里现有的值 ——
/// 它可能是个目录、可能是个文件、也可能是个已经不存在的路径（U 盘拔了、目录被改名），
/// 三种都得能用，第三种退回系统默认。
/// </remarks>
public sealed class WpfPathPicker : IPathPicker
{
    public string? Pick(PathPickerRequest request, string? initialPath)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            PathPickerKind.Folder => PickFolder(request, initialPath),
            _ => PickFile(request, initialPath),
        };
    }

    private static string? PickFolder(PathPickerRequest request, string? initialPath)
    {
        var dialog = new OpenFolderDialog
        {
            Title = request.Title,
            Multiselect = false,
        };

        if (PathStartFolder.Resolve(initialPath) is { } start)
        {
            dialog.InitialDirectory = start;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? PickFile(PathPickerRequest request, string? initialPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = request.Title,
            Filter = string.IsNullOrWhiteSpace(request.Filter) ? "所有文件 (*.*)|*.*" : request.Filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        if (PathStartFolder.Resolve(initialPath) is { } start)
        {
            dialog.InitialDirectory = start;

            // 已经填过某个具体文件时，把文件名也带上，省得重新在目录里找。
            if (File.Exists(initialPath?.Trim()))
            {
                dialog.FileName = Path.GetFileName(initialPath!.Trim());
            }
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
