using System.Numerics;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffTrackFragmentDecodeContinuityResult
{
    Classified,
    InvalidInput,
    TrackIdMismatch,
    InvalidRunTiming,
}

public enum PlaybackIsoBmffTrackFragmentDecodeContinuityKind
{
    Continuous,
    Gap,
    Overlap,
}

public sealed class PlaybackIsoBmffTrackFragmentDecodeContinuity
{
    internal PlaybackIsoBmffTrackFragmentDecodeContinuity(
        PlaybackIsoBmffTimedFragmentRun previous,
        PlaybackIsoBmffTimedFragmentRun current,
        BigInteger decodeDelta,
        PlaybackIsoBmffTrackFragmentDecodeContinuityKind kind)
    {
        Previous = previous;
        Current = current;
        DecodeDelta = decodeDelta;
        Kind = kind;
    }

    public PlaybackIsoBmffTimedFragmentRun Previous { get; }

    public PlaybackIsoBmffTimedFragmentRun Current { get; }

    public uint TrackId => Previous.TrackId;

    public BigInteger ExpectedDecodeTime => Previous.NextDecodeTime;

    public BigInteger ActualDecodeTime => Current.BaseMediaDecodeTime;

    public BigInteger DecodeDelta { get; }

    public PlaybackIsoBmffTrackFragmentDecodeContinuityKind Kind { get; }

    public bool IsContinuous =>
        Kind == PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Continuous;
}

public static class PlaybackIsoBmffTrackFragmentDecodeContinuityClassifier
{
    public static PlaybackIsoBmffTrackFragmentDecodeContinuityResult Classify(
        PlaybackIsoBmffTimedFragmentRun? previous,
        PlaybackIsoBmffTimedFragmentRun? current,
        out PlaybackIsoBmffTrackFragmentDecodeContinuity? continuity)
    {
        continuity = null;
        if (previous is null || current is null)
        {
            return PlaybackIsoBmffTrackFragmentDecodeContinuityResult.InvalidInput;
        }

        if (previous.TrackId != current.TrackId)
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeContinuityResult
                    .TrackIdMismatch;
        }

        if (previous.BaseMediaDecodeTime > previous.NextDecodeTime ||
            current.BaseMediaDecodeTime > current.NextDecodeTime)
        {
            return
                PlaybackIsoBmffTrackFragmentDecodeContinuityResult
                    .InvalidRunTiming;
        }

        var delta = current.BaseMediaDecodeTime - previous.NextDecodeTime;
        var kind = delta.Sign switch
        {
            > 0 => PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Gap,
            < 0 => PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Overlap,
            _ => PlaybackIsoBmffTrackFragmentDecodeContinuityKind.Continuous,
        };
        continuity = new(previous, current, delta, kind);
        return PlaybackIsoBmffTrackFragmentDecodeContinuityResult.Classified;
    }
}
