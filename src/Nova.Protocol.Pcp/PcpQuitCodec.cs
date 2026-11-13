namespace PeerCast.Nova.Protocol.Pcp;

public enum PcpQuitReason
{
    Unspecified,
    Normal,
    Unavailable,
    NotIdentified,
    BadAgent,
    Other,
}

/// <summary>Preserves the wire code without interpreting HTTP status or retry policy.</summary>
public readonly record struct PcpQuitInfo(uint Code)
{
    public PcpQuitReason Reason => Code switch
    {
        0 => PcpQuitReason.Unspecified,
        PcpQuitCodec.NormalCode => PcpQuitReason.Normal,
        PcpQuitCodec.UnavailableCode => PcpQuitReason.Unavailable,
        PcpQuitCodec.NotIdentifiedCode => PcpQuitReason.NotIdentified,
        PcpQuitCodec.BadAgentCode => PcpQuitReason.BadAgent,
        _ => PcpQuitReason.Other,
    };
}

public static class PcpQuitCodec
{
    public const uint NormalCode = 1000;
    public const uint UnavailableCode = 1003;
    public const uint NotIdentifiedCode = 1005;
    public const uint BadAgentCode = 1007;

    public static PcpAtom Create(uint code) =>
        PcpAtom.Leaf(PcpKnownAtomNames.Quit, PcpWireValueCodec.EncodeUInt32(code));

    public static PcpQuitInfo Parse(PcpAtom atom)
    {
        ArgumentNullException.ThrowIfNull(atom);
        if (atom.Name != PcpKnownAtomNames.Quit || atom.IsParent)
            throw new InvalidDataException("Expected a PCP QUIT leaf atom.");
        return Decode(atom.Content.Span);
    }

    public static PcpQuitInfo Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != sizeof(uint))
            throw new InvalidDataException("PCP QUIT must contain a four-byte code.");
        return new(PcpWireValueCodec.DecodeUInt32(payload));
    }
}
