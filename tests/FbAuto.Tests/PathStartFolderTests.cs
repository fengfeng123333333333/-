using FbAuto.Application.Dialogs;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 对话框的起始位置：输入框里现有的值可能是目录、是文件、也可能是已经失效的路径。
/// </summary>
/// <remarks>
/// 这件事值得测是因为三个分支行为完全不同，而错了都不会报错：
/// 把文件当成目录塞给对话框，它会弹在一个不存在的位置上（表现为「每次都要自己找」）；
/// 把失效路径硬塞进去，它会退到「此电脑」根目录，比系统默认还难用。
/// </remarks>
public sealed class PathStartFolderTests
{
    [Fact]
    public void 现有的目录直接用()
    {
        using var temp = new TempDirectory();

        Assert.Equal(temp.Path, PathStartFolder.Resolve(temp.Path));
    }

    /// <summary>选过文件之后，下次应该停在它所在的目录，而不是回到默认位置。</summary>
    [Fact]
    public void 现有的文件取它所在的目录()
    {
        using var temp = new TempDirectory();
        var file = temp.Combine("desc.txt");
        File.WriteAllText(file, "内容");

        Assert.Equal(temp.Path, PathStartFolder.Resolve(file));
    }

    /// <summary>U 盘拔了、目录被改名、配置从别的机器拷过来 —— 都会落到这个分支。</summary>
    [Theory]
    [InlineData("D:\\不存在的目录\\子目录")]
    [InlineData("\\\\不存在的主机\\共享")]
    public void 失效的路径交给系统默认(string path)
    {
        Assert.Null(PathStartFolder.Resolve(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void 空值交给系统默认(string? path)
    {
        Assert.Null(PathStartFolder.Resolve(path));
    }

    /// <summary>从别处复制路径常带首尾空格，不 Trim 就会「路径明明对却找不到」。</summary>
    [Fact]
    public void 首尾空格被去掉()
    {
        using var temp = new TempDirectory();

        Assert.Equal(temp.Path, PathStartFolder.Resolve($"  {temp.Path}  "));
    }
}
