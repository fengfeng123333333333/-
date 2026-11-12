using FbAuto.Automation.Flows;
using FbAuto.Automation.Pages;
using FbAuto.Automation.Uploads;
using FbAuto.Tests.Support;
using Microsoft.Playwright;

namespace FbAuto.Tests;

/// <summary>
/// 视频上传流程在真 Chrome 里跑。页面是本地仿真页（<see cref="FacebookMimicPage.UploadComposer"/>），
/// 选择器与产品代码共用同一份常量。
/// </summary>
/// <remarks>
/// 这些用例证明的是「动作与判定在真浏览器里成立」，**证明不了「Facebook 现在还是这个 DOM」**。
/// 后者要等出口通了再验。
///
/// 页面地址走 <see cref="VideoUploadOptions.ComposerUrlTemplate"/> 注入本地文件 URL，
/// 所以流程里「导航到上传页」那段是真的被执行了一遍，没有被跳过。
/// </remarks>
[Collection("chrome")]
public sealed class VideoUploadFlowIntegrationTests : ChromePageTestBase
{
    private static VideoUploadOptions FastOptions(TimeSpan? total = null) => new(
        PollInterval: TimeSpan.FromMilliseconds(50),
        FormFillDelay: TimeSpan.Zero,
        InputWaitTimeout: TimeSpan.FromSeconds(3),
        SchedulePanelTimeout: TimeSpan.FromSeconds(2),
        PublishTimeout: TimeSpan.FromSeconds(5),
        Monitor: new UploadMonitorOptions(
            ZeroProgressTimeout: TimeSpan.FromSeconds(2),
            NoChangeTimeout: TimeSpan.FromSeconds(3),
            TotalTimeout: total ?? TimeSpan.FromSeconds(20)));

    /// <summary>写一份仿真页，返回「把它当上传页」的选项。</summary>
    private VideoUploadOptions ComposerOptions(string html, string fileName, TimeSpan? total = null)
    {
        var path = FacebookMimicPage.Write(Dir.Path, fileName, html);
        return FastOptions(total) with { ComposerUrlTemplate = new Uri(path).AbsoluteUri };
    }

    private Task<IPage> NewPageAsync() => Channel.NewPageAsync(Browser);

    private string[] CreateVideos(int count)
    {
        var dir = Dir.Combine($"videos-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);

        var paths = new string[count];
        for (var i = 0; i < count; i++)
        {
            paths[i] = Path.Combine(dir, $"v{i + 1}.mp4");
            // 内容不重要，Playwright 只按路径造 File 对象；但必须是真文件。
            File.WriteAllBytes(paths[i], [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        }

        return paths;
    }

    [Fact]
    public async Task 全部成功_整批注入并发布到完成()
    {
        var options = ComposerOptions(FacebookMimicPage.UploadComposer(), "upload.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(2), "测试描述"),
            options);

        Assert.True(result.Published, string.Join("\n", result.LogLines));

        // 批量一次性注入：分两次注入的话第二个视频会把第一个覆盖掉。
        Assert.Equal(2, await ReadAsync<int>(page, "() => window.__injected"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__published"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__done"));
        Assert.Equal("测试描述", await ReadAsync<string>(page, "() => window.__description"));
    }

    [Fact]
    public async Task 初始没有文件输入框时先点添加视频()
    {
        var options = ComposerOptions(
            FacebookMimicPage.UploadComposer(gateByAddVideo: true),
            "upload-gate.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(1), string.Empty),
            options);

        Assert.True(result.Published, string.Join("\n", result.LogLines));
        Assert.True(await ReadAsync<bool>(page, "() => window.__addVideoClicked"));
        Assert.Contains(result.LogLines, x => x.Contains("已点击添加视频按钮"));
    }

    [Fact]
    public async Task 进度转成版权审核也判上传完成()
    {
        var options = ComposerOptions(
            FacebookMimicPage.UploadComposer(copyrightReview: true),
            "upload-copyright.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(1), string.Empty),
            options);

        // 审核阶段进度条不会消失，只把百分比换成「版权审核」。
        // 若只等「全部消失」，这里会一直等到总超时然后判失败。
        Assert.True(result.Published, string.Join("\n", result.LogLines));
        Assert.Contains(result.LogLines, x => x.Contains("版权审核"));
    }

    [Fact]
    public async Task 有失败条目时先移除再发布()
    {
        var options = ComposerOptions(
            FacebookMimicPage.UploadComposer(failCount: 1),
            "upload-partial.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(2), "描述"),
            options);

        Assert.True(result.Published, string.Join("\n", result.LogLines));
        Assert.Equal(1, await ReadAsync<int>(page, "() => window.__removed"));
        Assert.Contains(result.LogLines, x => x.Contains("部分成功"));
        Assert.True(await ReadAsync<bool>(page, "() => window.__published"));
    }

    [Fact]
    public async Task 全部失败时绝不点发布()
    {
        var options = ComposerOptions(
            FacebookMimicPage.UploadComposer(failCount: 2),
            "upload-allfail.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(2), string.Empty),
            options);

        // 这是最贵的一条：零成功还点发布会发出一条没有视频的空帖。
        Assert.Equal(VideoUploadStatus.Aborted, result.Status);
        Assert.False(await ReadAsync<bool>(page, "() => window.__published"));
        Assert.Contains(result.LogLines, x => x.Contains("账号疑似被封"));
    }

    [Fact]
    public async Task 定时发布时填入日期与小时()
    {
        var options = ComposerOptions(FacebookMimicPage.UploadComposer(), "upload-schedule.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest(
                "1234567890",
                CreateVideos(1),
                "描述",
                Scheduled: true,
                ScheduleDate: new DateOnly(2026, 12, 1),
                ScheduleHour: 9),
            options);

        Assert.True(result.Published, string.Join("\n", result.LogLines));
        Assert.True(await ReadAsync<bool>(page, "() => window.__timed"));

        // 原版格式是 yyyy-M-d，不是 yyyy-MM-dd。
        Assert.Equal("2026-12-1", await ReadAsync<string>(page, "() => window.__date"));
        Assert.Equal("9", await ReadAsync<string>(page, "() => window.__hour"));
    }

    [Fact]
    public async Task 描述为空时不填但流程照走()
    {
        var options = ComposerOptions(FacebookMimicPage.UploadComposer(), "upload-nodesc.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(1), string.Empty),
            options);

        Assert.True(result.Published, string.Join("\n", result.LogLines));
        Assert.Contains(result.LogLines, x => x.Contains("描述为空"));
        Assert.Equal(string.Empty, await ReadAsync<string>(page, "() => window.__description"));
    }

    [Fact]
    public async Task 失败条目晚于进度条出现_仍然要清理后再发布()
    {
        // 真机上的时序就是这样：进度条先消失，失败条目晚几帧才画出来。
        // 如果在进度条消失的那一刻立刻去数失败数，会数到 0，
        // 于是把「有失败」判成「全部成功」，跳过清理直接发布。
        var options = ComposerOptions(
            FacebookMimicPage.UploadComposer(failCount: 1, failRenderDelayMilliseconds: 400),
            "upload-latefail.html");
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await VideoUploadFlow.RunAsync(
            interop,
            new VideoUploadRequest("1234567890", CreateVideos(2), "描述"),
            options);

        Assert.True(result.Published, string.Join("\n", result.LogLines));
        Assert.Equal(1, await ReadAsync<int>(page, "() => window.__removed"));
        Assert.Contains(result.LogLines, x => x.Contains("部分成功"));
    }

    /// <summary>
    /// asset_id 的「导航 → 读整页 → 正则提取」这一串要在真浏览器里跑一遍。
    /// </summary>
    /// <remarks>
    /// 单测只能证明正则本身对，证明不了「页面内容真能读到」——
    /// 而真机上最常见的失败恰恰是读回来的是登录墙或被 JS 改写后的 DOM。
    /// </remarks>
    [Fact]
    public async Task 从主页列表页提取asset_id()
    {
        const string html = """
            <!DOCTYPE html>
            <html lang="zh-CN"><body>
              <a href="https://business.facebook.com/latest/leads_center?asset_id=987654321">业务主页</a>
            </body></html>
            """;

        var path = FacebookMimicPage.Write(Dir.Path, "pages.html", html);
        var page = await NewPageAsync();
        var interop = new PlaywrightPageInterop(page);

        var result = await AssetIdResolver.ResolveAsync(interop, new Uri(path).AbsoluteUri);

        Assert.True(result.Found);
        Assert.Equal("987654321", result.AssetId);
        Assert.Equal(AssetIdSource.LeadsCenter, result.Source);
    }
}
