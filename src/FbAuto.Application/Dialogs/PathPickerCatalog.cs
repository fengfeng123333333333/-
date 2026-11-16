using System.Collections.Generic;

namespace FbAuto.Application.Dialogs;

/// <summary>
/// 「选择...」按钮的对照表：按钮带哪个 key，就弹哪种对话框。
/// </summary>
/// <remarks>
/// 为什么不给每个按钮单独写一个 <c>RelayCommand</c>：十五个 <c>[RelayCommand] private void PickXxx()</c>
/// 除了属性名以外一模一样，改一个筛选器要改十五处，而且漏改不会有任何报错。
/// 做成 key → 请求的表之后，界面上只剩「这个按钮选哪一项」，剩下的规则都在这里，能单测。
///
/// key 就是 ViewModel 上的属性名（XAML 里写 <c>CommandParameter="VideoDirectory"</c>），
/// 这样「按钮接错了属性」用肉眼就能看出来。**大小写敏感**：写错就当未知 key，
/// 点按钮只会在日志里留一句警告，不会静默选错地方。
/// </remarks>
public static class PathPickerCatalog
{
    private static readonly Dictionary<string, PathPickerRequest> Entries =
        new(StringComparer.Ordinal)
        {
            // ── 视频上传 ──────────────────────────────────────────────────
            ["VideoDirectory"] = Folder("选择视频所在目录（该目录下的 mp4 会全部上传）"),
            ["DescriptionFile"] = File("选择视频描述文件", PathPickerRequest.TextFilter),
            ["BitVideoDirectory"] = Folder("选择比特版视频所在目录"),
            ["BitDescriptionFile"] = File("选择比特版视频描述文件", PathPickerRequest.TextFilter),
            ["SaveDirectory"] = Folder("选择结果保存目录（截图与导出文件放这里）"),

            // ── 资料修改 ──────────────────────────────────────────────────
            ["NamesFile"] = File("选择主页名称文件（一行一个名字）", PathPickerRequest.TextFilter),
            ["BitNamesFile"] = File("选择比特版主页名称文件", PathPickerRequest.TextFilter),
            ["SignatureFile"] = File("选择个性签名文件（整段作为一段签名）", PathPickerRequest.TextFilter),
            ["BitSignatureFile"] = File("选择比特版个性签名文件", PathPickerRequest.TextFilter),
            ["ImageDirectory"] = Folder("选择头像与背景图目录（两项共用这一张图）"),
            ["BitImageDirectory"] = Folder("选择比特版头像与背景图目录"),
            ["ProfileEditResultExportDir"] = Folder("选择资料修改结果保存目录（谷歌版）"),
            ["BitProfileEditResultExportDir"] = Folder("选择资料修改结果保存目录（比特版）"),

            // ── 两个原本连输入框都没有的项 ────────────────────────────────
            // 留空时都走自动逻辑（Chrome 注册表查找 / 程序目录 accounts.csv），
            // 但换机器、Chrome 装在非常规位置、账号清单放在别处时，必须能选。
            ["ChromePath"] = File("选择 Chrome 可执行文件（留空则自动查找）", PathPickerRequest.ExecutableFilter),
            ["AccountsFile"] = File("选择账号清单 CSV（留空则用程序目录下的 accounts.csv）", PathPickerRequest.CsvFilter),
        };

    /// <summary>表里登记的 key，供测试与界面文档核对。</summary>
    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    /// <summary>
    /// 查 key 对应的请求。<paramref name="key"/> 为空、空白或不在表里时返回 <c>null</c>。
    /// </summary>
    public static PathPickerRequest? TryResolve(string? key) =>
        !string.IsNullOrWhiteSpace(key) && Entries.TryGetValue(key, out var request) ? request : null;

    private static PathPickerRequest Folder(string title) =>
        new() { Kind = PathPickerKind.Folder, Title = title };

    private static PathPickerRequest File(string title, string filter) =>
        new() { Kind = PathPickerKind.File, Title = title, Filter = filter };
}
