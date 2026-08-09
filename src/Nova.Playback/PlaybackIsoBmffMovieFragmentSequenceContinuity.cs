using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieFragmentSequenceContinuityResult
{
    Classified,
    InvalidInput,
}

public enum PlaybackIsoBmffMovieFragmentSequenceContinuityKind
{
    Sequential,
    WrappedSequential,
    Duplicate,
    Discontinuous,
}

public sealed class PlaybackIsoBmffMovieFragmentSequenceContinuity
{
    internal PlaybackIsoBmffMovieFragmentSequenceContinuity(
        PlaybackIsoBmffMovieFragmentHeaderBox previous,
        PlaybackIsoBmffMovieFragmentHeaderBox current,
        uint expectedSequenceNumber,
        BigInteger rawSequenceDelta,
        PlaybackIsoBmffMovieFragmentSequenceContinuityKind kind)
    {
        Previous = previous;
        Current = current;
        ExpectedSequenceNumber = expectedSequenceNumber;
        RawSequenceDelta = rawSequenceDelta;
        Kind = kind;
    }

    public PlaybackIsoBmffMovieFragmentHeaderBox Previous { get; }

    public PlaybackIsoBmffMovieFragmentHeaderBox Current { get; }

    public uint ExpectedSequenceNumber { get; }

    public BigInteger RawSequenceDelta { get; }

    public PlaybackIsoBmffMovieFragmentSequenceContinuityKind Kind { get; }

    public bool IsContinuous => Kind is
        PlaybackIsoBmffMovieFragmentSequenceContinuityKind.Sequential or
        PlaybackIsoBmffMovieFragmentSequenceContinuityKind.WrappedSequential;
}

public static class PlaybackIsoBmffMovieFragmentSequenceContinuityClassifier
{
    public static PlaybackIsoBmffMovieFragmentSequenceContinuityResult Classify(
        PlaybackIsoBmffMovieFragmentHeaderBox? previous,
        PlaybackIsoBmffMovieFragmentHeaderBox? current,
        out PlaybackIsoBmffMovieFragmentSequenceContinuity? continuity)
    {
        continuity = null;
        if (previous is null || current is null)
        {
            return PlaybackIsoBmffMovieFragmentSequenceContinuityResult.InvalidInput;
        }

        var expected = unchecked(previous.SequenceNumber + 1);
        var kind = current.SequenceNumber == previous.SequenceNumber
            ? PlaybackIsoBmffMovieFragmentSequenceContinuityKind.Duplicate
            : current.SequenceNumber == expected
                ? previous.SequenceNumber == uint.MaxValue
                    ? PlaybackIsoBmffMovieFragmentSequenceContinuityKind
                        .WrappedSequential
                    : PlaybackIsoBmffMovieFragmentSequenceContinuityKind
                        .Sequential
                : PlaybackIsoBmffMovieFragmentSequenceContinuityKind
                    .Discontinuous;
        continuity = new(
            previous,
            current,
            expected,
            (BigInteger)current.SequenceNumber - previous.SequenceNumber,
            kind);
        return PlaybackIsoBmffMovieFragmentSequenceContinuityResult.Classified;
    }
}
