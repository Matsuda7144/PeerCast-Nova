namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Identifies a stable local channel session. The value remains unchanged when
/// the session reconnects to an upstream peer.
/// </summary>
public sealed record ChannelSessionId
{
    public ChannelSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A channel session identifier cannot be empty.",
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
