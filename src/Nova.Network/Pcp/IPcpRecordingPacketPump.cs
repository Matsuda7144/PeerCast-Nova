using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Produces one recording-compatible PCP packet sequence for a recording
/// request. Implementations must begin at packet sequence zero and include any
/// media bootstrap bytes required for the resulting file to be independently
/// consumable. Packet delivery must preserve order and observe cancellation.
/// </summary>
public interface IPcpRecordingPacketPump
{
    Task RunAsync(
        RecordingSinkStartRequest request,
        IPcpChannelPacketConsumer consumer,
        CancellationToken cancellationToken);
}
