using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Resolves one PCP source and runs it through the existing channel-session
/// implementation for the recording stream bridge.
/// </summary>
public sealed class PcpChannelSessionRecordingPacketPump :
    IPcpRecordingPacketPump
{
    private readonly IPcpRecordingSourceEndpointProvider endpointProvider;
    private readonly IPcpRecordingChannelSessionRunner sessionRunner;

    public PcpChannelSessionRecordingPacketPump(
        IPcpRecordingSourceEndpointProvider endpointProvider,
        IPcpRecordingChannelSessionRunner sessionRunner)
    {
        ArgumentNullException.ThrowIfNull(endpointProvider);
        ArgumentNullException.ThrowIfNull(sessionRunner);

        this.endpointProvider = endpointProvider;
        this.sessionRunner = sessionRunner;
    }

    public async Task RunAsync(
        RecordingSinkStartRequest request,
        IPcpChannelPacketConsumer consumer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(consumer);
        cancellationToken.ThrowIfCancellationRequested();

        var endpoint = await endpointProvider.ResolveAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        if (endpoint is null)
        {
            throw new InvalidOperationException(
                "No PCP recording source endpoint is available for the requested channel.");
        }

        var result = await sessionRunner.RunAsync(
            endpoint,
            request,
            consumer,
            cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(result);

        switch (result.EndReason)
        {
            case PcpChannelSessionEndReason.Stopped:
            case PcpChannelSessionEndReason.QuitReceived:
            case PcpChannelSessionEndReason.ConnectionEnded:
                return;

            case PcpChannelSessionEndReason.RelayFull:
                throw new IOException(
                    "The PCP recording source rejected the session because relay capacity is full.");

            case PcpChannelSessionEndReason.HttpRejected:
                throw new IOException(
                    "The PCP recording source rejected the HTTP channel request.");

            case PcpChannelSessionEndReason.ProtocolRejected:
                throw new InvalidDataException(
                    "The PCP recording source rejected the channel protocol negotiation.");

            default:
                throw new InvalidOperationException(
                    $"Unsupported PCP recording session end reason: {result.EndReason}.");
        }
    }
}
