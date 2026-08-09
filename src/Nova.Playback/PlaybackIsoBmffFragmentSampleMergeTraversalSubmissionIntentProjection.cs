using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
{
    Projected,
    InvalidInput,
    EntryLimitExceeded,
    SourceMismatch,
    ConsumerMismatch,
    BatchIdentityMismatch,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent(
        int submissionIndex,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry entry)
    {
        SubmissionIndex = submissionIndex;
        Entry = entry;
    }

    public int SubmissionIndex { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry Entry
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalRequest Request =>
        Entry.Binding.Request;

    public Guid ConsumerId => Entry.Binding.Consumer.ConsumerId;

    public Guid BatchId => Entry.BatchIdentity.BatchId;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent>
                intents)
    {
        Source = source;
        Intents = intents;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
        Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent> Intents
    { get; }

    public int IntentCount => Intents.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection?
                    source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                    .InvalidInput;
        }

        if (source.EntryCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                    .EntryLimitExceeded;
        }

        if (source.EntryCount != source.Source.BindingCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                    .SourceMismatch;
        }

        var intents =
            new PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent[
                source.EntryCount];
        for (var index = 0; index < intents.Length; index++)
        {
            var entry = source.Entries[index];
            if (entry.BatchEntryIndex != index ||
                !ReferenceEquals(entry.Binding, source.Source.Bindings[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                        .SourceMismatch;
            }

            if (!ReferenceEquals(
                    entry.Binding.Consumer,
                    source.Source.Consumer))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                        .ConsumerMismatch;
            }

            if (!ReferenceEquals(entry.BatchIdentity, source.BatchIdentity))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                        .BatchIdentityMismatch;
            }

            intents[index] = new(index, entry);
        }

        projection = new(source, Array.AsReadOnly(intents));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjectionResult
                .Projected;
    }
}
