using System.IO.Pipelines;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Production handoff from the shared downstream listener into the existing
/// child-session lifecycle. The listener keeps ownership of the connection and
/// PipeReader while the child consumes the already-routed request and remaining
/// buffered PCP bytes.
/// </summary>
internal sealed class PcpDownstreamChildSessionRoutedConnectionHandler :
    IPcpDownstreamRoutedConnectionHandler
{
    private readonly Guid _localSessionId;
    private readonly PcpDownstreamChildOptions _options;
    private readonly Func<ChannelId, PcpRelayFanout, IPcpDownstreamRelayAdmission?>? _resolveAdmission;

    public PcpDownstreamChildSessionRoutedConnectionHandler(
        Guid localSessionId,
        PcpDownstreamChildOptions? options = null,
        Func<ChannelId, PcpRelayFanout, IPcpDownstreamRelayAdmission?>? resolveAdmission = null)
    {
        if (localSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The local session identifier cannot be empty.",
                nameof(localSessionId));
        }

        _localSessionId = localSessionId;
        _options = options is null
            ? new PcpDownstreamChildOptions()
            : options with { };
        _options.Validate();
        _resolveAdmission = resolveAdmission;
    }

    public async ValueTask<PcpDownstreamChildResult> RunAsync(
        TcpTransportConnection connection,
        PipeReader reader,
        PcpDownstreamRelayRequest request,
        PcpRelayFanout fanout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fanout);

        await using var session = PcpDownstreamChildSession.CreateRouted(
            connection,
            reader,
            request,
            fanout,
            _localSessionId,
            _options,
            admission: _resolveAdmission?.Invoke(new ChannelId(request.ChannelId), fanout));

        return await session.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
