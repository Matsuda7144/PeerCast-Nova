using System.Buffers.Binary;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Stateless codec for the fixed PCP atom header.
/// It deliberately performs no socket I/O and allocates no payload buffers.
/// </summary>
public static class PcpAtomCodec
{
    public const int HeaderSize = 8;
    public const uint ParentFlag = 0x8000_0000;
    public const uint ValueMask = 0x7FFF_FFFF;

    public static bool TryReadHeader(
        ReadOnlySpan<byte> source,
        PcpProtocolLimits limits,
        out PcpAtomHeader header,
        out PcpParseError error)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        header = default;

        if (source.Length < HeaderSize)
        {
            error = PcpParseError.NeedMoreData;
            return false;
        }

        var name = PcpAtomName.Read(source);
        var encodedValue = BinaryPrimitives.ReadUInt32LittleEndian(source[4..HeaderSize]);
        var isParent = (encodedValue & ParentFlag) != 0;
        var value = checked((int)(encodedValue & ValueMask));

        if (isParent && value > limits.MaxChildCount)
        {
            error = PcpParseError.ChildCountLimitExceeded;
            return false;
        }

        if (!isParent && value > limits.MaxContentLength)
        {
            error = PcpParseError.ContentLengthLimitExceeded;
            return false;
        }

        header = new PcpAtomHeader(name, isParent, value);
        error = PcpParseError.None;
        return true;
    }

    public static void WriteHeader(Span<byte> destination, PcpAtomHeader header)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException("The destination is shorter than a PCP atom header.", nameof(destination));
        }

        header.Name.WriteTo(destination);

        var encodedValue = checked((uint)header.Value);

        if (header.IsParent)
        {
            encodedValue |= ParentFlag;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..HeaderSize], encodedValue);
    }
}
