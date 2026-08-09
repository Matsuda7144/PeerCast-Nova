using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
{
    Exclude,
    Include,
}

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
{
    Classified,
    MissingIntentProjection,
    MissingDispositions,
    IntentLimitExceeded,
    DispositionCountMismatch,
    SourceMismatch,
    InvalidDisposition,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent intent,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
                disposition)
    {
        Intent = intent;
        Disposition = disposition;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntent Intent
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
        Disposition
    { get; }

    public bool IsIncluded =>
        Disposition ==
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
            .Include;
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
                source,
            ReadOnlyCollection<
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification>
                classifications,
            int includedCount)
    {
        Source = source;
        Classifications = classifications;
        IncludedCount = includedCount;
    }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection
            Source
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification>
        Classifications
    { get; }

    public int IncludedCount { get; }

    public int ExcludedCount => Classifications.Count - IncludedCount;
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassifier
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
            Classify(
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionIntentProjection?
                    source,
                IReadOnlyList<
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition>?
                    dispositions,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                    .MissingIntentProjection;
        }

        if (dispositions is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                    .MissingDispositions;
        }

        if (source.IntentCount >
            PlaybackIsoBmffFragmentSampleMergeTraversalOptions
                .MaximumStepCountHardLimit)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                    .IntentLimitExceeded;
        }

        if (dispositions.Count != source.IntentCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                    .DispositionCountMismatch;
        }

        var classifications =
            new PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassification[
                source.IntentCount];
        var includedCount = 0;
        for (var index = 0; index < classifications.Length; index++)
        {
            var intent = source.Intents[index];
            if (intent.SubmissionIndex != index ||
                !ReferenceEquals(intent.Entry, source.Source.Entries[index]))
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                        .SourceMismatch;
            }

            var disposition = dispositions[index];
            if (disposition is not
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
                        .Exclude and not
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
                        .Include)
            {
                return
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                        .InvalidDisposition;
            }

            if (disposition ==
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionDisposition
                    .Include)
            {
                includedCount++;
            }

            classifications[index] = new(intent, disposition);
        }

        projection = new(
            source,
            Array.AsReadOnly(classifications),
            includedCount);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionClassificationResult
                .Classified;
    }
}
