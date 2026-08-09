using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentContinuityChainResult
{
    Projected,
    InvalidInput,
    InvalidConfiguration,
    TransitionLimitExceeded,
    TrackIdMismatch,
    SequenceChainMismatch,
    DecodeChainMismatch,
}

public sealed record PlaybackIsoBmffFragmentContinuityChainOptions
{
    public const int DefaultMaximumTransitionCount = 4_096;
    public const int MaximumTransitionCountHardLimit = 65_536;

    public int MaximumTransitionCount { get; init; } =
        DefaultMaximumTransitionCount;
}

public sealed class PlaybackIsoBmffFragmentContinuityChain
{
    internal PlaybackIsoBmffFragmentContinuityChain(
        uint? trackId,
        ReadOnlyCollection<PlaybackIsoBmffFragmentContinuityBinding> transitions)
    {
        TrackId = trackId;
        Transitions = transitions;
    }

    public uint? TrackId { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentContinuityBinding> Transitions
    { get; }

    public bool IsContinuous => Transitions.All(
        transition => transition.IsContinuous);
}

public static class PlaybackIsoBmffFragmentContinuityChainProjector
{
    public static PlaybackIsoBmffFragmentContinuityChainResult Project(
        IReadOnlyList<PlaybackIsoBmffFragmentContinuityBinding>? transitions,
        out PlaybackIsoBmffFragmentContinuityChain? chain,
        PlaybackIsoBmffFragmentContinuityChainOptions? options = null)
    {
        chain = null;
        options ??= new PlaybackIsoBmffFragmentContinuityChainOptions();
        if (options.MaximumTransitionCount is < 0 or
            > PlaybackIsoBmffFragmentContinuityChainOptions
                .MaximumTransitionCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentContinuityChainResult
                    .InvalidConfiguration;
        }

        if (transitions is null)
        {
            return PlaybackIsoBmffFragmentContinuityChainResult.InvalidInput;
        }

        if (transitions.Count > options.MaximumTransitionCount)
        {
            return
                PlaybackIsoBmffFragmentContinuityChainResult
                    .TransitionLimitExceeded;
        }

        var copied = new PlaybackIsoBmffFragmentContinuityBinding[
            transitions.Count];
        uint? trackId = null;
        for (var index = 0; index < copied.Length; index++)
        {
            var current = transitions[index];
            if (current is null)
            {
                return PlaybackIsoBmffFragmentContinuityChainResult.InvalidInput;
            }

            trackId ??= current.TrackId;
            if (current.TrackId != trackId)
            {
                return
                    PlaybackIsoBmffFragmentContinuityChainResult
                        .TrackIdMismatch;
            }

            if (index > 0)
            {
                var previous = copied[index - 1];
                if (previous.Sequence.Current.SequenceNumber !=
                    current.Sequence.Previous.SequenceNumber)
                {
                    return
                        PlaybackIsoBmffFragmentContinuityChainResult
                            .SequenceChainMismatch;
                }

                if (!SameRun(
                    previous.Decode.Current,
                    current.Decode.Previous))
                {
                    return
                        PlaybackIsoBmffFragmentContinuityChainResult
                            .DecodeChainMismatch;
                }
            }

            copied[index] = current;
        }

        chain = new(trackId, Array.AsReadOnly(copied));
        return PlaybackIsoBmffFragmentContinuityChainResult.Projected;
    }

    private static bool SameRun(
        PlaybackIsoBmffTimedFragmentRun left,
        PlaybackIsoBmffTimedFragmentRun right) =>
        left.TrackId == right.TrackId &&
        left.SampleDescriptionIndex == right.SampleDescriptionIndex &&
        left.BaseMediaDecodeTime == right.BaseMediaDecodeTime &&
        left.NextDecodeTime == right.NextDecodeTime &&
        left.Samples.Count == right.Samples.Count;
}
