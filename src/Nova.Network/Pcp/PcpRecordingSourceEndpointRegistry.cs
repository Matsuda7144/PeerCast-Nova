using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Bounded in-memory mapping from channel identity to the current PCP
/// recording source endpoint. Discovery/catalog layers may update it without
/// coupling the recording engine to those layers.
/// </summary>
public sealed class PcpRecordingSourceEndpointRegistry :
    IPcpRecordingSourceEndpointProvider
{
    public const int DefaultMaximumEndpointCount = 4096;

    private readonly object gate = new();
    private readonly int maximumEndpointCount;
    private readonly Dictionary<ChannelId, PcpRecordingSourceEndpoint>
        endpoints = [];

    public PcpRecordingSourceEndpointRegistry(
        int maximumEndpointCount = DefaultMaximumEndpointCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumEndpointCount);

        this.maximumEndpointCount = maximumEndpointCount;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return endpoints.Count;
            }
        }
    }

    public void Upsert(
        ChannelId channelId,
        PcpRecordingSourceEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(endpoint);

        lock (gate)
        {
            if (!endpoints.ContainsKey(channelId) &&
                endpoints.Count >= maximumEndpointCount)
            {
                throw new InvalidOperationException(
                    "The PCP recording source endpoint limit has been reached.");
            }

            endpoints[channelId] = endpoint;
        }
    }

    public bool Remove(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        lock (gate)
        {
            return endpoints.Remove(channelId);
        }
    }

    public bool TryGet(
        ChannelId channelId,
        out PcpRecordingSourceEndpoint? endpoint)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        lock (gate)
        {
            return endpoints.TryGetValue(
                channelId,
                out endpoint);
        }
    }

    public ValueTask<PcpRecordingSourceEndpoint?> ResolveAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        _ = TryGet(
            request.Candidate.ChannelId,
            out var endpoint);

        return ValueTask.FromResult(endpoint);
    }
}
