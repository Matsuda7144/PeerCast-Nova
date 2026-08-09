using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
{
    Projected,
    MissingSequenceProjections,
    EmptySequenceSet,
    SequenceLimitExceeded,
    MissingSequenceProjection,
    SourceMismatch,
    MovieTimingMismatch,
    DuplicateTrackId,
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection(
        ReadOnlyCollection<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection> sources,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSet sequenceSet)
    {
        Sources = sources;
        SequenceSet = sequenceSet;
    }

    public IReadOnlyList<PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection>
        Sources
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSet SequenceSet
    { get; }
}

public static class PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult Project(
            IReadOnlyList<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection>?
                    sources,
            out PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection?
                projection)
    {
        projection = null;
        if (sources is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                    .MissingSequenceProjections;
        }

        if (sources.Count == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                    .EmptySequenceSet;
        }

        if (sources.Count >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory
                .MaximumSequenceCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                    .SequenceLimitExceeded;
        }

        var ownedSources = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection[
                sources.Count];
        var sequences = new PlaybackIsoBmffTrackCodecPacketSequence[
            sources.Count];
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            if (source is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                        .MissingSequenceProjection;
            }

            if (!ReferenceEquals(
                    source.Sequence.MovieTiming,
                    source.Source.MovieTiming) ||
                source.Sequence.Entries.Count != source.Source.Entries.Count)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                        .SourceMismatch;
            }

            ownedSources[index] = source;
            sequences[index] = source.Sequence;
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory.TryCreate(
                sequences,
                out var sequenceSet);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .Created ||
            sequenceSet is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .MovieTimingMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                        .MovieTimingMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .DuplicateTrackId =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                        .DuplicateTrackId,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .SequenceLimitExceeded =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                        .SequenceLimitExceeded,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
                        .SourceMismatch,
            };
        }

        projection = new(
            Array.AsReadOnly(ownedSources),
            sequenceSet);
        return PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjectionResult
            .Projected;
    }
}
