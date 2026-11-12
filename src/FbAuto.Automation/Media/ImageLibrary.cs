namespace FbAuto.Automation.Media;

/// <summary>被扫描跳过的文件及原因。</summary>
public sealed record ImageSkip(string Path, string Reason);

/// <summary>
/// 一次图片目录扫描的结果。
/// </summary>
/// <param name="Directory">扫描的目录。为空时是「没配置」。</param>
/// <param name="UsableFiles">可用图片的绝对路径，已按文件名排序（保证多次扫描顺序一致）。</param>
/// <param name="Skipped">看起来可用但被跳过的文件（目前只有空文件）。</param>
/// <param name="Failure">整体不可用时的中文原因；可用时为 null。</param>
public sealed record ImageDirectoryScan(
    string? Directory,
    IReadOnlyList<string> UsableFiles,
    IReadOnlyList<ImageSkip> Skipped,
    string? Failure)
{
    public bool IsUsable => Failure is null && UsableFiles.Count > 0;

    public int Count => UsableFiles.Count;
}

/// <summary>
/// 图片目录扫描。白名单与文案来自原版提取串：
/// 「请选择图片目录（头像和背景图均从该目录随机选取）」以及紧邻的 <c>.webp .jpg .jpeg .png</c>。
/// </summary>
public static class ImageDirectoryScanner
{
    /// <summary>支持的扩展名，顺序与原版串一致。比较时不区分大小写（Windows 上 .JPG 很常见）。</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".webp", ".jpg", ".jpeg", ".png"];

    private static readonly HashSet<string> SupportedSet =
        new(SupportedExtensions, StringComparer.OrdinalIgnoreCase);

    public static bool IsSupportedExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return SupportedSet.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// 扫描目录。不做 IO 之外的事：不读像素、不校验尺寸（那是 CoverSizeChecker 的职责）。
    /// </summary>
    public static ImageDirectoryScan Scan(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new ImageDirectoryScan(null, [], [], "未配置图片目录（头像和背景图均从该目录随机选取）");
        }

        if (!System.IO.Directory.Exists(directory))
        {
            return new ImageDirectoryScan(directory, [], [], $"图片目录不存在: {directory}");
        }

        string[] entries;
        try
        {
            entries = System.IO.Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImageDirectoryScan(directory, [], [], $"读取图片目录失败: {ex.Message}");
        }

        var usable = new List<string>();
        var skipped = new List<ImageSkip>();

        foreach (var path in entries)
        {
            // 非白名单扩展名直接忽略，不记为 skip —— 目录里放个 readme.txt 不该刷屏警告。
            if (!IsSupportedExtension(path))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (info.Length == 0)
                {
                    skipped.Add(new ImageSkip(path, "文件为空（0 字节）"));
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new ImageSkip(path, $"无法读取文件信息: {ex.Message}"));
                continue;
            }

            usable.Add(path);
        }

        usable.Sort(StringComparer.OrdinalIgnoreCase);

        if (usable.Count == 0)
        {
            return new ImageDirectoryScan(
                directory,
                [],
                skipped,
                $"图片目录中没有可用图片（支持 {string.Join(" / ", SupportedExtensions)}）: {directory}");
        }

        return new ImageDirectoryScan(directory, usable, skipped, null);
    }
}

/// <summary>
/// 账号 → 图片的分配计划。
/// </summary>
/// <remarks>
/// 需求来自原版提示串（docs/evidence/extracted/clean_cn.txt:380）：
/// 「提示：头像和背景图在同一账号内使用同一张随机图片，不同账号随机不同」。
///
/// 这句话排除了「每次调用随机取一张」的写法 —— 那样同一账号的头像和背景图会不一样。
/// 正确语义是：**一个账号对应一张确定的图**，头像与背景图共用；账号之间尽量不重复。
///
/// 实现：按 seed 对图片做确定性洗牌，再按账号顺序依次取用。
/// <list type="bullet">
/// <item>同一账号永远拿到同一张（只要账号清单和 seed 不变）—— 可复现、可断点续跑。</item>
/// <item>前 min(账号数, 图片数) 个账号**保证互不相同**，这正是「不同账号随机不同」。</item>
/// <item>账号数超过图片数时必然重复，此时给出警告而不是假装没事。</item>
/// </list>
/// 用账号别名的稳定哈希取模也能「同账号同图」，但两张图、三个账号时极易撞车，
/// 达不到「不同账号不同」，所以不采用。
/// </remarks>
public sealed class AccountImagePlan
{
    private readonly Dictionary<string, string> _assignments;

    private AccountImagePlan(
        Dictionary<string, string> assignments,
        IReadOnlyList<string> warnings,
        int distinctImageCount)
    {
        _assignments = assignments;
        Warnings = warnings;
        DistinctImageCount = distinctImageCount;
    }

    /// <summary>账号别名 → 图片绝对路径。</summary>
    public IReadOnlyDictionary<string, string> Assignments => _assignments;

    /// <summary>分配过程中值得记一笔的情况（图片不够、只有一张等）。</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>实际被用到的不同图片数量。</summary>
    public int DistinctImageCount { get; }

    public bool IsEmpty => _assignments.Count == 0;

    /// <summary>
    /// 建立分配计划。
    /// </summary>
    /// <param name="accountAliases">账号别名，顺序即队列顺序；重复别名只算一次。</param>
    /// <param name="files">可用图片路径。内部会先排序，保证与传入顺序无关。</param>
    /// <param name="seed">洗牌种子。同一个 seed 永远得到同一套分配。</param>
    public static AccountImagePlan Create(
        IEnumerable<string>? accountAliases,
        IReadOnlyList<string>? files,
        int seed = 0)
    {
        var warnings = new List<string>();
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);

        var aliases = (accountAliases ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var pool = (files ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();
        pool.Sort(StringComparer.OrdinalIgnoreCase);

        if (aliases.Count == 0)
        {
            warnings.Add("没有账号，无法分配头像/背景图");
            return new AccountImagePlan(assignments, warnings, 0);
        }

        if (pool.Count == 0)
        {
            warnings.Add("没有可用图片，无法分配头像/背景图");
            return new AccountImagePlan(assignments, warnings, 0);
        }

        if (pool.Count == 1)
        {
            warnings.Add($"图片目录只有 1 张可用图片，所有 {aliases.Count} 个账号都会使用同一张: {Path.GetFileName(pool[0])}");
        }
        else if (aliases.Count > pool.Count)
        {
            warnings.Add(
                $"{aliases.Count} 个账号只有 {pool.Count} 张可用图片，会有 {aliases.Count - pool.Count} 个账号重复使用图片");
        }

        var shuffled = Shuffle(pool, seed);

        for (var i = 0; i < aliases.Count; i++)
        {
            assignments[aliases[i]] = shuffled[i % shuffled.Count];
        }

        return new AccountImagePlan(assignments, warnings, assignments.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>取某账号的图片。账号不在计划里返回 null（例如清单中途加了新账号）。</summary>
    public string? Pick(string? alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return null;
        }

        return _assignments.TryGetValue(alias, out var path) ? path : null;
    }

    /// <summary>头像和背景图共用同一个结果，这里给个明确的入口名，避免调用方各写一遍。</summary>
    public string? PickForAvatar(string? alias) => Pick(alias);

    /// <summary>背景图与头像同图，见类型注释里的原版提示。</summary>
    public string? PickForCover(string? alias) => Pick(alias);

    /// <summary>
    /// Fisher-Yates 洗牌。不用 <c>Random.Shared</c>：必须可复现，同一个 seed 每次跑出同一套分配。
    /// </summary>
    private static List<string> Shuffle(List<string> source, int seed)
    {
        var list = new List<string>(source);
        var random = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }
}
