using System.IO.Pipelines;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Handles one already-routed downstream connection. The shared listener keeps
/// ownership of the connection and PipeReader; implementations borrow both only
/// for the duration of the call. The selected fan-out is route-leased for that
/// same duration.
/// </summary>
internal interface IPcpDownstreamRoutedConnectionHandler
{
    ValueTask<PcpDownstreamChildResult> RunAsync(
        TcpTransportConnection connection,
        PipeReader reader,
        PcpDownstreamRelayRequest request,
        PcpRelayFanout fanout,
        CancellationToken cancellationToken);
}
