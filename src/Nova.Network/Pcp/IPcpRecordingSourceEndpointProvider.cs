using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Resolves the upstream PCP endpoint for a recording request.
/// </summary>
public interface IPcpRecordingSourceEndpointProvider
{
    ValueTask<PcpRecordingSourceEndpoint?> ResolveAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken);
}
