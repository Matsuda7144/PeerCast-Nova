namespace PeerCast.Nova.Playback;

public sealed record PlaybackDecoderRunnerOptions
{
    public static readonly TimeSpan MinimumStopTimeout =
        TimeSpan.FromMilliseconds(10);

    public static readonly TimeSpan MaximumStopTimeout =
        TimeSpan.FromSeconds(30);

    public int MaxBootstrapBytes { get; init; } = 64 * 1024;

    public int MaxDiscontinuities { get; init; } = 4;

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public void Validate()
    {
        if (MaxBootstrapBytes is <
                PlaybackMediaClassificationOptions.MinimumProbeBytes or
            > PlaybackMediaClassificationOptions.MaximumProbeBytesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxBootstrapBytes),
                MaxBootstrapBytes,
                $"The bootstrap byte limit must be between {PlaybackMediaClassificationOptions.MinimumProbeBytes} and {PlaybackMediaClassificationOptions.MaximumProbeBytesLimit}.");
        }

        if (MaxDiscontinuities is < 0 or
            > PlaybackMediaClassificationOptions.MaximumDiscontinuitiesLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxDiscontinuities),
                MaxDiscontinuities,
                $"The discontinuity limit must be between 0 and {PlaybackMediaClassificationOptions.MaximumDiscontinuitiesLimit}.");
        }

        if (StopTimeout < MinimumStopTimeout ||
            StopTimeout > MaximumStopTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StopTimeout),
                StopTimeout,
                $"The stop timeout must be between {MinimumStopTimeout} and {MaximumStopTimeout}.");
        }
    }

    internal PlaybackMediaClassificationOptions CreateClassificationOptions()
    {
        return new PlaybackMediaClassificationOptions
        {
            MaxProbeBytes = MaxBootstrapBytes,
            MaxDiscontinuities = MaxDiscontinuities,
        };
    }
}
