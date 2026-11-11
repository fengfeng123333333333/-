namespace FbAuto.Tests.Support;

/// <summary>
/// 每次测试一个独立临时目录，测试结束递归删除。避免测试之间互相污染，也不碰程序目录。
/// </summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "FbAutoTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // 临时目录删不掉不影响测试结论，留给系统清理。
        }
    }
}
