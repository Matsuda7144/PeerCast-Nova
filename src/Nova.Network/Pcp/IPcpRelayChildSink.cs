namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Sends one relay packet while its payload memory remains valid. A sink must
/// finish using the payload before the returned ValueTask completes and must
/// observe cancellation promptly.
/// </summary>
public interface IPcpRelayChildSink
{
    ValueTask SendAsync(
        PcpRelayPacket packet,
        CancellationToken cancellationToken);
}
