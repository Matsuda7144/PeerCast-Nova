using System.Buffers.Binary;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Codec for the twelve-byte PCP protocol preamble:
/// atom name "pcp\n", four-byte payload length, and protocol version.
/// </summary>
public static class PcpPreamble
{
    public const int Size = 12;
    public const uint SupportedProtocolVersion = 1;

    public static readonly PcpAtomName AtomName = PcpAtomName.FromAscii("pcp\n");

    public static bool TryRead(
        ReadOnlySpan<byte> source,
        PcpProtocolLimits limits,
        out uint protocolVersion,
        out PcpParseError error)
    {
        protocolVersion = 0;

        if (source.Length < Size)
        {
            error = PcpParseError.NeedMoreData;
            return false;
        }

        if (!PcpAtomCodec.TryReadHeader(source, limits, out var header, out error))
        {
            return false;
        }

        if (header.Name != AtomName || header.IsParent || header.Value != sizeof(uint))
        {
            error = PcpParseError.InvalidPreamble;
            return false;
        }

        protocolVersion = BinaryPrimitives.ReadUInt32LittleEndian(source[8..Size]);

        if (protocolVersion != SupportedProtocolVersion)
        {
            error = PcpParseError.UnsupportedProtocolVersion;
            return false;
        }

        error = PcpParseError.None;
        return true;
    }

    public static void Write(Span<byte> destination, uint protocolVersion = SupportedProtocolVersion)
    {
        if (destination.Length < Size)
        {
            throw new ArgumentException("The destination is shorter than a PCP preamble.", nameof(destination));
        }

        PcpAtomCodec.WriteHeader(
            destination,
            new PcpAtomHeader(AtomName, isParent: false, value: sizeof(uint)));

        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..Size], protocolVersion);
    }
}
