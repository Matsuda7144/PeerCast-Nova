using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Finite limits for the live CHAN control and packet boundary.
/// </summary>
public sealed record PcpChannelStreamOptions
{
    public int MaxAtomContentBytes { get; init; } = 1024 * 1024;

    public int MaxPacketBytes { get; init; } = 1024 * 1024;

    public int MaxTextBytes { get; init; } = 4 * 1024;

    public TimeSpan ReadIdleTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public void Validate()
    {
        if (MaxAtomContentBytes < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxAtomContentBytes)} must be positive.");
        }

        if (MaxPacketBytes < 0 ||
            MaxPacketBytes > MaxAtomContentBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxPacketBytes)} must be between zero and {nameof(MaxAtomContentBytes)}.");
        }

        if (MaxTextBytes < 1 || MaxTextBytes > MaxAtomContentBytes)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxTextBytes)} must be positive and no greater than {nameof(MaxAtomContentBytes)}.");
        }

        if (ReadIdleTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ReadIdleTimeout)} must be positive.");
        }
    }

    internal PcpProtocolLimits CreateProtocolLimits()
    {
        return new PcpProtocolLimits
        {
            MaxContentLength = MaxAtomContentBytes,
        };
    }

    internal PcpChannelControlLimits CreateControlLimits()
    {
        return new PcpChannelControlLimits
        {
            MaxTextBytes = MaxTextBytes,
        };
    }
}
