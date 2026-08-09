namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentContinuityBindingResult
{
    Bound,
    InvalidInput,
}

public enum PlaybackIsoBmffFragmentContinuityKind
{
    Continuous,
    SequenceDiscontinuity,
    DecodeDiscontinuity,
    SequenceAndDecodeDiscontinuity,
}

public sealed class PlaybackIsoBmffFragmentContinuityBinding
{
    internal PlaybackIsoBmffFragmentContinuityBinding(
        PlaybackIsoBmffMovieFragmentSequenceContinuity sequence,
        PlaybackIsoBmffTrackFragmentDecodeContinuity decode,
        PlaybackIsoBmffFragmentContinuityKind kind)
    {
        Sequence = sequence;
        Decode = decode;
        Kind = kind;
    }

    public PlaybackIsoBmffMovieFragmentSequenceContinuity Sequence { get; }

    public PlaybackIsoBmffTrackFragmentDecodeContinuity Decode { get; }

    public uint TrackId => Decode.TrackId;

    public PlaybackIsoBmffFragmentContinuityKind Kind { get; }

    public bool IsContinuous =>
        Kind == PlaybackIsoBmffFragmentContinuityKind.Continuous;
}

public static class PlaybackIsoBmffFragmentContinuityBinder
{
    public static PlaybackIsoBmffFragmentContinuityBindingResult Bind(
        PlaybackIsoBmffMovieFragmentSequenceContinuity? sequence,
        PlaybackIsoBmffTrackFragmentDecodeContinuity? decode,
        out PlaybackIsoBmffFragmentContinuityBinding? binding)
    {
        binding = null;
        if (sequence is null || decode is null)
        {
            return PlaybackIsoBmffFragmentContinuityBindingResult.InvalidInput;
        }

        var kind = (sequence.IsContinuous, decode.IsContinuous) switch
        {
            (true, true) => PlaybackIsoBmffFragmentContinuityKind.Continuous,
            (false, true) =>
                PlaybackIsoBmffFragmentContinuityKind.SequenceDiscontinuity,
            (true, false) =>
                PlaybackIsoBmffFragmentContinuityKind.DecodeDiscontinuity,
            _ => PlaybackIsoBmffFragmentContinuityKind
                .SequenceAndDecodeDiscontinuity,
        };
        binding = new(sequence, decode, kind);
        return PlaybackIsoBmffFragmentContinuityBindingResult.Bound;
    }
}
