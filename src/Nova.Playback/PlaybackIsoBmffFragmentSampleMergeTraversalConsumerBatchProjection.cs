using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
{
    Projected,
    MissingConsumerBindingProjection,
    MissingBatchIdentity,
    BindingLimitExceeded,
    SourceMismatch,
    ConsumerMismatch,
}

public sealed class PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry
{
    internal PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry(
        int batchEntryIndex,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding binding,
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
            batchIdentity)
    {
        BatchEntryIndex = batchEntryIndex;
        Binding = binding;
        BatchIdentity = batchIdentity;
    }

    public int BatchEntryIndex { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBinding Binding
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
        BatchIdentity
    { get; }
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
                batchIdentity,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry>
                entries)
    {
        Source = source;
        BatchIdentity = batchIdentity;
        Entries = entries;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection
            Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity
        BatchIdentity
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry> Entries
    { get; }

    public int EntryCount => Entries.Count;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBindingProjection?
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchIdentity?
                    batchIdentity,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                    .MissingConsumerBindingProjection;
        }

        if (batchIdentity is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                    .MissingBatchIdentity;
        }

        if (source.BindingCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                    .BindingLimitExceeded;
        }

        if (!ReferenceEquals(
                source.Source.Requests.Source,
                source.Source.Source.Selection))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                    .SourceMismatch;
        }

        var entries =
            new PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchEntry[
                source.BindingCount];
        for (var index = 0; index < entries.Length; index++)
        {
            var binding = source.Bindings[index];
            if (!ReferenceEquals(
                    binding.Request,
                    source.Source.Requests.Requests[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                        .SourceMismatch;
            }

            if (!ReferenceEquals(binding.Consumer, source.Consumer))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                        .ConsumerMismatch;
            }

            entries[index] = new(index, binding, batchIdentity);
        }

        projection = new(source, batchIdentity, Array.AsReadOnly(entries));
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjectionResult
                .Projected;
    }
}
