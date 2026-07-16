namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Consumes one packet while its payload memory remains valid. The consumer
/// must finish using the payload before the returned ValueTask completes.
/// Awaiting this method is the upstream backpressure boundary.
/// Implementations must observe cancellation and return promptly so the owning
/// Network session can stop within its configured module deadline.
/// </summary>
public interface IPcpChannelPacketConsumer
{
    ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken);
}
