using System.Text;

namespace FbAuto.Application.Execution;

/// <summary>
/// 「资料修改结果保存目录」里那份账号失败清单。
/// </summary>
/// <remarks>
/// 样式取自原版字符串（<c>_analysis/u16_all.txt</c> 2771-2795 行）：
/// <code>
///   settings.json / Logs / FaceBookAuto_ / yyyyMMdd / .log
///   AccountErrors_ / ] [账号失败] 浏览器: / | assetId: / | 原因:
/// </code>
/// 即文件名 <c>AccountErrors_yyyyMMdd.txt</c>，一行一条
/// <c>[时间] [账号失败] 浏览器:X | assetId:Y | 原因:Z</c>，时间格式与日志同一套
/// <c>yyyy-MM-dd HH:mm:ss.fff</c>。
///
/// 两处必须说明的取舍：
/// 1. **扩展名**：原版 DLL 里没有独立的 <c>.txt</c> 字面量（只有文件对话框的
///    <c>文本文件 (*.txt)|*.txt|...</c>），所以 <c>.txt</c> 是按「纯文本清单」推断的，
///    已在 S14 文档的「未完成项」里标明，需真机核对。
/// 2. **写盘策略**：原版只暴露了 <c>WriteAllText</c>（整体覆盖），没有 <c>AppendAllText</c>。
///    并发任务逐个「读-改-写」会互相覆盖，所以这里先在内存里攒，一轮结束一次性写；
///    写之前把当天已有内容读回来接在前面，避免同一天第二次执行把第一次的记录冲掉。
/// </remarks>
public sealed class AccountErrorLog
{
    /// <summary>文件名前缀，与日期一起拼成 <c>AccountErrors_20261116.txt</c>。</summary>
    public const string FilePrefix = "AccountErrors_";

    /// <summary>每条记录的行内标记，原版串原样照抄。</summary>
    public const string Tag = "[账号失败]";

    private readonly Dictionary<string, List<string>> _byDirectory = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已记下的条数（不分目录）。</summary>
    public int Count { get; private set; }

    /// <summary>按日期算出的文件名。日期用本地时间——使用者是按本机日期找文件的。</summary>
    public static string FileNameFor(DateTime when) => $"{FilePrefix}{when:yyyyMMdd}.txt";

    /// <summary>
    /// 拼一行。字段顺序与分隔符全部照原版串，不要「美化」成表格：
    /// 使用者可能拿它跟原版产出对比。
    /// </summary>
    public static string ComposeLine(DateTime when, string browserName, string assetId, string reason) =>
        $"[{when:yyyy-MM-dd HH:mm:ss.fff}] {Tag} 浏览器:{browserName} | assetId:{assetId} | 原因:{reason}";

    /// <summary>
    /// 记一条。<paramref name="directory"/> 为空表示没配保存目录，直接丢弃——
    /// 没配目录是「不导出」，不是错误，不该在每轮结束时冒一行告警。
    /// </summary>
    public void Record(string? directory, DateTime when, string browserName, string assetId, string reason)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        if (!_byDirectory.TryGetValue(directory, out var lines))
        {
            lines = [];
            _byDirectory[directory] = lines;
        }

        lines.Add(ComposeLine(when, browserName ?? string.Empty, assetId ?? string.Empty, reason ?? string.Empty));
        Count++;
    }

    /// <summary>
    /// 把攒下的记录写到各自的目录。返回实际写出的文件路径，方便调用方写日志。
    /// 写盘失败只吞掉并回调 <paramref name="onError"/>：导出是附加产物，
    /// 不能因为磁盘只读或路径不存在就把「本轮执行结果」改成「崩了」。
    /// </summary>
    public IReadOnlyList<string> Flush(DateTime when, Action<string>? onError = null)
    {
        var written = new List<string>();

        foreach (var (directory, lines) in _byDirectory)
        {
            if (lines.Count == 0)
            {
                continue;
            }

            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, FileNameFor(when));

                // 当天已有记录接在前面：原版只有 WriteAllText（整体覆盖），
                // 直接覆盖会让「同一天跑第二轮」把第一轮的失败清单抹掉。
                var existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
                if (existing.Length > 0 && !existing.EndsWith('\n'))
                {
                    existing += Environment.NewLine;
                }

                var body = existing + string.Join(Environment.NewLine, lines) + Environment.NewLine;
                File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                written.Add(path);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"账号失败清单没能写入「{directory}」：{ex.Message}（不影响任务结论）");
            }
        }

        return written;
    }
}
