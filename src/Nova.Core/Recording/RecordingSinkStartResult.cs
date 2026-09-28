namespace PeerCast.Nova.Core.Recording;

public enum RecordingSinkStartResultKind
{
    Started,
    AlreadyRunning,
    Rejected,
}

/// <summary>
/// Expected outcome of a recorder start request.
/// </summary>
public sealed record RecordingSinkStartResult
{
    private RecordingSinkStartResult(
        RecordingSinkStartResultKind kind,
        RecordingSinkSession? session)
    {
        Kind = kind;
        Session = session;
    }

    public static RecordingSinkStartResult Started(
        RecordingSinkSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new RecordingSinkStartResult(
            RecordingSinkStartResultKind.Started,
            session);
    }

    public static RecordingSinkStartResult AlreadyRunning(
        RecordingSinkSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new RecordingSinkStartResult(
            RecordingSinkStartResultKind.AlreadyRunning,
            session);
    }

    public static RecordingSinkStartResult Rejected { get; } =
        new(
            RecordingSinkStartResultKind.Rejected,
            session: null);

    public RecordingSinkStartResultKind Kind { get; }

    public RecordingSinkSession? Session { get; }
}
