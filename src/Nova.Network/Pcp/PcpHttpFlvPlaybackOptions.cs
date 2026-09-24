using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Bounds the independent loopback HTTP FLV playback candidate.
/// </summary>
public sealed record PcpHttpFlvPlaybackOptions
{
    public int Port { get; init; }

    public int MaximumConcurrentStreamClients { get; init; } = 1;

    public int MaximumHttpHeaderBytes { get; init; } = 32 * 1024;

    public TimeSpan RequestHeaderTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan BootstrapHeaderTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan WriteTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public PcpFlvBootstrapRetentionOptions Bootstrap { get; init; } = new();

    public PcpRelayFanoutOptions Fanout { get; init; } = new()
    {
        MaxChildren = 1,
        MaxPendingPacketsPerChild = 8,
        MaxPendingBytesPerChild = 4 * 1024 * 1024,
    };

    public TcpTransportOptions Transport { get; init; } = new()
    {
        MaxConcurrentConnections = 8,
    };

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Bootstrap);
        ArgumentNullException.ThrowIfNull(Fanout);
        ArgumentNullException.ThrowIfNull(Transport);
        Bootstrap.Validate();
        Fanout.Validate();
        Transport.Validate();

        if (Port is < 0 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(Port), Port, "Port must be between zero and 65535.");
        }

        if (MaximumConcurrentStreamClients is < 1 or > PcpRelayFanoutOptions.MaximumChildrenLimit ||
            MaximumConcurrentStreamClients > Transport.MaxConcurrentConnections)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumConcurrentStreamClients),
                MaximumConcurrentStreamClients,
                "The streaming-client bound must fit within the HTTP connection bound.");
        }

        if (MaximumHttpHeaderBytes is < 64 or > 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumHttpHeaderBytes));
        }

        ValidateTimeout(RequestHeaderTimeout, nameof(RequestHeaderTimeout));
        ValidateTimeout(BootstrapHeaderTimeout, nameof(BootstrapHeaderTimeout));
        ValidateTimeout(WriteTimeout, nameof(WriteTimeout));
        ValidateTimeout(CleanupTimeout, nameof(CleanupTimeout));
    }

    private static void ValidateTimeout(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                name,
                value,
                "Timeouts must be positive and no greater than one minute.");
        }
    }
}
