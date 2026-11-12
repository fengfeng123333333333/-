namespace FbAuto.Tests.Support;

/// <summary>
/// 需要真 Chrome 的测试共用一个集合，集合内的类串行执行。
/// </summary>
/// <remarks>
/// 为什么必须串行：每个测试类各自启一个 Chrome（一个实例十几个子进程）。几类同时跑时，
/// 页面加载与 CDP 握手互相拖慢，表现是**测试进程整个挂住**——不是超时失败，是没有任何输出地卡死，
/// 只能靠人盯着才发现。前面几轮的集成测试都带了 <c>[Collection("chrome")]</c>，
/// 新增的视频上传测试类漏了这一行，于是出现「单独跑全绿、全量跑必挂」。
///
/// **以后新增任何启动浏览器的测试类，第一件事就是加 [Collection("chrome")]。**
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ChromeCollection
{
    /// <summary>集合名。历史用例里写的是字面量 <c>"chrome"</c>，这里保持一致。</summary>
    public const string Name = "chrome";
}
