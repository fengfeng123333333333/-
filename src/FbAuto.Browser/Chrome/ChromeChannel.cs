using System.Diagnostics;
using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Internal;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;
using Microsoft.Playwright;

namespace FbAuto.Browser.Chrome;

/// <summary>
/// 谷歌版通道：自己拉起 Chrome，独立 user-data-dir，用 --remote-debugging-port=0 让系统分配端口，
/// 再从 DevToolsActivePort 读 ws 地址，用 ConnectOverCDP 接管——不重开浏览器，登录态还在。
/// </summary>
public sealed class ChromeChannel : IBrowserChannel, IAsyncDisposable
{
    /// <summary>端口轮询间隔。150ms 足够快，又不至于把磁盘读爆。</summary>
    private const int PortPollIntervalMilliseconds = 150;

    private readonly IAppLogger _logger;
    private readonly string? _configuredExecutablePath;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <param name="logger">日志。</param>
    /// <param name="executablePath">config.json 里的 ChromePath；为空则每次自动查找。</param>
    public ChromeChannel(IAppLogger logger, string? executablePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuredExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath;
    }

    public BrowserChannelKind Kind => BrowserChannelKind.Google;

    public async Task<BrowserSession> LaunchAsync(BrowserLaunchRequest request, CancellationToken cancellationToken = default)
    {
        var executable = ChromeLocator.Locate(request.ExecutablePath ?? _configuredExecutablePath);

        if (string.IsNullOrWhiteSpace(request.ProfileDirectory))
        {
            throw new BrowserChannelException(
                BrowserChannelFailure.LaunchFailed,
                "Chrome 通道必须指定 profile 目录（user-data-dir），否则会和用户日常使用的浏览器串在一起。");
        }

        Directory.CreateDirectory(request.ProfileDirectory);

        // 上一轮残留的端口文件会让这一轮读到旧端口，连到一个已经关掉的浏览器上，必须先清掉
        TryDelete(DevToolsActivePortFile.GetPath(request.ProfileDirectory));

        var arguments = ChromeOptionsBuilder.Build(request);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = request.ProfileDirectory,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                      ?? throw BrowserChannelException.LaunchFailed("Process.Start 返回空，未能创建进程。");
        }
        catch (BrowserChannelException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw BrowserChannelException.LaunchFailed(ex.Message, ex);
        }

        _logger.Info($"Chrome 已启动：PID={process.Id}，profile={request.ProfileDirectory}，可执行文件={executable}");

        var deadline = DateTime.UtcNow + request.LaunchTimeout;
        try
        {
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (process.HasExited)
                {
                    throw BrowserChannelException.LaunchFailed(
                        $"Chrome 启动后立即退出（退出码 {process.ExitCode}）。常见原因：该 profile 已被另一个实例占用，或启动参数不被当前版本支持。");
                }

                var activePort = DevToolsActivePortFile.TryRead(request.ProfileDirectory);
                if (activePort is not null)
                {
                    _logger.Info($"调试端口就绪：{activePort.Port}");
                    return new BrowserSession
                    {
                        Kind = Kind,
                        ProcessId = process.Id,
                        DebugPort = activePort.Port,
                        WsEndpoint = activePort.WebSocketUrl,
                        ProfileDirectory = request.ProfileDirectory,
                    };
                }

                await Task.Delay(PortPollIntervalMilliseconds, cancellationToken);
            }
        }
        catch
        {
            // 自己起的实例自己收，绝不留孤儿进程占着 profile
            KillTree(process.Id);
            throw;
        }

        KillTree(process.Id);
        throw BrowserChannelException.DebugEndpointTimeout(
            request.ProfileDirectory,
            (int)request.LaunchTimeout.TotalSeconds,
            process.Id);
    }

    public Task<string> GetWsEndpointAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(session.WsEndpoint))
        {
            return Task.FromResult(session.WsEndpoint);
        }

        var activePort = DevToolsActivePortFile.TryRead(session.ProfileDirectory);
        if (activePort is null)
        {
            throw BrowserChannelException.SessionClosed();
        }

        return Task.FromResult(activePort.WebSocketUrl);
    }

    public async Task<IBrowser> ConnectAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        var wsEndpoint = await GetWsEndpointAsync(session, cancellationToken);

        _playwright ??= await CdpConnector.CreatePlaywrightAsync();
        _browser = await CdpConnector.ConnectAsync(_playwright, wsEndpoint);
        _logger.Info($"已接管 Chrome（CDP）：{wsEndpoint}");
        return _browser;
    }

    public Task<IPage> NewPageAsync(IBrowser browser, CancellationToken cancellationToken = default)
        => CdpConnector.NewPageAsync(browser);

    public async Task<bool> IsAliveAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        // 条件一：进程还在
        if (session.ProcessId is not int processId || !IsProcessAlive(processId))
        {
            return false;
        }

        // 条件二：调试端口还能应答。只看进程会把「进程僵着但 CDP 已断」判成活
        if (session.DebugPort is int port)
        {
            var url = $"http://127.0.0.1:{port}/json/version";
            return await LocalHttpProbe.IsReachableAsync(url, TimeSpan.FromSeconds(2), cancellationToken);
        }

        // 没有端口信息时只能退回进程判定，但这种情况本方法的调用方应视为未知
        _logger.Warn($"会话缺少调试端口信息，仅按进程判定存活：PID={processId}");
        return true;
    }

    public async Task CloseAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        await ResetConnectionAsync();

        if (session.ProcessId is int processId)
        {
            // 只关自己起的这个 PID（含子进程）。绝不按进程名杀 chrome.exe——用户自己的浏览器会被一起干掉
            KillTree(processId);
            _logger.Info($"已关闭自己启动的 Chrome：PID={processId}");
            return;
        }

        _logger.Warn("会话没有 PID，无法按 PID 关闭；为避免误杀用户浏览器，此处不做任何进程操作。");
    }

    public async ValueTask DisposeAsync() => await ResetConnectionAsync();

    private async Task ResetConnectionAsync()
    {
        if (_browser is not null)
        {
            try
            {
                // 只断开连接；不调 CloseAsync，否则会把整个浏览器连同用户正在用的窗口一起关掉
                await _browser.DisposeAsync();
            }
            catch
            {
                // 连接已经断了，无所谓
            }

            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    private void KillTree(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch (ArgumentException)
        {
            // 进程已经不在了
        }
        catch (InvalidOperationException)
        {
            // 进程刚退出
        }
        catch (Exception ex)
        {
            _logger.Warn($"按 PID 关闭 Chrome 失败：PID={processId}，{ex.Message}");
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 删不掉就让轮询逻辑自己处理：读到的端口若连不上，IsAlive 会暴露出来
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
