using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal enum NovaObsPublishingAvailability
{
    Unavailable,
    NotStarted,
    Observed,
    Stopped,
}

/// <summary>
/// Bounded application facts for a borrowed publishing owner. Connection
/// endpoints, stream routes, raw exceptions and resources are deliberately absent.
/// Channel identity is configuration/ownership evidence, not proof of media.
/// </summary>
internal sealed record NovaObsPublishingReadOnlySnapshot(
    NovaObsPublishingAvailability Availability,
    Guid? ChannelId,
    Guid? SessionId,
    string StateText,
    string DiagnosticText,
    NovaObsPublishingTone Tone,
    ObsPublishChannelEndReason? EndReason,
    ObsRtmpRejectionStage? RejectionStage,
    ObsRtmpRejectionReason? RejectionReason)
{
    public long? Generation { get; init; }
    public bool IsSourceNotStarted { get; init; }
    public NovaObsPublishingLifecycleState? LifecycleState { get; init; }
    public NovaObsPublishingCompletionKind? CompletionKind { get; init; }
    public NovaObsPublishingLifecycleFailure? LifecycleFailure { get; init; }
    public ObsPublishLocalTerminationOutcome? LocalTerminationOutcome { get; init; }
    public ObsPublishWriterEvidence? WriterEvidence { get; init; }
    public bool? CleanupCompleted { get; init; }
    public bool CanRequestFinish { get; init; }

}

internal interface INovaObsPublishingReadOnlyStatus
{
    NovaObsPublishingReadOnlySnapshot Read();
}

/// <summary>
/// Adapts the existing status source/projection without creating, starting,
/// stopping, polling or disposing an owner. One adapter borrows one generation;
/// selecting a replacement owner requires a new adapter at composition time.
/// </summary>
internal sealed class NovaObsPublishingReadOnlyStatus(INovaObsPublishingStatusSource? source)
    : INovaObsPublishingReadOnlyStatus
{
    private readonly object _gate = new();
    private readonly NovaObsPublishingProjection _projection = new();
    private Guid? _sessionId;
    private NovaObsPublishingReadOnlySnapshot? _last;

    internal Action? ContendedReadObserver { get; set; }

    public NovaObsPublishingReadOnlySnapshot Read()
    {
        if (!Monitor.TryEnter(_gate))
        {
            ContendedReadObserver?.Invoke(); // No adapter or admission lock held here.
            Monitor.Enter(_gate);
        }
        try
        {
            if (source is null) return Unavailable("配信ownerが接続されていません。");
            ObsPublishStatusSnapshot status;
            try { status = source.Snapshot; }
            catch { return Unavailable("配信状態を取得できません。"); }

            if (status is null || status.ChannelId is null || status.SessionId is null ||
                status.ChannelId.Value == Guid.Empty || status.SessionId.Value == Guid.Empty ||
                status.Revision < 0 || !Enum.IsDefined(status.State))
                return Unavailable("配信状態の識別情報を確認できません。");

            if (_sessionId is null)
            {
                _sessionId = status.SessionId.Value;
                _projection.SelectGeneration(1, _sessionId.Value);
            }
            if (_sessionId != status.SessionId.Value ||
                (_last?.ChannelId is { } channelId && channelId != status.ChannelId.Value))
                return Unavailable("接続先の配信generationが変更されました。");

            var panel = _projection.Project(1, status);
            if (panel is null) return _last ?? Unavailable("配信状態を確認できません。");
            var notStarted = status.State == ChannelSessionState.Created &&
                !status.ListenerReady && !status.PublisherConnected && !status.HeadAvailable &&
                status.EndReason is null;
            var availability = panel.IsTerminal ? NovaObsPublishingAvailability.Stopped :
                notStarted ? NovaObsPublishingAvailability.NotStarted : NovaObsPublishingAvailability.Observed;
            _last = new(availability, status.ChannelId.Value, status.SessionId.Value,
                notStarted ? "未起動（待受未開始）" : panel.StateText,
                panel.DiagnosticText, panel.Tone, status.EndReason,
                status.RejectionStage, status.RejectionReason)
            { IsSourceNotStarted = notStarted };
            return _last;
        }
        finally { Monitor.Exit(_gate); }
    }

    private static NovaObsPublishingReadOnlySnapshot Unavailable(string diagnostic) =>
        new(NovaObsPublishingAvailability.Unavailable, null, null, "利用不可", diagnostic,
            NovaObsPublishingTone.Warning, null, null, null);
}
