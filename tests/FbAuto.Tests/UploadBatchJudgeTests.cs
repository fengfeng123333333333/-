using FbAuto.Automation.Uploads;

namespace FbAuto.Tests;

/// <summary>
/// 批次结束后的动作判定。唯一不能错的一条：**零成功绝不发布**。
/// 点下去就是一条没有视频的空帖，删不掉对账号的风控影响。
/// </summary>
public sealed class UploadBatchJudgeTests
{
    [Fact]
    public void 全部成功直接发布()
    {
        var verdict = UploadBatchJudge.Judge(success: 3, failed: 0);

        Assert.Equal(UploadBatchAction.Publish, verdict.Action);
        Assert.True(verdict.CanPublish);
        Assert.False(verdict.NeedsCleanup);
        Assert.Contains("全部成功", verdict.LogLine);
    }

    [Fact]
    public void 部分成功要先删失败条目再发布()
    {
        var verdict = UploadBatchJudge.Judge(success: 2, failed: 1);

        Assert.Equal(UploadBatchAction.RemoveFailedThenPublish, verdict.Action);
        Assert.True(verdict.CanPublish);
        Assert.True(verdict.NeedsCleanup);
        Assert.Contains("部分成功", verdict.LogLine);
        Assert.Contains("2", verdict.LogLine);
        Assert.Contains("1", verdict.LogLine);
    }

    [Fact]
    public void 全部失败时跳过发布并提示账号疑似被封()
    {
        var verdict = UploadBatchJudge.Judge(success: 0, failed: 3);

        Assert.Equal(UploadBatchAction.AbortSuspectedBanned, verdict.Action);
        Assert.False(verdict.CanPublish);
        Assert.Contains("账号疑似被封", verdict.LogLine);
    }

    [Fact]
    public void 一个都没注进去也不能发布()
    {
        // success=0 且 failed=0：视频可能根本没进页面（file input 选错、文件被拒）。
        // 这种情况最容易写漏 —— 因为没有「失败」可数，代码会当成「全都成功」。
        var verdict = UploadBatchJudge.Judge(success: 0, failed: 0);

        Assert.Equal(UploadBatchAction.AbortSuspectedBanned, verdict.Action);
        Assert.False(verdict.CanPublish);
    }

    [Fact]
    public void 负数参数是程序错误()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UploadBatchJudge.Judge(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UploadBatchJudge.Judge(0, -1));
    }
}
