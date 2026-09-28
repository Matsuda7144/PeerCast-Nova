namespace PeerCast.Nova.Core.Recording;

public enum RecordingSinkStopResultKind
{
    Stopped,
    AlreadyStopped,
    Rejected,
    Faulted,
}

/// <summary>
/// Expected outcome of a recorder stop request.
/// </summary>
public sealed record RecordingSinkStopResult
{
    private RecordingSinkStopResult(
        RecordingSinkStopResultKind kind,
        RecordingFaultDescriptor? fault = null)
    {
        Kind = kind;
        Fault = fault;
    }

    public static RecordingSinkStopResult Stopped { get; } =
        new(RecordingSinkStopResultKind.Stopped);

    public static RecordingSinkStopResult AlreadyStopped { get; } =
        new(RecordingSinkStopResultKind.AlreadyStopped);

    public static RecordingSinkStopResult Rejected { get; } =
        new(RecordingSinkStopResultKind.Rejected);

    public static RecordingSinkStopResult Faulted { get; } =
        new(RecordingSinkStopResultKind.Faulted);

    internal static RecordingSinkStopResult FromFault(
        RecordingFaultDescriptor fault)
    {
        ArgumentNullException.ThrowIfNull(fault);
        return new RecordingSinkStopResult(
            RecordingSinkStopResultKind.Faulted,
            fault);
    }

    public RecordingSinkStopResultKind Kind { get; }

    public RecordingFaultDescriptor? Fault { get; }
}
