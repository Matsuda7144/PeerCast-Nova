namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Callback-scoped relay view of one upstream packet. Payload memory must not
/// be retained after the child sink callback completes.
/// </summary>
public sealed class PcpRelayPacket
{
    internal PcpRelayPacket(
        long sequence,
        uint type,
        uint wirePosition,
        long? streamPosition,
        ReadOnlyMemory<byte> payload,
        PcpStreamDiscontinuity? discontinuity,
        bool isRetainedHeadReplay = false)
    {
        Sequence = sequence;
        Type = type;
        WirePosition = wirePosition;
        StreamPosition = streamPosition;
        Payload = payload;
        Discontinuity = discontinuity;
        IsRetainedHeadReplay = isRetainedHeadReplay;
    }

    public long Sequence { get; }

    public uint Type { get; }

    public uint WirePosition { get; }

    public long? StreamPosition { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public PcpStreamDiscontinuity? Discontinuity { get; }

    /// <summary>
    /// Gets whether this packet is the retained upstream HEAD replayed when a
    /// child joined the fan-out. Its source sequence predates that child.
    /// </summary>
    public bool IsRetainedHeadReplay { get; }
}
