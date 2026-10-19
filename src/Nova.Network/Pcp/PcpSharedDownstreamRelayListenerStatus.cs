namespace PeerCast.Nova.Network.Pcp;

internal enum PcpSharedDownstreamRelayListenerState
{
    Created,
    Running,
    Stopping,
    Stopped,
}

internal enum PcpSharedDownstreamRelayListenerEndReason
{
    Stopped,
    CallerCanceled,
    AcceptFailed,
}

/// <summary>
/// Fixed-field aggregate for one shared downstream listener. It contains no
/// address, channel/session identifier, agent, metadata, payload, or exception.
/// </summary>
internal sealed record PcpSharedDownstreamRelayListenerSnapshot(
    PcpSharedDownstreamRelayListenerState State,
    PcpSharedDownstreamRelayListenerEndReason? EndReason,
    long AcceptedConnectionCount,
    int ActiveConnectionCount,
    long CompletedConnectionCount,
    long RoutedConnectionCount,
    long BadRequestCount,
    long NotFoundCount,
    long HandshakeTimedOutCount,
    long HandlerFailureCount,
    long InternalFailureCount);

internal sealed record PcpSharedDownstreamRelayListenerResult(
    PcpSharedDownstreamRelayListenerEndReason EndReason,
    PcpSharedDownstreamRelayListenerSnapshot Snapshot);
