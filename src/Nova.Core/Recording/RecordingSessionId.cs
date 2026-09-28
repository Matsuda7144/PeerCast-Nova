namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Identifies one concrete recorder session.
/// </summary>
public sealed record RecordingSessionId
{
    public RecordingSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A recording session identifier cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public override string ToString()
    {
        return Value.ToString("N").ToUpperInvariant();
    }
}
