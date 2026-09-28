using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

public enum RecordingAutomationStopResultKind
{
    NoActiveSession,
    Stopped,
    AlreadyStopped,
    Rejected,
    Faulted,
}

/// <summary>
/// Result of explicitly stopping one channel coordinator.
/// </summary>
public sealed record RecordingAutomationStopResult
{
    internal RecordingAutomationStopResult(
        ChannelId channelId,
        RecordingAutomationStopResultKind kind,
        RecordingAutomationExecutionState state,
        RecordingFaultDescriptor? fault = null)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(state);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind != RecordingAutomationStopResultKind.Faulted &&
            fault is not null)
        {
            throw new ArgumentException(
                "Only a faulted stop result may expose a fault descriptor.",
                nameof(fault));
        }

        ChannelId = channelId;
        Kind = kind;
        State = state;
        Fault = fault;
    }

    public ChannelId ChannelId { get; }

    public RecordingAutomationStopResultKind Kind { get; }

    public RecordingAutomationExecutionState State { get; }

    public RecordingFaultDescriptor? Fault { get; }
}
