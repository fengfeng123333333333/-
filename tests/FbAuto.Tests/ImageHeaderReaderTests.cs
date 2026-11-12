using System.Buffers.Binary;
using System.Text;
using FbAuto.Automation.Media;
using FbAuto.Tests.Support;

namespace FbAuto.Tests;

/// <summary>
/// 图片头解析。这段代码是封面预校验的地基：读错一个字节，后面「尺寸够不够」的判断就全错，
/// 而且错得很安静（不会抛异常，只会把 820×312 读成 0×0 然后判「太小」）。
/// </summary>
public sealed class ImageHeaderReaderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void PNG_读出宽高()
    {
        var ok = ImageHeaderReader.TryRead(ImageFixtures.Png(820, 312), out var info, out var failure);

        Assert.True(ok, failure);
        Assert.NotNull(info);
        Assert.Equal(ImageFormat.Png, info!.Format);
        Assert.Equal(820, info.Width);
        Assert.Equal(312, info.Height);
    }

    [Fact]
    public void JPEG_读出宽高()
    {
        var ok = ImageHeaderReader.TryRead(ImageFixtures.Jpeg(1280, 720), out var info, out var failure);

        Assert.True(ok, failure);
        Assert.Equal(ImageFormat.Jpeg, info!.Format);
        Assert.Equal(1280, info.Width);
        Assert.Equal(720, info.Height);
    }

    [Fact]
    public void JPEG_跳过超长EXIF段仍能找到SOF()
    {
        // 真机常见的坑：相机直出图前 4KB 全是 EXIF，SOF 在很后面。
        var bytes = ImageFixtures.JpegWithLargeExif(1600, 1200, exifPayloadLength: 4000);

        var ok = ImageHeaderReader.TryRead(bytes, out var info, out var failure);

        Assert.True(ok, failure);
        Assert.Equal(1600, info!.Width);
        Assert.Equal(1200, info.Height);
    }

    [Fact]
    public void JPEG_渐进式SOF2也能识别()
    {
        var bytes = ImageFixtures.JpegWithLargeExif(851, 315, exifPayloadLength: 16);

        var ok = ImageHeaderReader.TryRead(bytes, out var info, out _);

        Assert.True(ok);
        Assert.Equal(851, info!.Width);
        Assert.Equal(315, info.Height);
    }

    [Theory]
    [InlineData(820, 312)]
    [InlineData(400, 150)]
    [InlineData(1640, 624)]
    public void WebP_VP8X格式读画布尺寸(int width, int height)
    {
        var ok = ImageHeaderReader.TryRead(ImageFixtures.WebpVp8X(width, height), out var info, out var failure);

        Assert.True(ok, failure);
        Assert.Equal(ImageFormat.Webp, info!.Format);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }

    [Fact]
    public void WebP_VP8有损格式读宽高()
    {
        var ok = ImageHeaderReader.TryRead(ImageFixtures.WebpVp8(1920, 1080), out var info, out var failure);

        Assert.True(ok, failure);
        Assert.Equal(ImageFormat.Webp, info!.Format);
        Assert.Equal(1920, info.Width);
        Assert.Equal(1080, info.Height);
    }

    [Fact]
    public void WebP_VP8L无损格式读宽高()
    {
        var ok = ImageHeaderReader.TryRead(ImageFixtures.WebpVp8L(300, 200), out var info, out var failure);

        Assert.True(ok, failure);
        Assert.Equal(ImageFormat.Webp, info!.Format);
        Assert.Equal(300, info.Width);
        Assert.Equal(200, info.Height);
    }

    [Fact]
    public void GIF_识别出格式但不属于白名单()
    {
        var ok = ImageHeaderReader.TryRead(ImageFixtures.Gif(200, 200), out var info, out _);

        Assert.True(ok);
        Assert.Equal(ImageFormat.Gif, info!.Format);
        Assert.False(CoverSizeRule.FacebookPageCover.AllowedFormats.Contains(info.Format));
    }

    [Fact]
    public void BMP_负高度取绝对值()
    {
        var bytes = ImageFixtures.Bmp(640, -480);

        var ok = ImageHeaderReader.TryRead(bytes, out var info, out _);

        Assert.True(ok);
        Assert.Equal(480, info!.Height);
    }

    [Fact]
    public void 空数据_报文件头不足()
    {
        var ok = ImageHeaderReader.TryRead([], out var info, out var failure);

        Assert.False(ok);
        Assert.Null(info);
        Assert.Contains("不足 12 字节", failure);
    }

    [Fact]
    public void 垃圾数据_报无法识别()
    {
        var garbage = new byte[64];
        Random.Shared.NextBytes(garbage);
        // 确保不会碰巧命中某个签名
        garbage[0] = 0x00;
        garbage[1] = 0x00;
        garbage[8] = 0x00;

        var ok = ImageHeaderReader.TryRead(garbage, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("无法识别", failure);
    }

    [Fact]
    public void PNG签名正确但缺IHDR块_报损坏()
    {
        var bytes = ImageFixtures.Png(100, 100);
        Encoding.ASCII.GetBytes("XXXX").CopyTo(bytes, 12);

        var ok = ImageHeaderReader.TryRead(bytes, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("IHDR", failure);
    }

    [Fact]
    public void PNG宽为0_报尺寸非法()
    {
        var bytes = ImageFixtures.Png(0, 100);

        var ok = ImageHeaderReader.TryRead(bytes, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("尺寸非法", failure);
    }

    [Fact]
    public void JPEG没有SOF段_报未找到()
    {
        // 12 字节：足够长到不会被「文件头不足」提前拦下，但里面确实没有 SOF 段。
        var bytes = new byte[]
        {
            0xFF, 0xD8,
            0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
            0x00, 0x00,
            0xFF, 0xD9,
        };

        var ok = ImageHeaderReader.TryRead(bytes, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("SOF", failure);
    }

    [Fact]
    public void WebP起始码错误_报损坏()
    {
        var bytes = ImageFixtures.WebpVp8(800, 600);
        bytes[23] = 0x00; // 破坏 9D 01 2A 起始码

        var ok = ImageHeaderReader.TryRead(bytes, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("起始码", failure);
    }

    [Fact]
    public void WebP子格式不支持_报具体块名()
    {
        var bytes = ImageFixtures.WebpVp8X(100, 100);
        Encoding.ASCII.GetBytes("ALPH").CopyTo(bytes, 12);

        var ok = ImageHeaderReader.TryRead(bytes, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("ALPH", failure);
    }

    [Fact]
    public void 读文件_落盘的真实PNG()
    {
        var path = ImageFixtures.WritePng(_temp.Path, "cover.png", 820, 312);

        var ok = ImageHeaderReader.TryReadFile(path, out var info, out var failure);

        Assert.True(ok, failure);
        Assert.Equal(820, info!.Width);
        Assert.Equal(312, info.Height);
    }

    [Fact]
    public void 读文件_文件不存在_说清路径()
    {
        var path = Path.Combine(_temp.Path, "missing.png");

        var ok = ImageHeaderReader.TryReadFile(path, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("文件不存在", failure);
        Assert.Contains("missing.png", failure);
    }

    [Fact]
    public void 读文件_路径为空()
    {
        var ok = ImageHeaderReader.TryReadFile("   ", out _, out var failure);

        Assert.False(ok);
        Assert.Contains("路径为空", failure);
    }

    [Fact]
    public void 读文件_空文件_报文件头不足()
    {
        var path = Path.Combine(_temp.Path, "empty.png");
        File.WriteAllBytes(path, []);

        var ok = ImageHeaderReader.TryReadFile(path, out _, out var failure);

        Assert.False(ok);
        Assert.Contains("不足", failure);
    }

    [Fact]
    public void 宽高比按宽除高计算()
    {
        var bytes = ImageFixtures.Png(820, 312);
        ImageHeaderReader.TryRead(bytes, out var info, out _);

        Assert.Equal(820d / 312d, info!.AspectRatio, 6);
    }

    [Fact]
    public void 高度为0时宽高比返回0而不是除零()
    {
        var info = new ImageInfo(ImageFormat.Png, 800, 0);

        Assert.Equal(0d, info.AspectRatio);
    }
}
