using FbAuto.Browser.Abstractions;
using Microsoft.Win32;

namespace FbAuto.Browser.Chrome;

/// <summary>
/// 找 chrome.exe。顺序：显式配置 → 常见安装位置 → 注册表 App Paths。
/// 显式配置了但不存在就直接报错，不偷偷换成别的浏览器——用户指定了哪个就用哪个。
/// </summary>
public static class ChromeLocator
{
    public const string ExecutableName = "chrome.exe";

    public static string Locate(string? preferredPath = null)
    {
        var found = TryLocate(preferredPath);
        if (found is null)
        {
            throw BrowserChannelException.ExecutableNotFound(preferredPath);
        }

        return found;
    }

    public static string? TryLocate(string? preferredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            return File.Exists(preferredPath) ? preferredPath : null;
        }

        foreach (var candidate in DefaultCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return ReadFromRegistry();
    }

    /// <summary>常见安装位置，按 64 位优先。</summary>
    public static IEnumerable<string> DefaultCandidates()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrEmpty(programFiles))
        {
            yield return Path.Combine(programFiles, "Google", "Chrome", "Application", ExecutableName);
        }

        if (!string.IsNullOrEmpty(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Google", "Chrome", "Application", ExecutableName);
        }

        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "Google", "Chrome", "Application", ExecutableName);
        }
    }

    /// <summary>注册表 App Paths 是 Chrome 安装时自己写的，比猜路径可靠。</summary>
    private static string? ReadFromRegistry()
    {
        const string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + ExecutableName;

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey);
                    if (key?.GetValue(null) is string path && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch
                {
                    // 注册表读不到就继续找下一个，不因为权限问题打断启动流程
                }
            }
        }

        return null;
    }
}
