namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveEpochStateCreateResult
{
    Created,
    InvalidInput,
}

public enum PlaybackFmp4LiveEpochStateTransitionResult
{
    Advanced,
    InvalidInput,
    StateMismatch,
    EpochOverflow,
}

public sealed class PlaybackFmp4LiveEpochState
{
    internal PlaybackFmp4LiveEpochState(
        ulong epochId,
        PlaybackIsoBmffMovieFragmentHeaderBox fragmentHeader,
        PlaybackIsoBmffTimedFragmentRun timedRun,
        PlaybackIsoBmffFragmentContinuityKind? entryContinuity)
    {
        EpochId = epochId;
        FragmentHeader = fragmentHeader;
        TimedRun = timedRun;
        EntryContinuity = entryContinuity;
    }

    public ulong EpochId { get; }

    public PlaybackIsoBmffMovieFragmentHeaderBox FragmentHeader { get; }

    public PlaybackIsoBmffTimedFragmentRun TimedRun { get; }

    public PlaybackIsoBmffFragmentContinuityKind? EntryContinuity { get; }

    public uint SequenceNumber => FragmentHeader.SequenceNumber;

    public uint TrackId => TimedRun.TrackId;

    public bool BeganAfterDiscontinuity =>
        EntryContinuity is { } continuity &&
        continuity != PlaybackIsoBmffFragmentContinuityKind.Continuous;
}

public static class PlaybackFmp4LiveEpochStateMachine
{
    public static PlaybackFmp4LiveEpochStateCreateResult Create(
        PlaybackIsoBmffMovieFragmentHeaderBox? fragmentHeader,
        PlaybackIsoBmffTimedFragmentRun? timedRun,
        out PlaybackFmp4LiveEpochState? state)
    {
        state = null;
        if (fragmentHeader is null || timedRun is null)
        {
            return PlaybackFmp4LiveEpochStateCreateResult.InvalidInput;
        }

        state = new(0, fragmentHeader, timedRun, null);
        return PlaybackFmp4LiveEpochStateCreateResult.Created;
    }

    public static PlaybackFmp4LiveEpochStateTransitionResult Advance(
        PlaybackFmp4LiveEpochState? state,
        PlaybackIsoBmffFragmentContinuityBinding? continuity,
        out PlaybackFmp4LiveEpochState? next)
    {
        next = null;
        if (state is null || continuity is null)
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.InvalidInput;
        }

        if (state.SequenceNumber != continuity.Sequence.Previous.SequenceNumber ||
            !SameRun(state.TimedRun, continuity.Decode.Previous))
        {
            return PlaybackFmp4LiveEpochStateTransitionResult.StateMismatch;
        }

        var epochId = state.EpochId;
        if (!continuity.IsContinuous)
        {
            if (epochId == ulong.MaxValue)
            {
                return PlaybackFmp4LiveEpochStateTransitionResult.EpochOverflow;
            }

            epochId++;
        }

        next = new(
            epochId,
            continuity.Sequence.Current,
            continuity.Decode.Current,
            continuity.Kind);
        return PlaybackFmp4LiveEpochStateTransitionResult.Advanced;
    }

    private static bool SameRun(
        PlaybackIsoBmffTimedFragmentRun left,
        PlaybackIsoBmffTimedFragmentRun right) =>
        left.TrackId == right.TrackId &&
        left.SampleDescriptionIndex == right.SampleDescriptionIndex &&
        left.BaseMediaDecodeTime == right.BaseMediaDecodeTime &&
        left.NextDecodeTime == right.NextDecodeTime &&
        SamplesMatch(left.Samples, right.Samples);

    private static bool SamplesMatch(
        IReadOnlyList<PlaybackIsoBmffTimedFragmentSample> left,
        IReadOnlyList<PlaybackIsoBmffTimedFragmentSample> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }
}
