using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

public enum PcpDownstreamRelayServerState
{
    Created,
    Running,
    Stopping,
    Stopped,
    Quiescent,
}

public enum PcpDownstreamRelayServerEndReason
{
    Stopped,
    CallerCanceled,
    AcceptFailed,
    Quiesced,
}

/// <summary>
/// Fixed-field, content-free listener/session aggregate. It deliberately has
/// no endpoint, channel/session identifier, agent, metadata, payload,
/// position, exception, or peer-controlled text.
/// </summary>
public sealed record PcpDownstreamRelayServerSnapshot
{
    public PcpWanIngressSnapshot? WanIngress { get; init; }
    internal PcpDownstreamRelayServerSnapshot(
        PcpDownstreamRelayServerState state,
        PcpDownstreamRelayServerEndReason? endReason,
        long acceptedConnectionCount,
        int activeConnectionCount,
        long completedConnectionCount,
        long acceptedHandshakeCount,
        long relayFullCount,
        long protocolRejectedCount,
        long handshakeTimedOutCount,
        long quitReceivedCount,
        long connectionEndedCount,
        long fanoutStoppedCount,
        long slowConsumerCount,
        long writeTimedOutCount,
        long writeFailedCount,
        long stoppedCount,
        long cleanupIncompleteCount,
        long deliveredPacketCount,
        long deliveredByteCount,
        long discardedPacketCount,
        long discardedByteCount,
        long internalFailureCount)
    {
        State = state;
        EndReason = endReason;
        AcceptedConnectionCount = acceptedConnectionCount;
        ActiveConnectionCount = activeConnectionCount;
        CompletedConnectionCount = completedConnectionCount;
        AcceptedHandshakeCount = acceptedHandshakeCount;
        RelayFullCount = relayFullCount;
        ProtocolRejectedCount = protocolRejectedCount;
        HandshakeTimedOutCount = handshakeTimedOutCount;
        QuitReceivedCount = quitReceivedCount;
        ConnectionEndedCount = connectionEndedCount;
        FanoutStoppedCount = fanoutStoppedCount;
        SlowConsumerCount = slowConsumerCount;
        WriteTimedOutCount = writeTimedOutCount;
        WriteFailedCount = writeFailedCount;
        StoppedCount = stoppedCount;
        CleanupIncompleteCount = cleanupIncompleteCount;
        DeliveredPacketCount = deliveredPacketCount;
        DeliveredByteCount = deliveredByteCount;
        DiscardedPacketCount = discardedPacketCount;
        DiscardedByteCount = discardedByteCount;
        InternalFailureCount = internalFailureCount;
    }

    public PcpDownstreamRelayServerState State { get; }

    public PcpDownstreamRelayServerEndReason? EndReason { get; }

    public long AcceptedConnectionCount { get; }

    public int ActiveConnectionCount { get; }

    public long CompletedConnectionCount { get; }

    public long AcceptedHandshakeCount { get; }

    public long RelayFullCount { get; }

    public long ProtocolRejectedCount { get; }

    public long HandshakeTimedOutCount { get; }

    public long QuitReceivedCount { get; }

    public long ConnectionEndedCount { get; }

    public long FanoutStoppedCount { get; }

    public long SlowConsumerCount { get; }

    public long WriteTimedOutCount { get; }

    public long WriteFailedCount { get; }

    public long StoppedCount { get; }

    public long CleanupIncompleteCount { get; }

    public long DeliveredPacketCount { get; }

    public long DeliveredByteCount { get; }

    public long DiscardedPacketCount { get; }

    public long DiscardedByteCount { get; }

    public long InternalFailureCount { get; }

    public long AdmissionClosedCount { get; init; }
    public long AdmissionCloseFailedCount { get; init; }
    public int AdmissionCleanupPendingCount { get; init; }
    public long AdmissionCleanupFailedCount { get; init; }
    public PcpDownstreamTrafficSnapshot? Traffic { get; init; }
    public long PacingLimitedCount { get; init; }
}

public enum PcpRelayQuiescenceOutcome { Drained, TimedOut, Cancelled, DeliveryFailed, CleanupFailed, Failed }

/// <summary>Local delivery/cleanup evidence only; never remote playback proof.</summary>
public sealed record PcpRelayQuiescenceResult(
    PcpRelayQuiescenceOutcome Outcome,
    bool SendsDrained,
    bool CleanupCompleted,
    PcpDownstreamRelayServerSnapshot Snapshot);

public sealed record PcpDownstreamRelayServerResult(
    PcpDownstreamRelayServerEndReason EndReason,
    PcpDownstreamRelayServerSnapshot Snapshot);
