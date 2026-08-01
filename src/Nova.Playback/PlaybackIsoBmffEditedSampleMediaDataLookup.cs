namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditedSampleMediaDataLookupResult
{
    Found,
    InvalidConfiguration,
    MissingEditedRange,
    InvalidSampleRange,
    SampleOutsideFile,
    InvalidBoxSequence,
    BoxLimitExceeded,
    NotFound,
}

public sealed class PlaybackIsoBmffEditedSampleMediaData
{
    internal PlaybackIsoBmffEditedSampleMediaData(
        PlaybackIsoBmffEditedSampleByteRange editedRange,
        PlaybackIsoBmffBoxLocation mediaDataBox)
    {
        EditedRange = editedRange;
        MediaDataBox = mediaDataBox;
    }

    public PlaybackIsoBmffEditedSampleByteRange EditedRange { get; }

    public ulong SampleNumber => EditedRange.SampleNumber;

    public PlaybackIsoBmffEditedSampleAdmission Admission =>
        EditedRange.Admission;

    public PlaybackIsoBmffSampleByteRange Range => EditedRange.Range;

    public PlaybackIsoBmffBoxLocation MediaDataBox { get; }
}

public static class PlaybackIsoBmffEditedSampleMediaDataLookup
{
    public static PlaybackIsoBmffEditedSampleMediaDataLookupResult Lookup(
        ReadOnlySpan<byte> file,
        PlaybackIsoBmffEditedSampleByteRange? editedRange,
        out PlaybackIsoBmffEditedSampleMediaData? mediaData,
        PlaybackIsoBmffSampleMediaDataLookupOptions? options = null)
    {
        mediaData = null;
        options ??= new PlaybackIsoBmffSampleMediaDataLookupOptions();
        if (options.MaximumBoxCount is < 0 or
            > PlaybackIsoBmffSampleMediaDataLookupOptions
                .MaximumBoxCountHardLimit)
        {
            return PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .InvalidConfiguration;
        }

        if (editedRange is null)
        {
            return PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .MissingEditedRange;
        }

        var result = PlaybackIsoBmffSampleMediaDataLookup.Lookup(
            file,
            editedRange.Range,
            out var mediaDataBox,
            options);
        if (result != PlaybackIsoBmffSampleMediaDataLookupResult.Found ||
            mediaDataBox is null)
        {
            return result switch
            {
                PlaybackIsoBmffSampleMediaDataLookupResult
                    .InvalidConfiguration =>
                    PlaybackIsoBmffEditedSampleMediaDataLookupResult
                        .InvalidConfiguration,
                PlaybackIsoBmffSampleMediaDataLookupResult.InvalidSampleRange =>
                    PlaybackIsoBmffEditedSampleMediaDataLookupResult
                        .InvalidSampleRange,
                PlaybackIsoBmffSampleMediaDataLookupResult.SampleOutsideFile =>
                    PlaybackIsoBmffEditedSampleMediaDataLookupResult
                        .SampleOutsideFile,
                PlaybackIsoBmffSampleMediaDataLookupResult.InvalidBoxSequence =>
                    PlaybackIsoBmffEditedSampleMediaDataLookupResult
                        .InvalidBoxSequence,
                PlaybackIsoBmffSampleMediaDataLookupResult.BoxLimitExceeded =>
                    PlaybackIsoBmffEditedSampleMediaDataLookupResult
                        .BoxLimitExceeded,
                _ => PlaybackIsoBmffEditedSampleMediaDataLookupResult.NotFound,
            };
        }

        mediaData = new(editedRange, mediaDataBox);
        return PlaybackIsoBmffEditedSampleMediaDataLookupResult.Found;
    }
}
