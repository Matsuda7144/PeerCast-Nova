namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Optional recorder capability used to reconcile sessions that ended
/// independently of automation policy evaluation.
/// </summary>
public interface IRecordingSinkSessionMonitor
{
    bool TryGetSessionStatus(
        RecordingSinkSession session,
        out RecordingSinkSessionLifecycleStatus? status);
}
