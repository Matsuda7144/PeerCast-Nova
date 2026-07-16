namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Identifies one PeerCast channel independently of any local connection or
/// upstream peer session.
/// </summary>
public sealed record ChannelId
{
    public ChannelId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "A channel identifier cannot be empty.",
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
