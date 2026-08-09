using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
{
    Projected,
    MissingByteRangeProjection,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidConfiguration,
    InvalidSampleRange,
    SampleOutsideFile,
    InvalidBoxSequence,
    BoxLimitExceeded,
    MediaDataNotFound,
}

public sealed class PlaybackIsoBmffTrackAdmittedSampleMediaData
{
    internal PlaybackIsoBmffTrackAdmittedSampleMediaData(
        PlaybackIsoBmffTrackAdmittedSampleByteRange source,
        PlaybackIsoBmffEditedSampleMediaData mediaData)
    {
        Source = source;
        MediaData = mediaData;
    }

    public PlaybackIsoBmffTrackAdmittedSampleByteRange Source { get; }

    public PlaybackIsoBmffEditedSampleMediaData MediaData { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public PlaybackIsoBmffSampleByteRange Range => Source.Range;

    public PlaybackIsoBmffBoxLocation MediaDataBox => MediaData.MediaDataBox;
}

public sealed class PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection
{
    internal PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection(
        PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection source,
        int fileLength,
        int maximumBoxCount,
        ReadOnlyCollection<PlaybackIsoBmffTrackAdmittedSampleMediaData> entries)
    {
        Source = source;
        FileLength = fileLength;
        MaximumBoxCount = maximumBoxCount;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection Source
    { get; }

    public int FileLength { get; }

    public int MaximumBoxCount { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackAdmittedSampleMediaData> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackAdmittedSampleMediaDataProjector
{
    public static PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
        Project(
            ReadOnlySpan<byte> file,
            PlaybackIsoBmffTrackAdmittedSampleByteRangeProjection? source,
            out PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection?
                projection,
            PlaybackIsoBmffSampleMediaDataLookupOptions? options = null)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .MissingByteRangeProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .EntryLimitExceeded;
        }

        options ??= new PlaybackIsoBmffSampleMediaDataLookupOptions();
        var entries = new PlaybackIsoBmffTrackAdmittedSampleMediaData[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(
                    entry.Source,
                    source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.EditedRange.Admission,
                    entry.Source.Admission))
            {
                return
                    PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                        .SourceMismatch;
            }

            var result = PlaybackIsoBmffEditedSampleMediaDataLookup.Lookup(
                file,
                entry.EditedRange,
                out var mediaData,
                options);
            if (result != PlaybackIsoBmffEditedSampleMediaDataLookupResult.Found ||
                mediaData is null)
            {
                return MapFailure(result);
            }

            entries[index] = new(entry, mediaData);
        }

        projection = new(
            source,
            file.Length,
            options.MaximumBoxCount,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
            .Projected;
    }

    private static PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
        MapFailure(PlaybackIsoBmffEditedSampleMediaDataLookupResult result) =>
        result switch
        {
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .InvalidConfiguration =>
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .InvalidSampleRange =>
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .InvalidSampleRange,
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .SampleOutsideFile =>
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .SampleOutsideFile,
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .InvalidBoxSequence =>
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .InvalidBoxSequence,
            PlaybackIsoBmffEditedSampleMediaDataLookupResult
                .BoxLimitExceeded =>
                PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                    .BoxLimitExceeded,
            _ => PlaybackIsoBmffTrackAdmittedSampleMediaDataProjectionResult
                .MediaDataNotFound,
        };
}
