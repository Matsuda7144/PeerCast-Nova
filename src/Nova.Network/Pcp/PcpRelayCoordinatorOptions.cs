using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Finite upstream, fan-out, downstream, and graceful-drain limits for one
/// coordinated channel relay.
/// </summary>
public sealed record PcpRelayCoordinatorOptions
{
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public PcpChannelSessionOptions UpstreamSession { get; init; } = new();

    public TcpTransportOptions DownstreamTransport { get; init; } = new();

    public PcpRelayFanoutOptions Fanout { get; init; } = new();

    public PcpDownstreamChildOptions DownstreamChild { get; init; } = new();

    public void Validate()
    {
        if (DrainTimeout <= TimeSpan.Zero ||
            DrainTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(DrainTimeout),
                DrainTimeout,
                "The relay drain timeout must be positive and no greater than one minute.");
        }

        ArgumentNullException.ThrowIfNull(UpstreamSession);
        ArgumentNullException.ThrowIfNull(DownstreamTransport);
        ArgumentNullException.ThrowIfNull(Fanout);
        ArgumentNullException.ThrowIfNull(DownstreamChild);

        UpstreamSession.Validate();
        DownstreamTransport.Validate();
        Fanout.Validate();
        DownstreamChild.Validate();
    }
}
