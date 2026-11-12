using FbAuto.Core.Models;

namespace FbAuto.Tests;

/// <summary>
/// 左栏任务类型表：显示名与通道归属。
/// </summary>
/// <remarks>
/// 这里的用例盯的是「任务入队时用哪个通道」——它决定挑哪个账号、启哪种浏览器。
/// 挑错的后果很隐蔽：账号清单里明明有比特版的号，界面却说「没有可用账号」，
/// 或者反过来拿谷歌版的号去跑比特版任务。
/// </remarks>
public sealed class TaskTypeCatalogTests
{
    private static TaskTypeDescriptor Of(TaskKind kind) => TaskTypeCatalog.Resolve(kind);

    /// <summary>
    /// 带版本后缀的任务**不跟随**全局浏览器类型。
    /// </summary>
    /// <remarks>
    /// 「[视频上传] - 比特版」写死了走比特版。若让它跟随全局，
    /// 使用者切到「谷歌版」时这条任务会偷偷改成走 Chrome，而界面上一个字都没变。
    /// </remarks>
    [Theory]
    [InlineData(TaskKind.VideoUploadGoogle, BrowserChannelKind.Bit, BrowserChannelKind.Google)]
    [InlineData(TaskKind.VideoUploadBit, BrowserChannelKind.Google, BrowserChannelKind.Bit)]
    [InlineData(TaskKind.ProfileEditGoogle, BrowserChannelKind.Bit, BrowserChannelKind.Google)]
    [InlineData(TaskKind.ProfileEditBit, BrowserChannelKind.Google, BrowserChannelKind.Bit)]
    public void 带版本后缀的任务不跟随全局(
        TaskKind kind,
        BrowserChannelKind global,
        BrowserChannelKind expected)
    {
        Assert.False(Of(kind).FollowsBrowserType);

        // 传一个与自身相反的全局值，确认没有被它带偏。
        Assert.Equal(expected, TaskTypeCatalog.ChannelFor(Of(kind), global));
    }

    /// <summary>
    /// 「[删除消息]」只有一个入口，通道跟随全局浏览器类型。
    /// </summary>
    /// <remarks>
    /// 依据是原版串 <c>正在获取比特浏览器列表（删除消息）...</c> ——
    /// 它确实会在比特浏览器上跑。若这里写死谷歌版，比特版账号就永远删不了消息。
    /// </remarks>
    [Theory]
    [InlineData(BrowserChannelKind.Google, BrowserChannelKind.Google)]
    [InlineData(BrowserChannelKind.Bit, BrowserChannelKind.Bit)]
    public void 删除消息跟随全局浏览器类型(BrowserChannelKind global, BrowserChannelKind expected)
    {
        Assert.True(Of(TaskKind.DeleteMessage).FollowsBrowserType);

        Assert.Equal(expected, TaskTypeCatalog.ChannelFor(Of(TaskKind.DeleteMessage), global));
    }

    [Fact]
    public void 左栏显示名与原版一字不差()
    {
        Assert.Equal("[视频上传] - 谷歌版", Of(TaskKind.VideoUploadGoogle).DisplayName);
        Assert.Equal("[视频上传] - 比特版", Of(TaskKind.VideoUploadBit).DisplayName);
        Assert.Equal("[资料修改] - 谷歌版", Of(TaskKind.ProfileEditGoogle).DisplayName);
        Assert.Equal("[资料修改] - 比特版", Of(TaskKind.ProfileEditBit).DisplayName);
        Assert.Equal("[删除消息]", Of(TaskKind.DeleteMessage).DisplayName);
    }

    [Fact]
    public void 每种任务类型在表里恰好一条()
    {
        var kinds = TaskTypeCatalog.All.Select(t => t.Kind).ToArray();

        Assert.Equal(kinds.Length, kinds.Distinct().Count());
        Assert.Equal(Enum.GetValues<TaskKind>().Length, kinds.Length);
    }
}
