namespace FbAuto.Browser.Chrome;

/// <summary>
/// Chrome 用 --remote-debugging-port=0 时，会把实际端口写进 user-data-dir 下的 DevToolsActivePort 文件。
/// 第一行是端口，第二行是浏览器级 ws 路径。这是拿到 ws 地址最稳的办法，比扫端口可靠。
/// </summary>
public static class DevToolsActivePortFile
{
    public const string FileName = "DevToolsActivePort";

    public static string GetPath(string profileDirectory) => Path.Combine(profileDirectory, FileName);

    /// <summary>读文件；不存在或内容不完整返回 null（调用方继续轮询）。</summary>
    public static DevToolsActivePort? TryRead(string profileDirectory)
    {
        try
        {
            var path = GetPath(profileDirectory);
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (IOException)
        {
            // Chrome 正在写，读到半截属于正常现象，下一轮再读
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DevToolsActivePort? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var lines = content
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        if (!int.TryParse(lines[0], out var port) || port is < 1 or > 65535)
        {
            return null;
        }

        var browserPath = lines[1];
        if (!browserPath.StartsWith('/'))
        {
            return null;
        }

        return new DevToolsActivePort(port, browserPath);
    }
}

/// <summary>DevToolsActivePort 的内容。</summary>
public sealed record DevToolsActivePort(int Port, string BrowserPath)
{
    /// <summary>拼出 CDP 的 ws 地址，例如 ws://127.0.0.1:51234/devtools/browser/xxxx。</summary>
    public string WebSocketUrl => $"ws://127.0.0.1:{Port}{BrowserPath}";

    /// <summary>探活用：拿端口问 /json/version。</summary>
    public string VersionUrl => $"http://127.0.0.1:{Port}/json/version";
}
