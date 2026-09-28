namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Generic lifecycle snapshot for a recorder-owned session.
/// </summary>
public sealed record RecordingSinkSessionLifecycleStatus
{
    public RecordingSinkSessionLifecycleStatus(
        RecordingSinkSession session,
        RecordingSinkSessionLifecycleKind kind,
        RecordingFaultDescriptor? fault = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind != RecordingSinkSessionLifecycleKind.Faulted &&
            fault is not null)
        {
            throw new ArgumentException(
                "Only a faulted recording session may expose a fault descriptor.",
                nameof(fault));
        }

        Session = session;
        Kind = kind;
        Fault = fault;
    }

    public RecordingSinkSession Session { get; }

    public RecordingSinkSessionLifecycleKind Kind { get; }

    public RecordingFaultDescriptor? Fault { get; }
}
