using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Runs one PCP channel session for recording using an already resolved source.
/// </summary>
public interface IPcpRecordingChannelSessionRunner
{
    Task<PcpChannelSessionResult> RunAsync(
        PcpRecordingSourceEndpoint endpoint,
        RecordingSinkStartRequest request,
        IPcpChannelPacketConsumer consumer,
        CancellationToken cancellationToken);
}
