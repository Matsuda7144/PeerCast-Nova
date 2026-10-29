using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Publishing;

public enum ObsPublishLocalTerminationOutcome
{
    LocalCompleted, TimedOut, Cancelled, DeliveryFailed, CleanupIncomplete, Failed,
}

public enum ObsPublishLocalTerminationPhase
{
    Admission, Ingress, PacketDrain, Relay, Http, RecordingSource, Fanout, Cleanup, Finished,
}

public enum ObsPublishWriterEvidence { NotRequired, NotObserved }

/// <summary>Actual owned-resource cleanup tasks, independent of media delivery and file finalization.</summary>
public sealed record ObsPublishOwnerCleanupEvidence(
    bool Ingress, bool Relay, bool Fanout, bool HttpSource, bool HttpListenerOwned, bool HttpListener, bool RecordingLeases)
{
    public bool ForcedStopRequested { get; init; }
    public bool ForcedCancellationCompleted { get; init; }
    public bool RecordingFaultNotificationCompleted { get; init; }
    public bool WanNetwork { get; init; } = true;
    public bool Completed => WanNetwork && Ingress && Relay && Fanout && HttpSource && HttpListener && RecordingLeases &&
        (!ForcedStopRequested || (ForcedCancellationCompleted && RecordingFaultNotificationCompleted));
}

/// <summary>Nova local termination; never proof of OBS EOS, remote playback or writer finalization.</summary>
public sealed record ObsPublishLocalTerminationResult(
    ChannelId ChannelId, ChannelSessionId SessionId,
    ObsPublishLocalTerminationOutcome Outcome, ObsPublishLocalTerminationPhase Phase, bool DeadlineExpired,
    bool RelayAdmissionClosed, bool UpstreamEnded, bool AcceptedCountersMatched,
    ObsRtmpQuiescenceResult? Ingress, PcpRelayQuiescenceResult? Relay,
    PcpRelayFanoutCompletionResult? Fanout, PcpHttpFlvCompletionResult? Http,
    PcpRecordingSourceCompletionResult? RecordingSource,
    ObsPublishWriterEvidence WriterEvidence, ObsPublishOwnerCleanupEvidence? Cleanup)
{
    public PcpRecordingSourceEvidence? RecordingFinalEvidence { get; init; }
    public bool HttpSourceCleanupFailed { get; init; }
    public bool CleanupCompleted => Cleanup?.Completed == true;
}
