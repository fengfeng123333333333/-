using System.Diagnostics;
using FbAuto.Browser.Abstractions;
using FbAuto.Browser.Internal;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;
using Microsoft.Playwright;

namespace FbAuto.Browser.Bit;

/// <summary>
/// 比特版通道：窗口由比特客户端管理，我们只调本地接口按 profileId 打开，再 ConnectOverCDP 接管。
/// 不再去拼 /json/version 猜 ws——接口已经把 ws 给出来了，自己拼等于多制造一个出错点。
/// </summary>
public sealed class BitBrowserChannel : IBrowserChannel, IAsyncDisposable
{
    private readonly IAppLogger _logger;
    private readonly BitBrowserApiClient _client;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public BitBrowserChannel(IAppLogger logger, BitBrowserApiClient? client = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? new BitBrowserApiClient();
    }

    public BrowserChannelKind Kind => BrowserChannelKind.Bit;

    public async Task<BrowserSession> LaunchAsync(BrowserLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BitProfileId))
        {
            throw BrowserChannelException.ProfileNotFound(request.BitProfileId);
        }

        _logger.Info($"正在通过比特接口打开窗口：{request.BitProfileId}（接口 {_client.BaseUrl}）");

        var opened = await _client.OpenAsync(request.BitProfileId, cancellationToken);

        _logger.Info($"比特窗口已打开：PID={opened.ProcessId?.ToString() ?? "未知"}，http={opened.HttpEndpoint ?? "未知"}");

        return new BrowserSession
        {
            Kind = Kind,
            ProcessId = opened.ProcessId,
            WsEndpoint = opened.WsEndpoint,
            HttpEndpoint = opened.HttpEndpoint,
            ProfileDirectory = opened.Directory ?? string.Empty,
            BitProfileId = request.BitProfileId,
        };
    }

    public Task<string> GetWsEndpointAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.WsEndpoint))
        {
            throw BrowserChannelException.SessionClosed();
        }

        return Task.FromResult(session.WsEndpoint);
    }

    public async Task<IBrowser> ConnectAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        var wsEndpoint = await GetWsEndpointAsync(session, cancellationToken);

        _playwright ??= await CdpConnector.CreatePlaywrightAsync();
        _browser = await CdpConnector.ConnectAsync(_playwright, wsEndpoint);
        _logger.Info($"已接管比特窗口（CDP）：{wsEndpoint}");
        return _browser;
    }

    public Task<IPage> NewPageAsync(IBrowser browser, CancellationToken cancellationToken = default)
        => CdpConnector.NewPageAsync(browser);

    public async Task<bool> IsAliveAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        // 条件一：进程在（比特有 pid 时才判）
        if (session.ProcessId is int processId && !IsProcessAlive(processId))
        {
            return false;
        }

        // 条件二：窗口的调试端口能应答。接口说活着但窗口已被手动关掉，靠这一条兜住
        if (!string.IsNullOrWhiteSpace(session.HttpEndpoint))
        {
            var url = BuildVersionUrl(session.HttpEndpoint);
            return await LocalHttpProbe.IsReachableAsync(url, TimeSpan.FromSeconds(2), cancellationToken);
        }

        if (session.ProcessId is null)
        {
            _logger.Warn("比特返回里没有 pid 也没有 http 端点，无法确认窗口存活");
            return false;
        }

        return true;
    }

    public async Task CloseAsync(BrowserSession session, CancellationToken cancellationToken = default)
    {
        await ResetConnectionAsync();

        if (string.IsNullOrWhiteSpace(session.BitProfileId))
        {
            _logger.Warn("会话缺少比特窗口 ID，无法调接口关闭；不做任何进程操作以免误杀窗口。");
            return;
        }

        try
        {
            await _client.CloseAsync(session.BitProfileId, cancellationToken);
            _logger.Info($"已通过比特接口关闭窗口：{session.BitProfileId}");
        }
        catch (BrowserChannelException ex)
        {
            // 关失败不影响主流程：窗口留在那儿比抛异常打断任务清理更好排查
            _logger.Warn($"关闭比特窗口失败：{ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetConnectionAsync();
        _client.Dispose();
    }

    private async Task ResetConnectionAsync()
    {
        if (_browser is not null)
        {
            try
            {
                await _browser.DisposeAsync();
            }
            catch
            {
                // 断开即可，窗口由比特客户端管理
            }

            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
    }

    private static string BuildVersionUrl(string httpEndpoint)
    {
        var endpoint = httpEndpoint.Trim().TrimEnd('/');
        if (!endpoint.Contains("://", StringComparison.Ordinal))
        {
            endpoint = $"http://{endpoint}";
        }

        return $"{endpoint}/json/version";
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
}
