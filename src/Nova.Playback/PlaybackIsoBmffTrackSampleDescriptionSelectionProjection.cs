using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
{
    Projected,
    MissingCandidateProjection,
    MissingSampleDescription,
    MissingHandler,
    EntryLimitExceeded,
    SourceMismatch,
    SampleDescriptionIndexOutOfRange,
    UnsupportedSampleEntry,
    HandlerMismatch,
}

public sealed class PlaybackIsoBmffTrackSampleDescriptionSelection
{
    internal PlaybackIsoBmffTrackSampleDescriptionSelection(
        PlaybackIsoBmffTrackEditedSamplePacketCandidate source,
        PlaybackIsoBmffEditedSampleDescriptionSelection selection)
    {
        Source = source;
        Selection = selection;
    }

    public PlaybackIsoBmffTrackEditedSamplePacketCandidate Source { get; }

    public PlaybackIsoBmffEditedSampleDescriptionSelection Selection { get; }

    public ulong SampleNumber => Source.SampleNumber;

    public uint SampleDescriptionIndex =>
        Selection.SampleDescriptionIndex;

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        Selection.CodecCandidate;

    public PlaybackIsoBmffHandlerKind MediaKind => Selection.MediaKind;
}

public sealed class PlaybackIsoBmffTrackSampleDescriptionSelectionProjection
{
    internal PlaybackIsoBmffTrackSampleDescriptionSelectionProjection(
        PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection source,
        PlaybackIsoBmffSampleDescriptionBox sampleDescription,
        PlaybackIsoBmffHandlerReferenceBox handler,
        ReadOnlyCollection<PlaybackIsoBmffTrackSampleDescriptionSelection>
            entries)
    {
        Source = source;
        SampleDescription = sampleDescription;
        Handler = handler;
        Entries = entries;
    }

    public PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection Source
    { get; }

    public PlaybackIsoBmffSampleDescriptionBox SampleDescription { get; }

    public PlaybackIsoBmffHandlerReferenceBox Handler { get; }

    public IReadOnlyList<PlaybackIsoBmffTrackSampleDescriptionSelection> Entries
    { get; }
}

public static class PlaybackIsoBmffTrackSampleDescriptionSelectionProjector
{
    public static
        PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult Project(
            PlaybackIsoBmffTrackEditedSamplePacketCandidateProjection? source,
            PlaybackIsoBmffSampleDescriptionBox? sampleDescription,
            PlaybackIsoBmffHandlerReferenceBox? handler,
            out PlaybackIsoBmffTrackSampleDescriptionSelectionProjection?
                projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                    .MissingCandidateProjection;
        }

        if (sampleDescription is null)
        {
            return
                PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                    .MissingSampleDescription;
        }

        if (handler is null)
        {
            return
                PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                    .MissingHandler;
        }

        if (source.Entries.Count >
            PlaybackIsoBmffTrackSampleSequencePlanFactory
                .MaximumPlannedSampleCount)
        {
            return
                PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                    .EntryLimitExceeded;
        }

        var entries = new PlaybackIsoBmffTrackSampleDescriptionSelection[
            source.Entries.Count];
        for (var index = 0; index < source.Entries.Count; index++)
        {
            var entry = source.Entries[index];
            if (!ReferenceEquals(entry.Source, source.Source.Entries[index]) ||
                !ReferenceEquals(
                    entry.Candidate.OwnedPayload,
                    entry.Source.OwnedPayload))
            {
                return
                    PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                        .SourceMismatch;
            }

            var result = PlaybackIsoBmffEditedSampleDescriptionSelector.Select(
                entry.Source.Source.Source.EditedRange,
                sampleDescription,
                handler,
                out var selection);
            if (result !=
                    PlaybackIsoBmffEditedSampleDescriptionSelectionResult.Selected ||
                selection is null)
            {
                return result switch
                {
                    PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                        .SampleDescriptionIndexOutOfRange =>
                        PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                            .SampleDescriptionIndexOutOfRange,
                    PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                        .UnsupportedSampleEntry =>
                        PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                            .UnsupportedSampleEntry,
                    PlaybackIsoBmffEditedSampleDescriptionSelectionResult
                        .HandlerMismatch =>
                        PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                            .HandlerMismatch,
                    _ =>
                        PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
                            .SourceMismatch,
                };
            }

            entries[index] = new(entry, selection);
        }

        projection = new(
            source,
            sampleDescription,
            handler,
            Array.AsReadOnly(entries));
        return PlaybackIsoBmffTrackSampleDescriptionSelectionProjectionResult
            .Projected;
    }
}
