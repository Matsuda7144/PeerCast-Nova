namespace PeerCast.Nova.Protocol.Pcp;

public sealed record PcpChannelControlLimits
{
    public int MaxTextBytes { get; init; } = 4 * 1024;

    public void Validate()
    {
        if (MaxTextBytes < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxTextBytes)} must be positive.");
        }
    }
}
