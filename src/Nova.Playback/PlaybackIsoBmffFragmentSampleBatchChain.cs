using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleBatchChainResult
{
    Projected,
    InvalidInput,
    InvalidConfiguration,
    BatchLimitExceeded,
    BatchTransitionCountMismatch,
    TrackIdMismatch,
    MediaTimescaleMismatch,
    SequenceEndpointMismatch,
    DecodeEndpointMismatch,
}

public sealed record PlaybackIsoBmffFragmentSampleBatchChainOptions
{
    public const int DefaultMaximumBatchCount = 4_096;
    public const int MaximumBatchCountHardLimit = 65_536;

    public int MaximumBatchCount { get; init; } = DefaultMaximumBatchCount;
}

public sealed class PlaybackIsoBmffFragmentSampleBatchChain
{
    internal PlaybackIsoBmffFragmentSampleBatchChain(
        PlaybackIsoBmffFragmentContinuityChain continuity,
        uint? trackId,
        uint? mediaTimescale,
        ReadOnlyCollection<PlaybackIsoBmffFragmentSampleBatch> batches)
    {
        Continuity = continuity;
        TrackId = trackId;
        MediaTimescale = mediaTimescale;
        Batches = batches;
    }

    public PlaybackIsoBmffFragmentContinuityChain Continuity { get; }

    public uint? TrackId { get; }

    public uint? MediaTimescale { get; }

    public IReadOnlyList<PlaybackIsoBmffFragmentSampleBatch> Batches { get; }

    public long TotalSampleCount => Batches.Sum(
        batch => (long)batch.SampleCount);
}

public static class PlaybackIsoBmffFragmentSampleBatchChainProjector
{
    public static PlaybackIsoBmffFragmentSampleBatchChainResult Project(
        PlaybackIsoBmffFragmentContinuityChain? continuity,
        IReadOnlyList<PlaybackIsoBmffFragmentSampleBatch>? batches,
        out PlaybackIsoBmffFragmentSampleBatchChain? chain,
        PlaybackIsoBmffFragmentSampleBatchChainOptions? options = null)
    {
        chain = null;
        options ??= new PlaybackIsoBmffFragmentSampleBatchChainOptions();
        if (options.MaximumBatchCount is < 0 or
            > PlaybackIsoBmffFragmentSampleBatchChainOptions
                .MaximumBatchCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleBatchChainResult
                    .InvalidConfiguration;
        }

        if (continuity is null || batches is null)
        {
            return PlaybackIsoBmffFragmentSampleBatchChainResult.InvalidInput;
        }

        if (batches.Count > options.MaximumBatchCount)
        {
            return PlaybackIsoBmffFragmentSampleBatchChainResult.BatchLimitExceeded;
        }

        var expectedTransitionCount = batches.Count == 0
            ? 0
            : batches.Count - 1;
        if (continuity.Transitions.Count != expectedTransitionCount)
        {
            return
                PlaybackIsoBmffFragmentSampleBatchChainResult
                    .BatchTransitionCountMismatch;
        }

        var copied = new PlaybackIsoBmffFragmentSampleBatch[batches.Count];
        uint? trackId = null;
        uint? mediaTimescale = null;
        for (var index = 0; index < copied.Length; index++)
        {
            var batch = batches[index];
            if (batch is null)
            {
                return PlaybackIsoBmffFragmentSampleBatchChainResult.InvalidInput;
            }

            trackId ??= batch.TrackId;
            if (batch.TrackId != trackId ||
                (continuity.TrackId is { } continuityTrackId &&
                    batch.TrackId != continuityTrackId))
            {
                return
                    PlaybackIsoBmffFragmentSampleBatchChainResult
                        .TrackIdMismatch;
            }

            mediaTimescale ??= batch.MediaTimescale;
            if (batch.MediaTimescale != mediaTimescale)
            {
                return
                    PlaybackIsoBmffFragmentSampleBatchChainResult
                        .MediaTimescaleMismatch;
            }

            if (index > 0)
            {
                var transition = continuity.Transitions[index - 1];
                var previousBatch = copied[index - 1];
                if (transition.Sequence.Previous.SequenceNumber !=
                        previousBatch.SequenceNumber ||
                    transition.Sequence.Current.SequenceNumber !=
                        batch.SequenceNumber)
                {
                    return
                        PlaybackIsoBmffFragmentSampleBatchChainResult
                            .SequenceEndpointMismatch;
                }

                if (!SameTimedRun(
                        transition.Decode.Previous,
                        previousBatch.TimedRun) ||
                    !SameTimedRun(transition.Decode.Current, batch.TimedRun))
                {
                    return
                        PlaybackIsoBmffFragmentSampleBatchChainResult
                            .DecodeEndpointMismatch;
                }
            }

            copied[index] = batch;
        }

        chain = new(
            continuity,
            trackId,
            mediaTimescale,
            Array.AsReadOnly(copied));
        return PlaybackIsoBmffFragmentSampleBatchChainResult.Projected;
    }

    private static bool SameTimedRun(
        PlaybackIsoBmffTimedFragmentRun left,
        PlaybackIsoBmffTimedFragmentRun right)
    {
        if (left.TrackId != right.TrackId ||
            left.SampleDescriptionIndex != right.SampleDescriptionIndex ||
            left.BaseMediaDecodeTime != right.BaseMediaDecodeTime ||
            left.NextDecodeTime != right.NextDecodeTime ||
            left.Samples.Count != right.Samples.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Samples.Count; index++)
        {
            if (left.Samples[index] != right.Samples[index])
            {
                return false;
            }
        }

        return true;
    }
}
