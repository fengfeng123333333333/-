using FbAuto.Automation.Abstractions;
using FbAuto.Automation.Pages;

namespace FbAuto.Automation.Profiles;

/// <summary>一次资料修改要改哪些字段。空值表示这一项不改。</summary>
/// <param name="Signature">个性签名，空表示不动。</param>
/// <param name="Category">类别，空表示不动。</param>
/// <param name="UpdatePostingSettings">是否把发帖权限改成仅限自己。</param>
/// <param name="ImageCandidates">
/// 头像与背景图的候选图片，**第一个是本账号应得的那张**，其余作换图备选（被页面拒绝时依次换）。
/// 空表示不改这两项。
/// </param>
public sealed record ProfileEditRequest(
    string Signature = "",
    string Category = "",
    bool UpdatePostingSettings = false,
    IReadOnlyList<string>? ImageCandidates = null)
{
    /// <summary>候选图片，永不为 null。</summary>
    public IReadOnlyList<string> Images => ImageCandidates ?? [];
}

/// <summary>资料修改的整体结果。</summary>
/// <param name="Success">是否所有要求修改的项都成功。</param>
/// <param name="Summary">一句话汇总，写进任务结果列。</param>
/// <param name="Fields">每一项的明细。</param>
/// <param name="LogLines">按发生顺序的日志行。</param>
public sealed record ProfileEditResult(
    bool Success,
    string Summary,
    IReadOnlyList<ProfileFieldResult> Fields,
    IReadOnlyList<string> LogLines)
{
    public int ChangedCount => Fields.Count(f => f.Status == FieldEditStatus.Changed);

    public int SkippedCount => Fields.Count(f => f.Status == FieldEditStatus.Skipped);

    public int FailedCount => Fields.Count(f => f.Status == FieldEditStatus.Failed);
}

/// <summary>
/// 资料修改（谷歌版）的编排：取 profile id → 逐项改 → 汇总。
/// </summary>
/// <remarks>
/// **每项失败不中断，继续下一项**。这不是偷懒，是原版明确的语义 ——
/// 串里依次出现 <c>修改签名失败(继续下一步)</c>、<c>修改类别失败(继续下一步)</c>、
/// <c>修改粉丝公开内容失败(继续下一步)</c>。理由也说得通：
/// 这几项互相独立，一项挂了不该让另外几项白跑一遍。
///
/// 但**汇总时只要有一项失败就判整体失败**。不能因为「改成了两项」就报成功 ——
/// 使用者看到成功就不会去核对页面，而漏改的那一项要等他自己发现。
///
/// **顺序按原版日志块排**：<c>修改头像…</c>、<c>修改背景图…</c>、<c>修改公共主页发帖设置…</c>、
/// <c>修改个性签名…</c>、<c>修改类别…</c>、<c>修改主页名字…</c>。
/// 顺序本身不影响结果（每项自己导航、自己保存），照它排是为了拿原版日志逐行对照时不用先做一次换算。
///
/// 还差两项：<b>粉丝公开内容</b>（关键词屏蔽那一段）与<b>主页名字</b>（iframe + 主账号密码二次确认），
/// 两者单独做。这一版先补上头像与背景图。
/// </remarks>
public static class ProfileEditFlow
{
    /// <summary>
    /// 跑一遍资料修改。
    /// </summary>
    /// <param name="page">页面原语。</param>
    /// <param name="request">要改的字段。</param>
    /// <param name="profileId">已知的 profile id；为空则先导航主页解析。</param>
    /// <param name="options">调参。</param>
    /// <param name="homeUrl">解析 profile id 用的入口地址，测试可指向 file://。</param>
    public static async Task<ProfileEditResult> RunAsync(
        IPageInterop page,
        ProfileEditRequest request,
        string? profileId = null,
        ProfileEditOptions? options = null,
        string? homeUrl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(request);

        options ??= ProfileEditOptions.Default;
        var lines = new List<string>();

        var id = profileId;
        if (string.IsNullOrWhiteSpace(id))
        {
            ProfileIdExtraction resolved;
            try
            {
                resolved = await ProfileIdResolver.ResolveAsync(page, homeUrl, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (PageActionException ex)
            {
                return Fail($"打开主页失败：{ex.Message}", lines, []);
            }

            lines.Add(resolved.LogLine);
            if (!resolved.Found)
            {
                return Fail(resolved.LogLine, lines, []);
            }

            id = resolved.ProfileId!;
        }
        else
        {
            lines.Add($"使用已知 profile id: {id}");
        }

        var fields = new List<ProfileFieldResult>();

        fields.Add(await GuardAsync(
            AvatarEditor.FieldName,
            () => AvatarEditor.RunAsync(page, id!, request.Images, options, cancellationToken),
            lines,
            cancellationToken).ConfigureAwait(false));

        fields.Add(await GuardAsync(
            CoverPhotoEditor.FieldName,
            () => CoverPhotoEditor.RunAsync(page, id!, request.Images, options, cancellationToken),
            lines,
            cancellationToken).ConfigureAwait(false));

        fields.Add(await GuardAsync(
            PostingSettingsEditor.FieldName,
            () => request.UpdatePostingSettings
                ? PostingSettingsEditor.RunAsync(page, options, cancellationToken)
                : Task.FromResult(ProfileFieldResult.Skipped(
                    PostingSettingsEditor.FieldName, "未启用发帖设置修改")),
            lines,
            cancellationToken).ConfigureAwait(false));

        fields.Add(await GuardAsync(
            SignatureEditor.FieldName,
            () => SignatureEditor.RunAsync(page, id!, request.Signature, options, cancellationToken),
            lines,
            cancellationToken).ConfigureAwait(false));

        fields.Add(await GuardAsync(
            CategoryEditor.FieldName,
            () => CategoryEditor.RunAsync(page, id!, request.Category, options, cancellationToken),
            lines,
            cancellationToken).ConfigureAwait(false));

        var failed = fields.Where(f => f.Status == FieldEditStatus.Failed).ToArray();
        var summary = failed.Length == 0
            ? $"资料修改完成（{fields.Count(f => f.Status == FieldEditStatus.Changed)} 项已改，"
              + $"{fields.Count(f => f.Status == FieldEditStatus.Skipped)} 项跳过）"
            : $"资料修改有 {failed.Length} 项失败：{string.Join("、", failed.Select(f => f.Field))}";

        lines.Add(summary);

        return new ProfileEditResult(failed.Length == 0, summary, fields, lines);
    }

    /// <summary>
    /// 单项兜底：异常转成「失败」结果，不让它中断后续项。
    /// </summary>
    /// <remarks>
    /// 取消必须原样往外抛 —— 那是使用者按了停止，不是这一项出错。
    ///
    /// 失败有两种来源：编辑器自己返回 <see cref="FieldEditStatus.Failed"/>（找不到入口之类），
    /// 或者抛异常。**两种都要记同一句「继续下一步」** —— 否则同一个故障，
    /// 一种写法在日志里有「后面还在跑」，另一种没有，看日志的人会以为流程整个断了，
    /// 实际上后两项已经改完了。
    /// </remarks>
    private static async Task<ProfileFieldResult> GuardAsync(
        string field,
        Func<Task<ProfileFieldResult>> action,
        List<string> lines,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await action().ConfigureAwait(false);
            lines.AddRange(result.LogLines);
            if (result.Status == FieldEditStatus.Failed)
            {
                lines.Add($"修改{field}失败(继续下一步): {result.Message}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is PageActionException or InvalidOperationException)
        {
            var message = $"修改{field}失败(继续下一步): {ex.Message}";
            lines.Add(message);
            return ProfileFieldResult.Failed(field, message, []);
        }
    }

    private static ProfileEditResult Fail(
        string message,
        List<string> lines,
        IReadOnlyList<ProfileFieldResult> fields)
    {
        lines.Add(message);
        return new ProfileEditResult(false, message, fields, lines);
    }
}
