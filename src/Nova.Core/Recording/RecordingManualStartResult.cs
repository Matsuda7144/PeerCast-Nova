using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

public enum RecordingManualStartResultKind
{
    Started,
    AlreadyRunning,
    Rejected,
    Canceled,
}

/// <summary>
/// Result of an explicit user-initiated recording start.
/// </summary>
public sealed record RecordingManualStartResult
{
    internal RecordingManualStartResult(
        ChannelId channelId,
        RecordingManualStartResultKind kind,
        RecordingAutomationExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(state);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ChannelId = channelId;
        Kind = kind;
        State = state;
    }

    public ChannelId ChannelId { get; }

    public RecordingManualStartResultKind Kind { get; }

    public RecordingAutomationExecutionState State { get; }
}
