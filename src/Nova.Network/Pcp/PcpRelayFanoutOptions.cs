namespace PeerCast.Nova.Network.Pcp;

public sealed record PcpRelayFanoutOptions
{
    public const int MaximumChildrenLimit = 1024;
    public const int MaximumPendingPacketsLimit = 4096;
    public const long MaximumPendingBytesLimit = 1024L * 1024 * 1024;

    public int MaxChildren { get; init; } = 32;

    public int MaxPendingPacketsPerChild { get; init; } = 8;

    public long MaxPendingBytesPerChild { get; init; } = 4L * 1024 * 1024;

    public TimeSpan ChildStopTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public void Validate()
    {
        if (MaxChildren is < 1 or > MaximumChildrenLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxChildren),
                MaxChildren,
                $"The child limit must be between 1 and {MaximumChildrenLimit}.");
        }

        if (MaxPendingPacketsPerChild is < 1 or > MaximumPendingPacketsLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingPacketsPerChild),
                MaxPendingPacketsPerChild,
                $"The pending packet limit must be between 1 and {MaximumPendingPacketsLimit}.");
        }

        if (MaxPendingBytesPerChild is < 1 or > MaximumPendingBytesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingBytesPerChild),
                MaxPendingBytesPerChild,
                $"The pending byte limit must be between 1 and {MaximumPendingBytesLimit}.");
        }

        if (ChildStopTimeout <= TimeSpan.Zero ||
            ChildStopTimeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ChildStopTimeout),
                ChildStopTimeout,
                "The child stop timeout must be positive and no greater than one minute.");
        }
    }
}
