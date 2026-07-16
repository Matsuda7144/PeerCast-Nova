namespace PeerCast.Nova.Playback;

public sealed record PlaybackMediaClassificationOptions
{
    public const int MinimumProbeBytes = 16;
    public const int MaximumProbeBytesLimit = 1024 * 1024;
    public const int MaximumDiscontinuitiesLimit = 64;

    public int MaxProbeBytes { get; init; } = 64 * 1024;

    public int MaxDiscontinuities { get; init; } = 4;

    public void Validate()
    {
        if (MaxProbeBytes is < MinimumProbeBytes or
            > MaximumProbeBytesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxProbeBytes),
                MaxProbeBytes,
                $"The probe byte limit must be between {MinimumProbeBytes} and {MaximumProbeBytesLimit}.");
        }

        if (MaxDiscontinuities is < 0 or
            > MaximumDiscontinuitiesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxDiscontinuities),
                MaxDiscontinuities,
                $"The discontinuity limit must be between 0 and {MaximumDiscontinuitiesLimit}.");
        }
    }
}
