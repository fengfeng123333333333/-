using System.Text;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Models;

namespace FbAuto.Infrastructure.Accounts;

/// <summary>
/// 账号清单读取器（CSV）。
///
/// 三条设计取舍：
/// 1. **按列名找列，不按位置**。清单是手工维护的，列顺序改了不应该让程序读错人。
/// 2. **坏行跳过并记警告，不整表失败**。一行填错就让程序拒绝启动，是最坏的用户体验。
/// 3. **文件不存在不算错误**。首次使用时无清单是正常状态，执行时再报「没分到账号」。
/// </summary>
public sealed class AccountListCsv : IAccountDirectory
{
    private static readonly string[] AliasHeaders = ["别名", "alias", "账号"];
    private static readonly string[] ChannelHeaders = ["通道", "channel"];
    private static readonly string[] ProfileHeaders = ["user-data-dir", "userdatadir", "profile", "配置目录"];
    private static readonly string[] BitIdHeaders = ["比特窗口ID", "窗口ID", "bitprofileid"];
    private static readonly string[] ProxyHeaders = ["代理", "proxy"];
    private static readonly string[] HomeIdHeaders = ["主页ID", "homepageid"];
    private static readonly string[] HomeNameHeaders = ["主页名称", "homename"];
    private static readonly string[] CredHeaders = ["凭证引用", "credential"];
    private static readonly string[] StatusHeaders = ["状态", "status"];
    private static readonly string[] RemarkHeaders = ["备注", "remark"];

    private readonly IAppLogger _logger;
    private readonly object _sync = new();
    private Dictionary<BrowserChannelKind, int> _roundRobin = [];

    public AccountListCsv(string path, IAppLogger logger)
    {
        SourcePath = path ?? throw new ArgumentNullException(nameof(path));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Reload();
    }

    public IReadOnlyList<AccountEntry> All { get; private set; } = [];

    public string SourcePath { get; }

    public AccountEntry? Find(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        return All.FirstOrDefault(a => string.Equals(a.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    public AccountEntry? NextAvailable(BrowserChannelKind channel)
    {
        var candidates = All.Where(a => a.Channel == channel && a.IsUsable).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        lock (_sync)
        {
            var index = _roundRobin.GetValueOrDefault(channel);
            var picked = candidates[index % candidates.Count];
            _roundRobin[channel] = (index + 1) % candidates.Count;
            return picked;
        }
    }

    public void Reload()
    {
        All = Load(out var warnings);
        _roundRobin = [];

        foreach (var warning in warnings)
        {
            _logger.Warn($"账号清单：{warning}");
        }

        if (All.Count == 0)
        {
            _logger.Warn($"账号清单为空或不存在：{SourcePath}。执行任务会因「没有账号」而失败，请先按 docs/accounts-HOWTO.md 填写");
        }
        else
        {
            var google = All.Count(a => a.Channel == BrowserChannelKind.Google);
            var bit = All.Count(a => a.Channel == BrowserChannelKind.Bit);
            _logger.Info($"账号清单已加载：{All.Count} 个（谷歌 {google} / 比特 {bit}），来源 {SourcePath}");
        }
    }

    private List<AccountEntry> Load(out List<string> warnings)
    {
        var result = new List<AccountEntry>();
        warnings = [];

        if (!File.Exists(SourcePath))
        {
            return result;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(SourcePath, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            warnings.Add($"读取失败（{ex.Message}），本次按空清单处理");
            return result;
        }

        var headerIndex = -1;
        int aliasCol = -1, channelCol = -1, profileCol = -1, bitIdCol = -1, proxyCol = -1;
        int homeIdCol = -1, homeNameCol = -1, credCol = -1, statusCol = -1, remarkCol = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart('\uFEFF');   // BOM 会粘在第一列名前面，不去掉就找不到「别名」
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var cells = SplitCsvLine(line);
            headerIndex = i;
            aliasCol = IndexOfHeader(cells, AliasHeaders);
            channelCol = IndexOfHeader(cells, ChannelHeaders);
            profileCol = IndexOfHeader(cells, ProfileHeaders);
            bitIdCol = IndexOfHeader(cells, BitIdHeaders);
            proxyCol = IndexOfHeader(cells, ProxyHeaders);
            homeIdCol = IndexOfHeader(cells, HomeIdHeaders);
            homeNameCol = IndexOfHeader(cells, HomeNameHeaders);
            credCol = IndexOfHeader(cells, CredHeaders);
            statusCol = IndexOfHeader(cells, StatusHeaders);
            remarkCol = IndexOfHeader(cells, RemarkHeaders);
            break;
        }

        if (headerIndex < 0)
        {
            warnings.Add("文件里没有表头行，本次按空清单处理");
            return result;
        }

        if (aliasCol < 0 || channelCol < 0)
        {
            warnings.Add($"表头缺少必需列「别名」或「通道」（实际表头：{string.Join("/", SplitCsvLine(lines[headerIndex]))}）");
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = headerIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var cells = SplitCsvLine(line);
            var alias = Cell(cells, aliasCol);
            if (string.IsNullOrWhiteSpace(alias))
            {
                warnings.Add($"第 {i + 1} 行没有别名，已跳过");
                continue;
            }

            if (!seen.Add(alias))
            {
                warnings.Add($"第 {i + 1} 行别名「{alias}」重复，保留第一次出现的，已跳过本行");
                continue;
            }

            var channelText = Cell(cells, channelCol).Trim().ToLowerInvariant();
            BrowserChannelKind channel;
            switch (channelText)
            {
                case "google":
                case "谷歌":
                case "谷歌版":
                    channel = BrowserChannelKind.Google;
                    break;
                case "bit":
                case "比特":
                case "比特版":
                    channel = BrowserChannelKind.Bit;
                    break;
                default:
                    warnings.Add($"第 {i + 1} 行通道填的是「{Cell(cells, channelCol)}」，只认 google / bit，已跳过");
                    continue;
            }

            var profile = Cell(cells, profileCol);
            var bitId = Cell(cells, bitIdCol);

            // 必需字段缺失属于「填错了」，不是「没填完」。补一条警告，但仍然载入——
            // 执行时会因为目录为空给出更具体的提示，比在这里静默丢弃更好排障。
            if (channel == BrowserChannelKind.Google && string.IsNullOrWhiteSpace(profile))
            {
                warnings.Add($"第 {i + 1} 行（{alias}）是 google 通道但 user-data-dir 为空，执行时会失败");
            }

            if (channel == BrowserChannelKind.Bit && string.IsNullOrWhiteSpace(bitId))
            {
                warnings.Add($"第 {i + 1} 行（{alias}）是 bit 通道但比特窗口ID 为空，执行时会失败");
            }

            result.Add(new AccountEntry
            {
                Alias = alias,
                Channel = channel,
                ProfileDirectory = profile,
                BitProfileId = bitId,
                Proxy = Cell(cells, proxyCol),
                HomePageId = Cell(cells, homeIdCol),
                HomePageName = Cell(cells, homeNameCol),
                CredentialRef = Cell(cells, credCol),
                Status = Cell(cells, statusCol),
                Remark = Cell(cells, remarkCol),
            });
        }

        return result;
    }

    private static int IndexOfHeader(IReadOnlyList<string> cells, string[] candidates)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            var normalized = NormalizeHeader(cells[i]);
            foreach (var candidate in candidates)
            {
                if (normalized == NormalizeHeader(candidate))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// 表头归一化：去空格、下划线、连字符，统一小写。
    /// 两边都要归一——只归一候选名的话，「user-data-dir」永远匹配不上「userdatadir」。
    /// </summary>
    private static string NormalizeHeader(string value) =>
        value.Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .ToLowerInvariant();

    private static string Cell(IReadOnlyList<string> cells, int index) =>
        index >= 0 && index < cells.Count ? cells[index].Trim() : string.Empty;

    /// <summary>
    /// 最小 CSV 行解析：支持双引号包裹、字段内逗号、转义双引号（""）。
    /// 不够完整（不支持跨行字段），但账号清单里不会出现换行，够用。
    /// </summary>
    internal static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        buffer.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    buffer.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    cells.Add(buffer.ToString());
                    buffer.Clear();
                    break;
                default:
                    buffer.Append(c);
                    break;
            }
        }

        cells.Add(buffer.ToString());
        return cells;
    }
}
