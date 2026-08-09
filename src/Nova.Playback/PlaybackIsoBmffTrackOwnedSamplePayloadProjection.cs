using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
{
    Projected,
    MissingMediaDataProjection,
    EntryLimitExceeded,
    SourceMismatch,
    InvalidConfiguration,
    SampleOutsideFile,
    MediaDataLocationMismatch,
    EmptyPayload,
    PayloadLimitExceeded,
}

public sealed class PlaybackIsoBmffTrackOwnedSamplePayload
{
    internal PlaybackIsoBmffTrackOwnedSamplePayload(
        PlaybackIsoBmffTrackAdmittedSampleMediaData source,
        PlaybackIsoBmffOwnedEditedSamplePayload ownedPayload)
    {
        Source = source;
        OwnedPayload = ownedPayload;
    }

    public PlaybackIsoBmffTrackAdmittedSampleMediaData Source { get; }

    public PlaybackIsoBmffOwnedEditedSamplePayload OwnedPayload { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public ReadOnlyMemory<byte> Payload => OwnedPayload.Payload;
}

public sealed class PlaybackIsoBmffTrackOwnedSamplePayloadProjection
{
    internal PlaybackIsoBmffTrackOwnedSamplePayloadProjection(
        PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection source,
        int fileLength,
        int maximumPayloadBytes,
        ulong totalPayloadBytes,
        ReadOnlyCollection<PlaybackIsoBmffTrackOwnedSamplePayload> entries)
    {
        Source = source;
        FileLength = fileLength;
        MaximumPayloadBytes = maximumPayloadBytes;
        TotalPayloadBytes = totalPayloadBytes;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection Source
    { get; }

    public int FileLength { get; }

    public int MaximumPayloadBytes { get; }

    public ulong TotalPayloadBytes { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackOwnedSamplePayload> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackOwnedSamplePayloadProjector
{
    public static PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
        Project(
            ReadOnlySpan<byte> file,
            PlaybackIsoBmffTrackAdmittedSampleMediaDataProjection? source,
            out PlaybackIsoBmffTrackOwnedSamplePayloadProjection? projection,
            PlaybackIsoBmffOwnedEditedSamplePayloadOptions? options = null)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                .MissingMediaDataProjection;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                .EntryLimitExceeded;
        }

        options ??= new PlaybackIsoBmffOwnedEditedSamplePayloadOptions();
        ulong totalPayloadBytes = 0;
        var entries = new PlaybackIsoBmffTrackOwnedSamplePayload[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(
                    entry.Source,
                    source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.MediaData.EditedRange,
                    entry.Source.EditedRange))
            {
                return PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                    .SourceMismatch;
            }

            var result = PlaybackIsoBmffOwnedEditedSamplePayloadCopy.Copy(
                file,
                entry.MediaData,
                out var ownedPayload,
                options);
            if (result != PlaybackIsoBmffOwnedEditedSamplePayloadResult.Copied ||
                ownedPayload is null)
            {
                return MapFailure(result);
            }

            totalPayloadBytes += (ulong)ownedPayload.Payload.Length;
            entries[index] = new(entry, ownedPayload);
        }

        projection = new(
            source,
            file.Length,
            options.MaximumPayloadBytes,
            totalPayloadBytes,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
            .Projected;
    }

    private static PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
        MapFailure(PlaybackIsoBmffOwnedEditedSamplePayloadResult result) =>
        result switch
        {
            PlaybackIsoBmffOwnedEditedSamplePayloadResult
                .InvalidConfiguration =>
                PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                    .InvalidConfiguration,
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.SampleOutsideFile =>
                PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                    .SampleOutsideFile,
            PlaybackIsoBmffOwnedEditedSamplePayloadResult
                .MediaDataLocationMismatch =>
                PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                    .MediaDataLocationMismatch,
            PlaybackIsoBmffOwnedEditedSamplePayloadResult.EmptyPayload =>
                PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                    .EmptyPayload,
            _ => PlaybackIsoBmffTrackOwnedSamplePayloadProjectionResult
                .PayloadLimitExceeded,
        };
}
