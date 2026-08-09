using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
{
    Created,
    MissingSequences,
    EmptySequenceSet,
    SequenceLimitExceeded,
    MissingSequence,
    MovieTimingMismatch,
    DuplicateTrackId,
}

public sealed class PlaybackIsoBmffIndexedTrackCodecPacketSequence
{
    internal PlaybackIsoBmffIndexedTrackCodecPacketSequence(
        int streamIndex,
        PlaybackIsoBmffTrackCodecPacketSequence sequence)
    {
        StreamIndex = streamIndex;
        Sequence = sequence;
    }

    public int StreamIndex { get; }

    public PlaybackIsoBmffTrackCodecPacketSequence Sequence { get; }

    public uint TrackId => Sequence.TrackId;

    public PlaybackIsoBmffHandlerKind MediaKind => Sequence.MediaKind;
}

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceSet
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceSet(
        PlaybackIsoBmffMovieHeaderTimingBox movieTiming,
        ReadOnlyCollection<PlaybackIsoBmffIndexedTrackCodecPacketSequence>
            streams)
    {
        MovieTiming = movieTiming;
        Streams = streams;
    }

    public PlaybackIsoBmffMovieHeaderTimingBox MovieTiming { get; }

    public IReadOnlyList<PlaybackIsoBmffIndexedTrackCodecPacketSequence> Streams
    { get; }
}

public static class PlaybackIsoBmffMovieTrackCodecPacketSequenceSetFactory
{
    public const int MaximumSequenceCount =
        PlaybackIsoBmffMovieCodecPacketIndexer.MaximumPacketCount;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult TryCreate(
            IReadOnlyList<PlaybackIsoBmffTrackCodecPacketSequence>? sequences,
            out PlaybackIsoBmffMovieTrackCodecPacketSequenceSet? sequenceSet)
    {
        sequenceSet = null;
        if (sequences is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .MissingSequences;
        }

        if (sequences.Count == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .EmptySequenceSet;
        }

        if (sequences.Count > MaximumSequenceCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .SequenceLimitExceeded;
        }

        var first = sequences[0];
        if (first is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                    .MissingSequence;
        }

        var movieTiming = first.MovieTiming;
        var trackIds = new HashSet<uint>();
        var streams = new PlaybackIsoBmffIndexedTrackCodecPacketSequence[
            sequences.Count];
        for (var index = 0; index < sequences.Count; index++)
        {
            var sequence = sequences[index];
            if (sequence is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                        .MissingSequence;
            }

            if (!ReferenceEquals(sequence.MovieTiming, movieTiming))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                        .MovieTimingMismatch;
            }

            if (!trackIds.Add(sequence.TrackId))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
                        .DuplicateTrackId;
            }

            streams[index] = new(index, sequence);
        }

        sequenceSet = new(movieTiming, Array.AsReadOnly(streams));
        return PlaybackIsoBmffMovieTrackCodecPacketSequenceSetCreationResult
            .Created;
    }
}
