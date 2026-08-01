using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMediaTimeConversionResult
{
    Converted,
    InvalidConfiguration,
    MissingMediaTiming,
    MagnitudeExceeded,
}

public sealed record PlaybackIsoBmffMediaTimeConversionOptions
{
    public static readonly TimeSpan DefaultMaximumMagnitude =
        TimeSpan.FromDays(7);

    public static readonly TimeSpan MaximumMagnitudeHardLimit =
        TimeSpan.FromDays(3_650);

    public TimeSpan MaximumMagnitude { get; init; } = DefaultMaximumMagnitude;
}

public static class PlaybackIsoBmffMediaTimeConverter
{
    public static PlaybackIsoBmffMediaTimeConversionResult TryConvert(
        Int128 timeUnits,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        out TimeSpan? time,
        PlaybackIsoBmffMediaTimeConversionOptions? options = null)
    {
        time = null;
        options ??= new PlaybackIsoBmffMediaTimeConversionOptions();
        if (options.MaximumMagnitude <= TimeSpan.Zero ||
            options.MaximumMagnitude >
                PlaybackIsoBmffMediaTimeConversionOptions
                    .MaximumMagnitudeHardLimit)
        {
            return PlaybackIsoBmffMediaTimeConversionResult
                .InvalidConfiguration;
        }

        if (mediaTiming is null)
        {
            return PlaybackIsoBmffMediaTimeConversionResult.MissingMediaTiming;
        }

        var scaledTicks = BigInteger.CreateChecked(timeUnits) *
            TimeSpan.TicksPerSecond;
        var convertedTicks = scaledTicks / mediaTiming.Timescale;
        if (convertedTicks < -options.MaximumMagnitude.Ticks ||
            convertedTicks > options.MaximumMagnitude.Ticks)
        {
            return PlaybackIsoBmffMediaTimeConversionResult.MagnitudeExceeded;
        }

        time = TimeSpan.FromTicks((long)convertedTicks);
        return PlaybackIsoBmffMediaTimeConversionResult.Converted;
    }
}
