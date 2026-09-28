namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Immutable request to stop one concrete recording session.
/// </summary>
public sealed record RecordingSinkStopRequest
{
    public RecordingSinkStopRequest(
        RecordingSinkSession session)
    {
        Session = session ??
            throw new ArgumentNullException(nameof(session));
    }

    public RecordingSinkSession Session { get; }
}
