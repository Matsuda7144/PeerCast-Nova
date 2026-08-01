namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleMediaDataLookupResult
{
    Found,
    InvalidConfiguration,
    InvalidSampleRange,
    SampleOutsideFile,
    InvalidBoxSequence,
    BoxLimitExceeded,
    NotFound,
}

public sealed record PlaybackIsoBmffSampleMediaDataLookupOptions
{
    public const int DefaultMaximumBoxCount = 4_096;
    public const int MaximumBoxCountHardLimit = 65_536;

    public int MaximumBoxCount { get; init; } = DefaultMaximumBoxCount;
}

public static class PlaybackIsoBmffSampleMediaDataLookup
{
    public static PlaybackIsoBmffSampleMediaDataLookupResult Lookup(
        ReadOnlySpan<byte> file,
        PlaybackIsoBmffSampleByteRange? range,
        out PlaybackIsoBmffBoxLocation? mediaDataBox,
        PlaybackIsoBmffSampleMediaDataLookupOptions? options = null)
    {
        mediaDataBox = null;
        options ??= new PlaybackIsoBmffSampleMediaDataLookupOptions();
        if (options.MaximumBoxCount is < 0 or
            > PlaybackIsoBmffSampleMediaDataLookupOptions
                .MaximumBoxCountHardLimit)
        {
            return PlaybackIsoBmffSampleMediaDataLookupResult
                .InvalidConfiguration;
        }

        var fileBoundsResult =
            PlaybackIsoBmffSampleFileBoundsValidator.Validate(
                range,
                (ulong)file.Length);
        if (fileBoundsResult ==
            PlaybackIsoBmffSampleFileBoundsValidationResult.InvalidRange)
        {
            return PlaybackIsoBmffSampleMediaDataLookupResult
                .InvalidSampleRange;
        }

        if (fileBoundsResult ==
            PlaybackIsoBmffSampleFileBoundsValidationResult.OutsideFile)
        {
            return PlaybackIsoBmffSampleMediaDataLookupResult.SampleOutsideFile;
        }

        var offset = 0;
        var boxCount = 0;
        while (offset != file.Length)
        {
            if (boxCount >= options.MaximumBoxCount)
            {
                return PlaybackIsoBmffSampleMediaDataLookupResult
                    .BoxLimitExceeded;
            }

            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    file,
                    offset,
                    out var box) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                box is null)
            {
                return PlaybackIsoBmffSampleMediaDataLookupResult
                    .InvalidBoxSequence;
            }

            boxCount++;
            if (PlaybackIsoBmffSampleMediaDataBoundsValidator.Validate(
                    range,
                    box,
                    containerOffset: 0) ==
                PlaybackIsoBmffSampleMediaDataBoundsValidationResult.Contained)
            {
                mediaDataBox = box;
                return PlaybackIsoBmffSampleMediaDataLookupResult.Found;
            }

            offset = box.NextOffset;
        }

        return PlaybackIsoBmffSampleMediaDataLookupResult.NotFound;
    }
}
