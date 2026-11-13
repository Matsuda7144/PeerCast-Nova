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
    private readonly Func<ChannelId, PcpRelayFanout, PcpDownstreamRelayServer?>? _resolveOwner;

    public PcpDownstreamChildSessionRoutedConnectionHandler(
        Guid localSessionId,
        PcpDownstreamChildOptions? options = null,
        Func<ChannelId, PcpRelayFanout, IPcpDownstreamRelayAdmission?>? resolveAdmission = null,
        Func<ChannelId, PcpRelayFanout, PcpDownstreamRelayServer?>? resolveOwner = null)
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
        _resolveOwner = resolveOwner;
    }

    public async ValueTask<PcpDownstreamChildResult> RunAsync(
        TcpTransportConnection connection,
        PipeReader reader,
        PcpDownstreamRelayRequest request,
        PcpRelayFanout fanout,
        CancellationToken cancellationToken)
    {
        if (_resolveOwner is not null)
            return await RunOwnerAsync(connection, reader, request, fanout, null, cancellationToken).ConfigureAwait(false);
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

    public ValueTask<PcpDownstreamChildResult> RunWanAsync(TcpTransportConnection connection, PipeReader reader,
        PcpDownstreamRelayRequest request, PcpRelayFanout fanout, PcpWanIngressPolicy.Lease ingress,
        CancellationToken token) => RunOwnerAsync(connection, reader, request, fanout, ingress, token);

    private async ValueTask<PcpDownstreamChildResult> RunOwnerAsync(TcpTransportConnection connection, PipeReader reader,
        PcpDownstreamRelayRequest request, PcpRelayFanout fanout, PcpWanIngressPolicy.Lease? ingress, CancellationToken token)
    {
        var owner = _resolveOwner?.Invoke(new(request.ChannelId), fanout);
        if (owner is null)
        {
            await connection.WriteAsync(PcpDownstreamHttpResponse.CreateRejection(404, _options.UserAgent), token).ConfigureAwait(false);
            throw new OperationCanceledException("No current channel owner.");
        }
        return await owner.RunRoutedAsync(connection, reader, request, ingress, token).ConfigureAwait(false);
    }
}
