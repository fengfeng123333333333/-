using System.Buffers.Binary;

namespace FbAuto.Automation.Media;

/// <summary>
/// 图片格式。只列实际会遇到且需要区分的几种，其余一律 <see cref="Unknown"/>。
/// </summary>
public enum ImageFormat
{
    Unknown = 0,
    Png = 1,
    Jpeg = 2,
    Webp = 3,
    Gif = 4,
    Bmp = 5,
}

/// <summary>
/// 从文件头读出的像素尺寸。不含文件大小 —— 大小由调用方给，避免同一份数据两处记账。
/// </summary>
public sealed record ImageInfo(ImageFormat Format, int Width, int Height)
{
    /// <summary>宽高比（宽 ÷ 高）。高度为 0 时返回 0，调用方须先判断有效性。</summary>
    public double AspectRatio => Height <= 0 ? 0d : (double)Width / Height;
}

/// <summary>
/// 只读文件头解析图片宽高，不解码像素。
/// </summary>
/// <remarks>
/// 为什么要自己解析：封面尺寸预校验必须在上传**之前**完成，才能省掉一次注定失败的上传。
/// 引入 ImageSharp 之类的解码库代价过大（体积 + 新的供应商依赖），而三种白名单格式的
/// 尺寸都在头部，读前几十字节即可得到。
///
/// 白名单格式来自原版提取串：<c>.webp</c> <c>.jpg</c> <c>.jpeg</c> <c>.png</c>
/// （见 docs/plans/S5-PAGE-GENERIC-LOG.md 2.1 证据表）。
/// </remarks>
public static class ImageHeaderReader
{
    /// <summary>
    /// 默认真实读取的头部字节数。JPEG 的 EXIF 段理论上可达数百 KB（内嵌缩略图），
    /// 取 512 KB 可覆盖绝大多数相机直出图；仍读不到 SOF 时按「无法识别」处理并给出明确原因。
    /// </summary>
    public const int DefaultHeadLength = 512 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// 解析内存中的文件头。成功返回 true；失败返回 false 并把原因写入 <paramref name="failure"/>。
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> head, out ImageInfo? info, out string failure)
    {
        info = null;
        failure = string.Empty;

        if (head.Length < 12)
        {
            failure = $"文件头不足 12 字节（实际 {head.Length} 字节），无法识别";
            return false;
        }

        if (head[..8].SequenceEqual(PngSignature))
        {
            return TryReadPng(head, out info, out failure);
        }

        if (head[0] == 0xFF && head[1] == 0xD8)
        {
            return TryReadJpeg(head, out info, out failure);
        }

        if (IsRiffWebp(head))
        {
            return TryReadWebp(head, out info, out failure);
        }

        if (head[0] == 0x47 && head[1] == 0x49 && head[2] == 0x46) // "GIF"
        {
            // GIF 不在白名单内，但识别出来能让报错更准确（「格式不支持 GIF」而不是「无法识别」）。
            if (head.Length < 10)
            {
                failure = "GIF 文件头不完整";
                return false;
            }

            var gw = BinaryPrimitives.ReadUInt16LittleEndian(head[6..8]);
            var gh = BinaryPrimitives.ReadUInt16LittleEndian(head[8..10]);
            info = new ImageInfo(ImageFormat.Gif, gw, gh);
            return true;
        }

        if (head[0] == 0x42 && head[1] == 0x4D) // "BM"
        {
            if (head.Length < 26)
            {
                failure = "BMP 文件头不完整";
                return false;
            }

            var bw = BinaryPrimitives.ReadInt32LittleEndian(head[18..22]);
            var bh = BinaryPrimitives.ReadInt32LittleEndian(head[22..26]);
            // BMP 高度可为负数（自上而下存储），尺寸判断只看绝对值。
            info = new ImageInfo(ImageFormat.Bmp, Math.Abs(bw), Math.Abs(bh));
            return true;
        }

        failure = "无法识别的图片格式（仅支持 PNG / JPEG / WebP）";
        return false;
    }

    /// <summary>
    /// 读文件头部并解析尺寸。文件不存在、过短、无法识别都会给出中文原因。
    /// </summary>
    public static bool TryReadFile(string path, out ImageInfo? info, out string failure, int headLength = DefaultHeadLength)
    {
        info = null;
        failure = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
        {
            failure = "图片路径为空";
            return false;
        }

        if (!File.Exists(path))
        {
            failure = $"文件不存在: {path}";
            return false;
        }

        byte[] buffer;
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = (int)Math.Min(stream.Length, Math.Max(headLength, 12));
            buffer = new byte[length];
            read = stream.ReadAtLeast(buffer, length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = $"读取文件失败: {ex.Message}";
            return false;
        }

        return TryRead(buffer.AsSpan(0, read), out info, out failure);
    }

    private static bool TryReadPng(ReadOnlySpan<byte> head, out ImageInfo? info, out string failure)
    {
        info = null;
        failure = string.Empty;

        // PNG：8 字节签名 + 4 字节块长度 + "IHDR" + 宽(4, BE) + 高(4, BE)
        if (head.Length < 24)
        {
            failure = $"PNG 文件头不完整（{head.Length} 字节，至少需要 24 字节）";
            return false;
        }

        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
        {
            failure = "PNG 缺少 IHDR 块，文件可能已损坏";
            return false;
        }

        var w = BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        var h = BinaryPrimitives.ReadInt32BigEndian(head[20..24]);
        if (w <= 0 || h <= 0)
        {
            failure = $"PNG 尺寸非法: {w}×{h}";
            return false;
        }

        info = new ImageInfo(ImageFormat.Png, w, h);
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> head, out ImageInfo? info, out string failure)
    {
        info = null;
        failure = string.Empty;

        var pos = 2;
        while (pos + 3 < head.Length)
        {
            if (head[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            var marker = head[pos + 1];

            // 填充字节（FF FF）与无需长度的标记（SOI/EOI/RSTn/TEM）
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            if (marker == 0xD8 || marker == 0xD9 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                pos += 2;
                if (marker == 0xD9)
                {
                    break;
                }

                continue;
            }

            if (pos + 4 > head.Length)
            {
                break;
            }

            var segLen = BinaryPrimitives.ReadUInt16BigEndian(head[(pos + 2)..(pos + 4)]);
            if (segLen < 2)
            {
                break;
            }

            if (IsStartOfFrame(marker))
            {
                // SOFn 段：长度(2) 精度(1) 高(2) 宽(2)
                if (pos + 9 > head.Length)
                {
                    failure = "JPEG 的 SOF 段被截断，无法读取尺寸";
                    return false;
                }

                var h = BinaryPrimitives.ReadUInt16BigEndian(head[(pos + 5)..(pos + 7)]);
                var w = BinaryPrimitives.ReadUInt16BigEndian(head[(pos + 7)..(pos + 9)]);
                if (w == 0 || h == 0)
                {
                    failure = $"JPEG 尺寸非法: {w}×{h}";
                    return false;
                }

                info = new ImageInfo(ImageFormat.Jpeg, w, h);
                return true;
            }

            pos += 2 + segLen;
        }

        failure = "JPEG 中未找到 SOF 段（文件可能损坏或头部读取不足）";
        return false;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF);

    private static bool IsRiffWebp(ReadOnlySpan<byte> head) =>
        head.Length >= 12 &&
        head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F' &&
        head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P';

    private static bool TryReadWebp(ReadOnlySpan<byte> head, out ImageInfo? info, out string failure)
    {
        info = null;
        failure = string.Empty;

        if (head.Length < 30)
        {
            failure = $"WebP 文件头不完整（{head.Length} 字节，至少需要 30 字节）";
            return false;
        }

        var chunk = System.Text.Encoding.ASCII.GetString(head[12..16].ToArray());

        switch (chunk)
        {
            case "VP8 ":
            {
                // 帧标记(1) + 起始码(3=9D 01 2A) + 宽(2, LE, 低 14 位) + 高(2, LE)
                if (head[23] != 0x9D || head[24] != 0x01 || head[25] != 0x2A)
                {
                    failure = "WebP(VP8) 起始码不匹配，文件可能已损坏";
                    return false;
                }

                var w = BinaryPrimitives.ReadUInt16LittleEndian(head[26..28]) & 0x3FFF;
                var h = BinaryPrimitives.ReadUInt16LittleEndian(head[28..30]) & 0x3FFF;
                if (w == 0 || h == 0)
                {
                    failure = $"WebP 尺寸非法: {w}×{h}";
                    return false;
                }

                info = new ImageInfo(ImageFormat.Webp, w, h);
                return true;
            }

            case "VP8L":
            {
                // 签名 0x2F + 4 字节位打包：宽-1 占低 14 位，高-1 占次 14 位
                if (head[20] != 0x2F)
                {
                    failure = "WebP(VP8L) 签名不匹配，文件可能已损坏";
                    return false;
                }

                var bits = BinaryPrimitives.ReadUInt32LittleEndian(head[21..25]);
                var w = (int)(bits & 0x3FFF) + 1;
                var h = (int)((bits >> 14) & 0x3FFF) + 1;
                info = new ImageInfo(ImageFormat.Webp, w, h);
                return true;
            }

            case "VP8X":
            {
                // 4 字节标志 + 画布宽-1(3, LE) + 画布高-1(3, LE)
                var w = (head[24] | (head[25] << 8) | (head[26] << 16)) + 1;
                var h = (head[27] | (head[28] << 8) | (head[29] << 16)) + 1;
                info = new ImageInfo(ImageFormat.Webp, w, h);
                return true;
            }

            default:
                failure = $"WebP 子格式不受支持: {chunk}";
                return false;
        }
    }
}
