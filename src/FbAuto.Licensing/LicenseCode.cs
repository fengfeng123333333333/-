using System.Buffers.Binary;

namespace FbAuto.Licensing;

/// <summary>
/// 解析后的授权码。只承载数据，不做验签——验签是 <see cref="LicenseVerifier"/> 的职责，
/// 分开是为了让「格式对不对」和「是不是我签的」两类失败能被区别提示。
/// </summary>
public sealed class LicenseCode
{
    /// <summary>格式版本。</summary>
    public required byte Version { get; init; }

    /// <summary>签发所用公钥编号。</summary>
    public required byte KeyId { get; init; }

    /// <summary>随机序号，仅用于区分批次。</summary>
    public required uint Serial { get; init; }

    /// <summary>签发日期。</summary>
    public required DateOnly IssuedOn { get; init; }

    /// <summary>激活后的有效天数。</summary>
    public required int ValidDays { get; init; }

    /// <summary>功能标志位，当前全部保留为 0。</summary>
    public required byte Flags { get; init; }

    /// <summary>64 字节签名，不参与自身被签名的内容。</summary>
    public required byte[] Signature { get; init; }

    /// <summary>
    /// 重建被签名的 11 字节负载。必须与签发时逐字节一致，
    /// 任何字段顺序、字节序的改动都会让所有已发出的码集体失效。
    /// </summary>
    public byte[] ToPayloadBytes() => BuildPayload(Version, KeyId, Serial, IssuedOn, ValidDays, Flags);

    /// <summary>
    /// 按字段拼出被签名的负载。签发端（还不知道签名）和验签端（从码里还原）共用这一处，
    /// 保证两边拼出来的字节必然一致——分两处写就迟早会漂。
    /// </summary>
    public static byte[] BuildPayload(
        byte version, byte keyId, uint serial, DateOnly issuedOn, int validDays, byte flags)
    {
        if (validDays is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validDays), validDays, $"有效天数必须在 1 到 {ushort.MaxValue} 之间");
        }

        var buffer = new byte[LicenseCodeLayout.PayloadLength];
        buffer[0] = version;
        buffer[1] = keyId;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(2, 4), serial);
        BinaryPrimitives.WriteUInt16BigEndian(
            buffer.AsSpan(6, 2), LicenseCodeLayout.ToDayNumber(issuedOn));
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(8, 2), (ushort)validDays);
        buffer[10] = flags;
        return buffer;
    }

    /// <summary>从 11 字节负载加 64 字节签名还原。</summary>
    public static LicenseCode FromPayload(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        if (payload.Length != LicenseCodeLayout.PayloadLength)
        {
            throw new ArgumentException(
                $"负载长度应为 {LicenseCodeLayout.PayloadLength} 字节，实际 {payload.Length}",
                nameof(payload));
        }

        if (signature.Length != LicenseCodeLayout.SignatureLength)
        {
            throw new ArgumentException(
                $"签名长度应为 {LicenseCodeLayout.SignatureLength} 字节，实际 {signature.Length}",
                nameof(signature));
        }

        return new LicenseCode
        {
            Version = payload[0],
            KeyId = payload[1],
            Serial = BinaryPrimitives.ReadUInt32BigEndian(payload.Slice(2, 4)),
            IssuedOn = LicenseCodeLayout.FromDayNumber(
                BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2))),
            ValidDays = BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(8, 2)),
            Flags = payload[10],
            Signature = signature.ToArray(),
        };
    }

    /// <summary>给人看的摘要，写日志用，不含签名。</summary>
    public override string ToString() =>
        $"码#{Serial} 签发 {IssuedOn:yyyy-MM-dd} 有效 {ValidDays} 天 密钥{KeyId} 版本{Version}";
}
