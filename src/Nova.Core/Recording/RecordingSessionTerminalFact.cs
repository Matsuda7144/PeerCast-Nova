namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Immutable facts observed for one admitted writer session. Observation times
/// are not media timestamps or exact file creation/finalization times.
/// </summary>
public sealed record RecordingSessionTerminalFact
{
    public RecordingSessionTerminalFact(
        RecordingSinkSession session,
        string? channelName,
        string? streamType,
        string? requestedFileExtension,
        DateTimeOffset admissionObservedAtUtc,
        DateTimeOffset terminalObservedAtUtc,
        RecordingSinkSessionLifecycleKind kind,
        RecordingFileWriteResult? writeResult = null,
        RecordingFaultDescriptor? fault = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Enum.IsDefined(kind) || kind == RecordingSinkSessionLifecycleKind.Active)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind == RecordingSinkSessionLifecycleKind.Completed && writeResult is null)
        {
            throw new ArgumentException("Completed sessions require writer-proven file facts.", nameof(writeResult));
        }

        if (kind == RecordingSinkSessionLifecycleKind.NoDecodableVideoStart && writeResult is not null)
        {
            throw new ArgumentException("A no-start session cannot have a finalized file.", nameof(writeResult));
        }

        if (kind != RecordingSinkSessionLifecycleKind.Faulted && fault is not null)
        {
            throw new ArgumentException("Only a faulted session may include a fault descriptor.", nameof(fault));
        }

        Session = session;
        ChannelName = channelName;
        StreamType = streamType;
        RequestedFileExtension = requestedFileExtension;
        AdmissionObservedAtUtc = admissionObservedAtUtc.ToUniversalTime();
        TerminalObservedAtUtc = terminalObservedAtUtc.ToUniversalTime();
        Kind = kind;
        WriteResult = writeResult;
        Fault = fault;
    }

    public RecordingSinkSession Session { get; }
    public string? ChannelName { get; }
    public string? StreamType { get; }
    public string? RequestedFileExtension { get; }
    public DateTimeOffset AdmissionObservedAtUtc { get; }
    public DateTimeOffset TerminalObservedAtUtc { get; }
    public RecordingSinkSessionLifecycleKind Kind { get; }
    public RecordingFileWriteResult? WriteResult { get; }
    public RecordingFaultDescriptor? Fault { get; }
}
