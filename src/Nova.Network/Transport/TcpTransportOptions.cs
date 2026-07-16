namespace PeerCast.Nova.Network.Transport;

/// <summary>
/// Defines finite resource and timeout limits for TCP transport primitives.
/// </summary>
public sealed record TcpTransportOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public int ListenBacklog { get; init; } = 128;

    public int MaxConcurrentConnections { get; init; } = 128;

    public int ReceiveBufferSize { get; init; } = 64 * 1024;

    public int SendBufferSize { get; init; } = 64 * 1024;

    public bool NoDelay { get; init; } = true;

    public bool KeepAlive { get; init; } = true;

    public bool EnableDualMode { get; init; } = true;

    public void Validate()
    {
        if (ConnectTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ConnectTimeout)} must be positive.");
        }

        if (ListenBacklog < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(ListenBacklog)} must be at least one.");
        }

        if (MaxConcurrentConnections < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(MaxConcurrentConnections)} must be at least one.");
        }

        if (ReceiveBufferSize < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(ReceiveBufferSize)} must be at least one.");
        }

        if (SendBufferSize < 1)
        {
            throw new InvalidOperationException(
                $"{nameof(SendBufferSize)} must be at least one.");
        }
    }
}
