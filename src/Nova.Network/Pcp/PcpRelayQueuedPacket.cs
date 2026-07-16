namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpRelayQueuedPacket : IDisposable
{
    private PcpRelaySharedPayload? _payload;

    public PcpRelayQueuedPacket(
        PcpChannelPacket source,
        PcpRelaySharedPayload payload)
    {
        payload.AddReference();

        try
        {
            _payload = payload;
            Packet = new PcpRelayPacket(
                source.Sequence,
                source.Type,
                source.WirePosition,
                source.StreamPosition,
                payload.Memory,
                source.Discontinuity);
        }
        catch
        {
            payload.Dispose();
            throw;
        }
    }

    public PcpRelayPacket Packet { get; }

    public int PayloadLength => Packet.Payload.Length;

    public void Dispose()
    {
        Interlocked.Exchange(ref _payload, null)?.Dispose();
    }
}
