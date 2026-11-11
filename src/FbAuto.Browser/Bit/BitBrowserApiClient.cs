using System.Net.Http.Json;
using System.Text.Json;
using FbAuto.Browser.Abstractions;

namespace FbAuto.Browser.Bit;

/// <summary>
/// 比特浏览器本地接口客户端（默认 http://127.0.0.1:54345）。
/// 两点必须注意：① 本机请求不能走系统代理，否则连 127.0.0.1 都会被送去代理；
/// ② 客户端没开时要报中文原因，而不是抛一个 HttpRequestException 让用户看不懂。
/// </summary>
public sealed class BitBrowserApiClient : IDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:54345";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public BitBrowserApiClient(string? baseUrl = null, HttpClient? httpClient = null)
    {
        BaseUrl = NormalizeBaseUrl(baseUrl);

        if (httpClient is null)
        {
            _http = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                Timeout = TimeSpan.FromSeconds(15),
            };
            _ownsHttpClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttpClient = false;
        }
    }

    public string BaseUrl { get; }

    /// <summary>按 profileId 打开窗口，返回 ws / http / pid。</summary>
    public async Task<BitBrowserOpenResult> OpenAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var json = await PostAsync("browser/open", "打开窗口", profileId, cancellationToken);
        return BitApiParser.ParseOpen(json);
    }

    /// <summary>关闭指定窗口。只关这一个，不动其他窗口。</summary>
    public async Task CloseAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var json = await PostAsync("browser/close", "关闭窗口", profileId, cancellationToken);
        BitApiParser.EnsureSuccess(json, "关闭窗口");
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>把 127.0.0.1:54345 这类写法补齐成完整 URL。</summary>
    public static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return DefaultBaseUrl;
        }

        var value = baseUrl.Trim().TrimEnd('/');
        return value.Contains("://", StringComparison.Ordinal) ? value : $"http://{value}";
    }

    private async Task<string> PostAsync(string relativePath, string action, string profileId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/{relativePath}";

        try
        {
            using var response = await _http.PostAsJsonAsync(
                url,
                new Dictionary<string, string> { ["id"] = profileId },
                cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body))
            {
                throw BrowserChannelException.ApiRejected(action, (int)response.StatusCode, response.ReasonPhrase);
            }

            return body;
        }
        catch (HttpRequestException ex)
        {
            throw BrowserChannelException.ClientNotRunning(BaseUrl, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw BrowserChannelException.ClientNotRunning(BaseUrl, ex);
        }
        catch (JsonException ex)
        {
            throw BrowserChannelException.ApiRejected(action, null, $"请求体序列化失败：{ex.Message}");
        }
    }
}
