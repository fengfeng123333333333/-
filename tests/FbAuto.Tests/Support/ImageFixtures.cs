using System.Buffers.Binary;
using System.Text;

namespace FbAuto.Tests.Support;

/// <summary>
/// 造各种图片的**真实文件头**字节。
/// </summary>
/// <remarks>
/// 不用「随便几个字节」凑数：解析器的价值就在于按格式规范读偏移，
/// 假数据只能测出「不崩」，测不出「读对」。这里的每个方法都按各格式的实际布局拼字节。
/// </remarks>
public static class ImageFixtures
{
    /// <summary>PNG：8 字节签名 + IHDR 块（宽高为大端）。</summary>
    public static byte[] Png(int width, int height)
    {
        var buffer = new byte[33];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(buffer, 0);

        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(8, 4), 13); // IHDR 长度
        Encoding.ASCII.GetBytes("IHDR").CopyTo(buffer, 12);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(20, 4), height);
        buffer[24] = 8;  // 位深
        buffer[25] = 2;  // 真彩色
        // 后面 5 字节（压缩/过滤/隔行）+ 4 字节 CRC 留 0，解析器不校验

        return buffer;
    }

    /// <summary>JPEG：SOI + APP0 段 + SOF0 段（高宽为大端）+ EOI。</summary>
    public static byte[] Jpeg(int width, int height)
    {
        using var stream = new MemoryStream();

        stream.Write([0xFF, 0xD8]); // SOI

        // APP0/JFIF：长度 16（含长度自身两字节）
        stream.Write([0xFF, 0xE0, 0x00, 0x10]);
        stream.Write(Encoding.ASCII.GetBytes("JFIF\0"));
        stream.Write([0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);

        // SOF0：长度 17，精度 8，然后 高(2) 宽(2)
        stream.Write([0xFF, 0xC0, 0x00, 0x11, 0x08]);
        var size = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(size.AsSpan(0, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(size.AsSpan(2, 2), (ushort)width);
        stream.Write(size);
        stream.Write([0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);

        stream.Write([0xFF, 0xD9]); // EOI
        return stream.ToArray();
    }

    /// <summary>JPEG，且在 SOF 之前塞一个超长的 APP1(EXIF) 段，用来验证会正确跳过无关段。</summary>
    public static byte[] JpegWithLargeExif(int width, int height, int exifPayloadLength = 4000)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);

        var segmentLength = exifPayloadLength + 2;
        stream.Write([0xFF, 0xE1]);
        var lenBytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lenBytes, (ushort)segmentLength);
        stream.Write(lenBytes);
        stream.Write(new byte[exifPayloadLength]); // 假装是 EXIF 数据

        stream.Write([0xFF, 0xC2, 0x00, 0x11, 0x08]); // SOF2（渐进式）也要能认
        var size = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(size.AsSpan(0, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(size.AsSpan(2, 2), (ushort)width);
        stream.Write(size);
        stream.Write([0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        stream.Write([0xFF, 0xD9]);

        return stream.ToArray();
    }

    /// <summary>WebP（VP8X 扩展格式）：画布宽高各占 3 字节小端，存的是「值 − 1」。</summary>
    public static byte[] WebpVp8X(int width, int height)
    {
        var buffer = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(buffer, 8);
        Encoding.ASCII.GetBytes("VP8X").CopyTo(buffer, 12);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16, 4), 10);
        // 20..23 标志位留 0
        Write24LittleEndian(buffer, 24, width - 1);
        Write24LittleEndian(buffer, 27, height - 1);
        return buffer;
    }

    /// <summary>WebP（VP8 有损）：起始码 9D 01 2A，宽高各 2 字节小端，低 14 位有效。</summary>
    public static byte[] WebpVp8(int width, int height)
    {
        var buffer = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(buffer, 8);
        Encoding.ASCII.GetBytes("VP8 ").CopyTo(buffer, 12);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16, 4), 10);
        buffer[20] = 0x30; // 帧标记
        buffer[21] = 0x00;
        buffer[22] = 0x00;
        buffer[23] = 0x9D;
        buffer[24] = 0x01;
        buffer[25] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(26, 2), (ushort)(width & 0x3FFF));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(28, 2), (ushort)(height & 0x3FFF));
        return buffer;
    }

    /// <summary>WebP（VP8L 无损）：0x2F 签名 + 位打包的「宽−1 / 高−1」。</summary>
    public static byte[] WebpVp8L(int width, int height)
    {
        var buffer = new byte[30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4, 4), 22);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(buffer, 8);
        Encoding.ASCII.GetBytes("VP8L").CopyTo(buffer, 12);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16, 4), 10);
        buffer[20] = 0x2F;
        var bits = (uint)((width - 1) & 0x3FFF) | ((uint)((height - 1) & 0x3FFF) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(21, 4), bits);
        return buffer;
    }

    /// <summary>GIF：签名 + 宽高各 2 字节小端。不在白名单内，只用于验证能被识别成「格式不支持」。</summary>
    public static byte[] Gif(int width, int height)
    {
        var buffer = new byte[16];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), (ushort)height);
        return buffer;
    }

    /// <summary>BMP：宽高在 18 / 22 偏移，4 字节小端。</summary>
    public static byte[] Bmp(int width, int height)
    {
        var buffer = new byte[30];
        buffer[0] = 0x42;
        buffer[1] = 0x4D;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(18, 4), width);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(22, 4), height);
        return buffer;
    }

    /// <summary>写一个能落盘的最小 PNG（内容仍是合法文件头，足够本项目的解析用）。</summary>
    public static string WritePng(string directory, string fileName, int width = 820, int height = 312)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, Png(width, height));
        return path;
    }

    private static void Write24LittleEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
    }
}
