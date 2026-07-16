namespace PeerCast.Nova.Playback;

public sealed record PlaybackLatencyBufferOptions
{
    public const int MaximumPendingPacketsLimit = 4096;
    public const long MaximumPendingBytesLimit = 1024L * 1024 * 1024;

    public int MaxPendingPackets { get; init; } = 64;

    public long MaxPendingBytes { get; init; } = 8L * 1024 * 1024;

    public void Validate()
    {
        if (MaxPendingPackets is < 1 or > MaximumPendingPacketsLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingPackets),
                MaxPendingPackets,
                $"The pending packet limit must be between 1 and {MaximumPendingPacketsLimit}.");
        }

        if (MaxPendingBytes is < 1 or > MaximumPendingBytesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPendingBytes),
                MaxPendingBytes,
                $"The pending byte limit must be between 1 and {MaximumPendingBytesLimit}.");
        }
    }
}
