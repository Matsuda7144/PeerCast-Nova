using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Recording adapter over the existing PCP channel-session implementation.
/// </summary>
public sealed class PcpChannelSessionRecordingRunner :
    IPcpRecordingChannelSessionRunner
{
    private readonly TcpTransportConnector connector;
    private readonly PcpChannelSessionOptions options;

    public PcpChannelSessionRecordingRunner(
        TcpTransportConnector connector,
        PcpChannelSessionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(connector);

        var effectiveOptions = options is null
            ? new PcpChannelSessionOptions()
            : options with
            {
                Stream = options.Stream with { },
            };
        effectiveOptions.Validate();

        this.connector = connector;
        this.options = effectiveOptions;
    }

    public async Task<PcpChannelSessionResult> RunAsync(
        PcpRecordingSourceEndpoint endpoint,
        RecordingSinkStartRequest request,
        IPcpChannelPacketConsumer consumer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(consumer);
        cancellationToken.ThrowIfCancellationRequested();

        var tracker = new ChannelStatusTracker(
            request.Candidate.ChannelId,
            new ChannelSessionId(Guid.NewGuid()));

        await using var session = new PcpChannelSession(
            connector,
            tracker,
            consumer,
            options);

        return await session.RunAsync(
            endpoint.Host,
            endpoint.Port,
            endpoint.InitialStreamPosition,
            cancellationToken).ConfigureAwait(false);
    }
}
