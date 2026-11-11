using System.Text.Json;
using FbAuto.Browser.Abstractions;

namespace FbAuto.Browser.Bit;

/// <summary>比特接口打开窗口后返回的东西。ws 是必须的，pid/http 用来探活与核对。</summary>
public sealed record BitBrowserOpenResult(
    string WsEndpoint,
    string? HttpEndpoint,
    int? ProcessId,
    string? Directory,
    string? Name);

/// <summary>
/// 比特本地接口的返回解析。单独抽出来是因为这套接口不同版本字段不一（code/success、msg/message），
/// 出错时还要给出能看懂的中文提示，这部分逻辑必须能被单测覆盖。
/// </summary>
public static class BitApiParser
{
    /// <summary>解析 /browser/open 的返回。失败抛中文异常，带上比特自己给的 msg。</summary>
    public static BitBrowserOpenResult ParseOpen(string json)
    {
        var envelope = ParseEnvelope(json, "打开窗口");

        if (!envelope.Success)
        {
            throw BrowserChannelException.ApiRejected("打开窗口", envelope.Code, envelope.Message);
        }

        if (envelope.Data is not JsonElement data)
        {
            throw BrowserChannelException.ApiRejected("打开窗口", envelope.Code, "返回里没有 data 字段");
        }

        var ws = ReadString(data, "ws");
        if (string.IsNullOrWhiteSpace(ws))
        {
            throw BrowserChannelException.ApiRejected("打开窗口", envelope.Code, "返回里没有 ws 地址");
        }

        return new BitBrowserOpenResult(
            WsEndpoint: ws,
            HttpEndpoint: ReadString(data, "http"),
            ProcessId: ReadInt(data, "pid"),
            Directory: ReadString(data, "dir"),
            Name: ReadString(data, "name"));
    }

    /// <summary>只校验成功与否，用于 close 这类不需要返回体的动作。</summary>
    public static void EnsureSuccess(string json, string action)
    {
        var envelope = ParseEnvelope(json, action);
        if (!envelope.Success)
        {
            throw BrowserChannelException.ApiRejected(action, envelope.Code, envelope.Message);
        }
    }

    /// <summary>
    /// 统一外壳：兼容 code==0 与 success==true 两种写法。
    /// 两者都没有时，退回「有 data 就算成功」——老版本接口就是这样。
    /// </summary>
    public static BitApiEnvelope ParseEnvelope(string json, string action)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw BrowserChannelException.ApiRejected(action, null, "接口返回空内容");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            throw BrowserChannelException.ApiRejected(action, null, $"返回内容不是合法 JSON：{Truncate(json)}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw BrowserChannelException.ApiRejected(action, null, $"返回内容不是 JSON 对象：{Truncate(json)}");
            }

            int? code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                ? codeElement.GetInt32()
                : null;

            bool success;
            if (root.TryGetProperty("success", out var successElement)
                && successElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                success = successElement.GetBoolean();
            }
            else if (code.HasValue)
            {
                success = code.Value == 0;
            }
            else
            {
                success = root.TryGetProperty("data", out _);
            }

            var message = ReadString(root, "msg") ?? ReadString(root, "message");

            JsonElement? data = root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object
                ? dataElement.Clone()
                : null;

            return new BitApiEnvelope(success, code, message, data);
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    private static string Truncate(string text)
        => text.Length <= 200 ? text : text[..200] + "…";
}

/// <summary>比特接口的通用外壳。</summary>
public sealed record BitApiEnvelope(bool Success, int? Code, string? Message, JsonElement? Data);
