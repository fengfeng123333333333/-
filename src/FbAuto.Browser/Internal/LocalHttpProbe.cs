namespace FbAuto.Browser.Internal;

/// <summary>
/// 探活用的本机 HTTP 探针。
/// 必须显式关闭代理：本机注入的 HTTP(S)_PROXY 会把 127.0.0.1 的请求也送去代理，
/// 结果是浏览器明明活着却探测失败（或反之），这种误判很难查。
/// </summary>
internal static class LocalHttpProbe
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<bool> IsReachableAsync(string url, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }
}
