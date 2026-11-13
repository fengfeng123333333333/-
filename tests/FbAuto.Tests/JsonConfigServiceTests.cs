using FbAuto.Infrastructure.Configuration;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 冒烟测试 1：配置文件损坏时必须回退默认值，不能把程序挡在启动之前。
/// 这条对应原版的真实教训——配置一坏就静默退出，用户只看到窗口闪一下。
/// </summary>
public sealed class JsonConfigServiceTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly TestLogger _logger = new();

    private JsonConfigService CreateService() => new(_dir.Path, _logger);

    [Fact]
    public void 文件不存在时生成默认配置且默认值取自原版实测()
    {
        var service = CreateService();

        var config = service.Load();

        Assert.True(File.Exists(service.ConfigFilePath), "首次启动应把默认配置写出来，便于人工核对字段");
        Assert.Equal(1, config.ConcurrentCount);
        Assert.Equal(3, config.BrowserOpenIntervalSeconds);
        Assert.Equal(30, config.LogRetentionDays);
        Assert.Equal(string.Empty, config.ChromePath);
        Assert.Equal("http://127.0.0.1:54345", config.BitBrowserApiBaseUrl);
        Assert.Equal(["com", "https", "pn", "dn", "inbox"], config.CommentsBlockWords.Select(w => w.TrimStart('.')));
        Assert.Equal(20, config.DeleteMessageLimit);
        Assert.Equal(string.Empty, config.DeleteMessageCookieRootPath);

        // 比特版是**另一套**参数，不是谷歌版的副本：默认都为空，但键必须各自独立存在。
        Assert.Equal(string.Empty, config.BitVideoDirectory);
        Assert.Equal(string.Empty, config.BitDescriptionFile);
        Assert.Equal(string.Empty, config.BitSignatureFile);
        Assert.Equal(string.Empty, config.BitProfileCategory);
        Assert.True(config.BitUpdatePostingSettings);
        Assert.Equal(5, config.BitCommentsBlockWords.Length);
        Assert.Equal(string.Empty, config.ImageDirectory);
        Assert.Equal(string.Empty, config.BitImageDirectory);
        Assert.True(_logger.Contains("INF", "配置文件不存在"));
    }

    [Theory]
    [InlineData("{ 这不是 JSON")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{\"ConcurrentCount\": \"三个\"}")]
    public void 配置损坏时回退默认值并记错误日志(string brokenJson)
    {
        var service = CreateService();
        File.WriteAllText(service.ConfigFilePath, brokenJson);

        var config = service.Load();

        // 关键：不抛异常，返回可用的默认配置
        Assert.Equal(1, config.ConcurrentCount);
        Assert.Equal(3, config.BrowserOpenIntervalSeconds);

        if (brokenJson == "null")
        {
            // 反序列化 null 属于合法 JSON，走的是「为空则新建」分支，不报错
            Assert.True(_logger.Contains("INF", "配置已加载"));
        }
        else
        {
            Assert.True(_logger.Contains("ERR", "配置读取失败，回退默认值"));
        }
    }

    [Fact]
    public void 只改部分字段时其余字段保持默认()
    {
        var service = CreateService();
        File.WriteAllText(service.ConfigFilePath, "{\"ConcurrentCount\": 5}");

        var config = service.Load();

        Assert.Equal(5, config.ConcurrentCount);
        Assert.Equal(3, config.BrowserOpenIntervalSeconds);
        Assert.Equal("谷歌版", config.BrowserType);
    }

    [Fact]
    public void 保存后重新加载值一致且中文不被转义()
    {
        var first = CreateService();
        var config = first.Load();
        config.ConcurrentCount = 7;
        config.BrowserType = "比特版";
        first.Save();

        var raw = File.ReadAllText(first.ConfigFilePath);
        Assert.Contains("比特版", raw);

        var second = new JsonConfigService(_dir.Path, new TestLogger());
        var reloaded = second.Load();

        Assert.Equal(7, reloaded.ConcurrentCount);
        Assert.Equal("比特版", reloaded.BrowserType);
    }

    /// <summary>
    /// 谷歌版与比特版的路径互不覆盖。
    /// </summary>
    /// <remarks>
    /// 这条防的是「两套配置写成了同一个字段」这类错误。原版 settings.json 里
    /// <c>GoogleVideoPath</c> 与 <c>BitVideoPath</c> 是两个键，抄漏一个的直接后果是
    /// 比特版任务去传谷歌版目录里的视频 —— 不报错、不失败，只是传错了东西。
    /// </remarks>
    [Fact]
    public void 谷歌版与比特版配置互不覆盖()
    {
        var first = CreateService();
        var config = first.Load();
        config.VideoDirectory = @"D://g-videos";
        config.BitVideoDirectory = @"D://b-videos";
        config.ImageDirectory = @"D://g-images";
        config.BitImageDirectory = @"D://b-images";
        first.Save();

        var reloaded = new JsonConfigService(_dir.Path, new TestLogger()).Load();

        Assert.Equal(@"D://g-videos", reloaded.VideoDirectory);
        Assert.Equal(@"D://b-videos", reloaded.BitVideoDirectory);
        Assert.Equal(@"D://g-images", reloaded.ImageDirectory);
        Assert.Equal(@"D://b-images", reloaded.BitImageDirectory);
    }

    [Fact]
    public void 允许注释与尾逗号便于人工维护()
    {
        var service = CreateService();
        File.WriteAllText(service.ConfigFilePath, """
            {
              // 手改过的配置不该因为多一个逗号就失效
              "ConcurrentCount": 2,
              "BrowserOpenIntervalSeconds": 5,
            }
            """);

        var config = service.Load();

        Assert.Equal(2, config.ConcurrentCount);
        Assert.Equal(5, config.BrowserOpenIntervalSeconds);
    }

    public void Dispose() => _dir.Dispose();
}
