namespace FbAuto.Automation.Uploads;

/// <summary>一批上传结束后该做什么。</summary>
public enum UploadBatchAction
{
    /// <summary>全部成功，直接点发布。</summary>
    Publish,

    /// <summary>部分成功，先删掉失败条目再发布。</summary>
    RemoveFailedThenPublish,

    /// <summary>一个都没成功，不能发布。</summary>
    AbortSuspectedBanned,
}

/// <summary>批次判定结果。</summary>
/// <param name="Action">下一步动作。</param>
/// <param name="Success">已完成的个数。</param>
/// <param name="Failed">失败的个数。</param>
/// <param name="LogLine">写进任务日志的中文说明。</param>
public sealed record UploadBatchVerdict(
    UploadBatchAction Action,
    int Success,
    int Failed,
    string LogLine)
{
    /// <summary>失败条目是否需要先清理。</summary>
    public bool NeedsCleanup => Action == UploadBatchAction.RemoveFailedThenPublish;

    /// <summary>能不能继续走到发布。</summary>
    public bool CanPublish => Action != UploadBatchAction.AbortSuspectedBanned;
}

/// <summary>
/// 把「成功几个、失败几个」判成下一步动作。
/// </summary>
/// <remarks>
/// 原版三条对应串：
/// <list type="bullet">
/// <item><c>全部成功，直接发布</c></item>
/// <item><c>部分成功（n 个），删除 m 个失败视频后发布</c></item>
/// <item><c>没有视频上传成功，账号疑似被封，跳过发布</c></item>
/// </list>
///
/// **零成功时绝不发布**是这里唯一不能错的一条。点下去就是一条没有视频的空帖，
/// 而且发出去之后删不掉「对账号的风控印象」。所以哪怕 <c>failed</c> 也是 0
/// （一个都没注入进去）也必须走 <see cref="UploadBatchAction.AbortSuspectedBanned"/>。
/// </remarks>
public static class UploadBatchJudge
{
    public static UploadBatchVerdict Judge(int success, int failed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(success);
        ArgumentOutOfRangeException.ThrowIfNegative(failed);

        if (success <= 0)
        {
            return new UploadBatchVerdict(
                UploadBatchAction.AbortSuspectedBanned,
                success,
                failed,
                "没有视频上传成功，账号疑似被封，跳过发布");
        }

        if (failed == 0)
        {
            return new UploadBatchVerdict(
                UploadBatchAction.Publish,
                success,
                failed,
                "全部成功，直接发布");
        }

        return new UploadBatchVerdict(
            UploadBatchAction.RemoveFailedThenPublish,
            success,
            failed,
            $"部分成功（{success} 个），删除 {failed} 个失败视频后发布");
    }
}
