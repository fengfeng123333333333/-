using FbAuto.Application.Dialogs;

namespace FbAuto.Tests;

/// <summary>
/// 「选择...」对照表：哪个按钮该选目录、哪个该选文件、文件是什么扩展名。
/// </summary>
/// <remarks>
/// 为什么这些值得单独测：把描述文件选成了目录，界面照样显示一串路径，
/// 直到真跑任务才报「读不到描述文件」，中间没有任何提示。
/// 而「选目录却带了文件筛选器」这种表写错，WPF 那边会默默忽略，行为变成不可预期。
/// </remarks>
public sealed class PathPickerCatalogTests
{
    /// <summary>
    /// 十五个入口，一个不多一个不少。少登记一个的表现是「按钮按下去没反应」。
    /// </summary>
    [Fact]
    public void 登记了十五个入口()
    {
        Assert.Equal(15, PathPickerCatalog.Keys.Count);
    }

    [Theory]
    // 目录：7 个
    [InlineData("VideoDirectory", PathPickerKind.Folder)]
    [InlineData("BitVideoDirectory", PathPickerKind.Folder)]
    [InlineData("SaveDirectory", PathPickerKind.Folder)]
    [InlineData("ImageDirectory", PathPickerKind.Folder)]
    [InlineData("BitImageDirectory", PathPickerKind.Folder)]
    [InlineData("ProfileEditResultExportDir", PathPickerKind.Folder)]
    [InlineData("BitProfileEditResultExportDir", PathPickerKind.Folder)]
    // 文件：8 个
    [InlineData("DescriptionFile", PathPickerKind.File)]
    [InlineData("BitDescriptionFile", PathPickerKind.File)]
    [InlineData("NamesFile", PathPickerKind.File)]
    [InlineData("BitNamesFile", PathPickerKind.File)]
    [InlineData("SignatureFile", PathPickerKind.File)]
    [InlineData("BitSignatureFile", PathPickerKind.File)]
    [InlineData("ChromePath", PathPickerKind.File)]
    [InlineData("AccountsFile", PathPickerKind.File)]
    public void 每个入口的对话框种类正确(string key, PathPickerKind expected)
    {
        var request = PathPickerCatalog.TryResolve(key);

        Assert.NotNull(request);
        Assert.Equal(expected, request!.Kind);
    }

    /// <summary>
    /// 选目录的不能带筛选器，选文件的必须有筛选器。
    /// 这条是表本身的内部一致性：写反了界面不会报错，只会让人在目录对话框里看不到文件。
    /// </summary>
    [Fact]
    public void 种类与筛选器一致()
    {
        foreach (var key in PathPickerCatalog.Keys)
        {
            var request = PathPickerCatalog.TryResolve(key)!;

            if (request.Kind == PathPickerKind.Folder)
            {
                Assert.True(
                    string.IsNullOrEmpty(request.Filter),
                    $"{key} 是选目录，却带了文件筛选器「{request.Filter}」");
            }
            else
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(request.Filter),
                    $"{key} 是选文件，却没有筛选器 —— 使用者会在目录里看到所有类型的文件");
            }
        }
    }

    /// <summary>每个入口都要有能说明「在选什么」的标题，不能弹一个没标题的框。</summary>
    [Fact]
    public void 每个入口都有说明用途的标题()
    {
        foreach (var key in PathPickerCatalog.Keys)
        {
            var request = PathPickerCatalog.TryResolve(key)!;

            Assert.False(string.IsNullOrWhiteSpace(request.Title), $"{key} 没有标题");
            Assert.Contains("选择", request.Title);
        }
    }

    /// <summary>
    /// 文件类入口必须把扩展名锁死。锁错了的表现是「选了个 .jpg 当描述文件」，
    /// 执行到读文件那一步才炸，而且报的是「文件为空」而非「选错了文件」。
    /// </summary>
    [Theory]
    [InlineData("DescriptionFile", "*.txt")]
    [InlineData("BitDescriptionFile", "*.txt")]
    [InlineData("NamesFile", "*.txt")]
    [InlineData("BitNamesFile", "*.txt")]
    [InlineData("SignatureFile", "*.txt")]
    [InlineData("BitSignatureFile", "*.txt")]
    [InlineData("ChromePath", "*.exe")]
    [InlineData("AccountsFile", "*.csv")]
    public void 文件入口锁定了扩展名(string key, string pattern)
    {
        var request = PathPickerCatalog.TryResolve(key);

        Assert.NotNull(request);
        Assert.Contains(pattern, request!.Filter);
    }

    /// <summary>
    /// 认不出来的参数返回空，不抛异常：界面上对应的是「点了没反应 + 一行警告」，
    /// 抛异常会把整个点击事件变成未处理异常弹窗，比不生效更糟。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("videoDirectory")] // 大小写不对
    [InlineData("VideoDir")]        // 拼错
    [InlineData("DeleteMessageCookieRootPath")] // 故意没登记的一项
    public void 无法识别的参数返回空(string? key)
    {
        Assert.Null(PathPickerCatalog.TryResolve(key));
    }
}
