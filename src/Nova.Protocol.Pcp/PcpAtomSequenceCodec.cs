using System.Buffers;

namespace PeerCast.Nova.Protocol.Pcp;

/// <summary>
/// Reads a fixed PCP atom header from a segmented sequence without advancing
/// the caller's reader unless a complete, valid header is available.
/// </summary>
public static class PcpAtomSequenceCodec
{
    public static bool TryReadHeader(
        ref SequenceReader<byte> reader,
        PcpProtocolLimits limits,
        out PcpAtomHeader header,
        out PcpParseError error)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var probe = reader;
        Span<byte> headerBytes = stackalloc byte[PcpAtomCodec.HeaderSize];

        if (!probe.TryCopyTo(headerBytes))
        {
            header = default;
            error = PcpParseError.NeedMoreData;
            return false;
        }

        if (!PcpAtomCodec.TryReadHeader(
            headerBytes,
            limits,
            out header,
            out error))
        {
            return false;
        }

        probe.Advance(PcpAtomCodec.HeaderSize);
        reader = probe;
        return true;
    }
}
