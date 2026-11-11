using FbAuto.Browser.Abstractions;

namespace FbAuto.Browser.Chrome;

/// <summary>
/// Chrome 启动参数拼装。纯函数，便于单测——参数错一个字母，调试端口就不出来，
/// 而这在真机上表现为「浏览器起来了但连不上」，很容易被误判成网络问题。
/// </summary>
public static class ChromeOptionsBuilder
{
    /// <summary>固定给 0，让系统分配空闲端口，多开不会撞车。</summary>
    public const string DebugPortArgument = "--remote-debugging-port=0";

    public static IReadOnlyList<string> Build(BrowserLaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileDirectory))
        {
            throw new ArgumentException("Chrome 通道必须指定 user-data-dir（每个账号目录独立）", nameof(request));
        }

        var args = new List<string>
        {
            $"--user-data-dir={TrimTrailingSeparator(request.ProfileDirectory)}",
            DebugPortArgument,
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-popup-blocking",
        };

        if (request.Headless)
        {
            args.Add("--headless=new");
        }

        if (!string.IsNullOrWhiteSpace(request.Proxy))
        {
            args.Add($"--proxy-server={NormalizeProxy(request.Proxy)}");
        }

        foreach (var extra in request.ExtraArguments)
        {
            if (!string.IsNullOrWhiteSpace(extra))
            {
                args.Add(extra.Trim());
            }
        }

        return args;
    }

    /// <summary>代理写法统一成带 scheme 的形式，Chrome 认 http://host:port。</summary>
    public static string NormalizeProxy(string proxy)
    {
        var value = proxy.Trim();
        return value.Contains("://", StringComparison.Ordinal) ? value : $"http://{value}";
    }

    private static string TrimTrailingSeparator(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
