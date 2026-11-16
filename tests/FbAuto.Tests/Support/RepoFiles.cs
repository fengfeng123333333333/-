using System.IO;

namespace FbAuto.Tests.Support;

/// <summary>
/// 按仓库根拼路径，用来读产品源码（XAML / ViewModel）。
/// </summary>
/// <remarks>
/// 用 <c>FbAuto.slnx</c> 当锚点往上找 —— 测试输出在 <c>build/out/...</c>，改输出路径也不会失效。
/// 找不到就直接抛：静默跳过等于那条守卫用例不存在，比红着还危险。
/// </remarks>
public static class RepoFiles
{
    public static string Resolve(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "FbAuto.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException($"从 {AppContext.BaseDirectory} 往上没找到 FbAuto.slnx");
        }

        var path = System.IO.Path.Combine([dir.FullName, .. relative]);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"没找到 {path}");
        }

        return path;
    }
}
