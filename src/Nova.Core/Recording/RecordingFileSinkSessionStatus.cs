namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Snapshot of one file sink session without exposing implementation tasks.
/// </summary>
public sealed record RecordingFileSinkSessionStatus
{
    internal RecordingFileSinkSessionStatus(
        RecordingSinkSession session,
        RecordingFileSinkSessionStatusKind kind,
        RecordingFileWriteResult? writeResult,
        RecordingFaultDescriptor? fault = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == RecordingFileSinkSessionStatusKind.Completed &&
            writeResult is null)
        {
            throw new ArgumentException(
                "A completed file sink session must expose its write result.",
                nameof(writeResult));
        }

        if (kind != RecordingFileSinkSessionStatusKind.Completed &&
            writeResult is not null)
        {
            throw new ArgumentException(
                "Only a completed file sink session may expose a write result.",
                nameof(writeResult));
        }

        if (kind != RecordingFileSinkSessionStatusKind.Faulted &&
            fault is not null)
        {
            throw new ArgumentException(
                "Only a faulted file sink session may expose a fault descriptor.",
                nameof(fault));
        }

        Session = session;
        Kind = kind;
        WriteResult = writeResult;
        Fault = fault;
    }

    public RecordingSinkSession Session { get; }

    public RecordingFileSinkSessionStatusKind Kind { get; }

    public RecordingFileWriteResult? WriteResult { get; }

    public RecordingFaultDescriptor? Fault { get; }
}
