using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FbAuto.Tests.Support;

/// <summary>
/// 极简 HTTP 桩服务，用裸 TcpListener 实现。
/// 不用 HttpListener：那玩意在非管理员下对 127.0.0.1 前缀会拒绝监听，测试会因权限而红，和环境无关。
/// </summary>
public sealed class FakeHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<string, string> _responder;
    private readonly Task _acceptLoop;

    /// <param name="responder">按请求路径返回响应体（JSON 字符串）。</param>
    public FakeHttpServer(Func<string, string> responder)
    {
        _responder = responder;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public ConcurrentQueue<(string Path, string Body)> Requests { get; } = new();

    public IReadOnlyList<string> RequestedPaths => [.. Requests.Select(r => r.Path)];

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();

        try
        {
            await _acceptLoop;
        }
        catch
        {
            // 关停期间的异常不影响测试结论
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    return;
                }

                Requests.Enqueue((request.Value.Path, request.Value.Body));

                var payload = Encoding.UTF8.GetBytes(_responder(request.Value.Path));
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json; charset=utf-8\r\n"
                    + $"Content-Length: {payload.Length}\r\n"
                    + "Connection: close\r\n\r\n");

                await stream.WriteAsync(header);
                await stream.WriteAsync(payload);
                await stream.FlushAsync();
            }
        }
        catch (IOException)
        {
            // 客户端提前断开，忽略
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static async Task<(string Path, string Body)?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var builder = new StringBuilder();
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return null;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            headerEnd = builder.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal);
        }

        var text = builder.ToString();
        var head = text[..headerEnd];
        var body = text[(headerEnd + 4)..];

        var contentLength = 0;
        foreach (var line in head.Split("\r\n").Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0
                && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), out var parsed))
            {
                contentLength = parsed;
            }
        }

        while (Encoding.UTF8.GetByteCount(body) < contentLength)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            body += Encoding.UTF8.GetString(buffer, 0, read);
        }

        var requestLine = head.Split("\r\n")[0].Split(' ');
        var path = requestLine.Length > 1 ? requestLine[1] : "/";

        // 统一成不含查询串的路径，便于断言
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        return (path, body);
    }
}
