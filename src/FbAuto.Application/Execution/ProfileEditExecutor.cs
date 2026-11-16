using FbAuto.Automation.Media;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Profiles;
using FbAuto.Core.Abstractions;
using FbAuto.Core.Configuration;
using FbAuto.Core.Models;

namespace FbAuto.Application.Execution;

/// <summary>
/// 资料修改执行器：读签名文件 + 扫图片目录 + 挑名字 → 交给流程 → 汇总。
/// </summary>
/// <remarks>
/// 七项齐备：背景图、头像、发帖设置、粉丝公开内容、个性签名、类别、主页名字
/// （顺序即原版分发串的顺序）。
///
/// 三项输入来源完全不同，别混：
/// <list type="bullet">
/// <item><b>签名</b>：整段 txt 一份，所有账号同一段话。</item>
/// <item><b>图片</b>：目录里一张图，按别名稳定哈希分，同账号同图。</item>
/// <item><b>名字</b>：txt 一行一个，按别名稳定哈希挑一行。缺密码时名字改不动。</item>
/// </list>
/// </remarks>
public sealed class ProfileEditExecutor : ITaskExecutor
{
    private readonly AppConfig _config;
    private readonly TaskKind _kind;
    private readonly ProfileEditOptions? _options;

    /// <param name="config">全局配置。</param>
    /// <param name="kind">任务类型，决定读哪一套业务参数（谷歌版 / 比特版）。</param>
    /// <param name="options">调参覆盖，仅测试用。</param>
    public ProfileEditExecutor(
        AppConfig config,
        TaskKind kind = TaskKind.ProfileEditGoogle,
        ProfileEditOptions? options = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _kind = kind;
        _options = options;
    }

    public TaskKind Kind => _kind;

    /// <summary>本执行器实际使用的那套业务参数。公开出来给用例钉住「没读错通道」。</summary>
    public ChannelProfile Profile => _config.ForChannel(TaskTypeCatalog.ChannelOf(_kind));

    public async Task<TaskExecutionResult> ExecuteAsync(
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.Logger;

        var profile = Profile;
        logger.Info($"按 {profile.DisplayName} 取配置：签名文件 {profile.SignatureFile}，图片目录 {profile.ImageDirectory}");

        // 开关先写一行「本次要改哪几项」。没有这一行，用户看到一串「已跳过」时
        // 分不清是「没勾」还是「没数据」—— 这两种在日志里是同一句原版串。
        var enabled = profile.EnabledEditItems;
        logger.Info(enabled.Count == 0
            ? "七项修改都未勾选：本次不会修改任何内容"
            : $"本次要改 {enabled.Count} 项：{string.Join("、", enabled)}");

        // 没勾的项连数据都不读：否则「图片目录指向了一个坏路径」会在只改签名时
        // 报一句「头像与背景图两项将跳过」，让人以为漏了配置 —— 其实是这次压根不改那两项。
        var wantImages = profile.EditCoverImage || profile.EditAvatar;
        var wantSignature = profile.EditSignature;
        var wantName = profile.EditPageName;

        var (signature, sourceLines) = wantSignature
            ? SignatureSource.Read(profile.SignatureFile)
            : (string.Empty, (IReadOnlyList<string>)[]);
        foreach (var line in sourceLines)
        {
            logger.Info(line);
        }

        var images = wantImages
            ? BuildImageCandidates(context.Account.Alias, profile.ImageDirectory, logger)
            : [];

        // 名字按别名挑一行：逐任务执行时只有当前账号一个别名，按行号取会永远是第一行，
        // 表现是「所有账号改成同一个名字」（头像图片踩过同一个坑）。
        var nameSelection = wantName
            ? NameSource.Pick(profile.NamesFile, context.Account.Alias)
            : new NameSelection(null, []);
        foreach (var line in nameSelection.LogLines)
        {
            logger.Info(line);
        }

        if (string.IsNullOrWhiteSpace(profile.PageNamePassword) && nameSelection.Found)
        {
            logger.Warn("没有配置主页名称密码：主页名字会提交，但很可能不会生效，请在页面上人工核对");
        }

        // 类别留空时回落到原版硬编码的「戏剧」，而不是空着让 CategoryEditor 跳过。
        // 原版没有类别配置键，选择器与日志里写死这个词；留空即跳过会让
        // 「修改类别」勾了却永远不改 —— 界面上看不出任何异常，只是那一项从来没生效过。
        var category = CategoryEditor.ResolveTarget(profile.ProfileCategory);

        if (profile.EditCategory && string.IsNullOrWhiteSpace(profile.ProfileCategory))
        {
            logger.Info($"没有配置类别，按原版默认值「{CategoryEditor.DefaultCategory}」修改");
        }

        // 全部用具名实参：这里有 6 个连续的 bool，位置写错不会有任何编译期或运行期提示，
        // 表现是「勾了 A 结果改了 B」，而日志上看不出来。具名之后这类错位不可能发生。
        var request = new ProfileEditRequest(
            Signature: signature,
            Category: category,
            UpdatePostingSettings: profile.UpdatePostingSettings,
            ImageCandidates: images,
            BlockWords: profile.CommentsBlockWords,
            PageName: nameSelection.Name ?? string.Empty,
            PageNamePassword: profile.PageNamePassword,
            EditCoverImage: profile.EditCoverImage,
            EditAvatar: profile.EditAvatar,
            EditFollowersContent: profile.EditFollowersContent,
            EditSignature: profile.EditSignature,
            EditCategory: profile.EditCategory,
            EditPageName: profile.EditPageName);

        var page = new PlaywrightPageInterop(context.Page);

        ProfileEditResult result;
        try
        {
            result = await ProfileEditFlow.RunAsync(
                page,
                request,
                options: _options,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (PageActionException ex)
        {
            return await context.FailAsync($"页面动作失败：{ex.Message}").ConfigureAwait(false);
        }

        foreach (var line in result.LogLines)
        {
            logger.Info(line);
        }

        return result.Success
            ? TaskExecutionResult.Ok(result.Summary)
            : await context.FailAsync(result.Summary).ConfigureAwait(false);
    }

    /// <summary>
    /// 把图片目录变成「本账号那张图在最前」的候选列表。
    /// </summary>
    /// <remarks>
    /// 选图规则在 <see cref="AccountImageCandidates"/> 里（可单测，不碰页面也不碰浏览器）。
    /// 这里只负责把扫描结果与告警写进日志 —— 「配了目录但一张能用的都没有」必须让人看见，
    /// 否则使用者会以为头像换了，实际两项都被跳过。
    /// </remarks>
    private static IReadOnlyList<string> BuildImageCandidates(string alias, string directory, IAppLogger logger)
    {
        var scan = ImageDirectoryScanner.Scan(directory);
        if (!scan.IsUsable)
        {
            logger.Info(scan.Failure ?? "图片目录不可用，头像与背景图两项将跳过");
            return [];
        }

        logger.Info($"图片目录可用 {scan.Count} 张：{directory}");
        foreach (var skip in scan.Skipped)
        {
            logger.Warn($"跳过图片 {Path.GetFileName(skip.Path)}：{skip.Reason}");
        }

        var selection = AccountImageCandidates.Build(alias, scan);
        if (selection.IsEmpty)
        {
            logger.Warn($"账号 {alias} 没有分到图片，头像与背景图两项将跳过");
            return [];
        }

        // 「目录里只有一张图」这类情况只在同一批账号一起跑时才看得出来，
        // 而执行器是逐任务跑的 —— 所以计划自带的告警在这里必须照抄一遍。
        foreach (var warning in selection.Warnings)
        {
            logger.Warn(warning);
        }

        logger.Info(
            $"账号 {alias} 使用图片：{Path.GetFileName(selection.Primary!)}（备选 {selection.AlternateCount} 张）");
        return selection.Candidates;
    }
}
