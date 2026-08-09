namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
{
    Projected,
    InvalidInput,
    CompletionMismatch,
    InvalidStepChain,
    ViewMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalCompletion source,
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView view)
    {
        Source = source;
        View = view;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalCompletion Source
    { get; }

    public PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementView View
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalCompletion? source,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                    .InvalidInput;
        }

        var result =
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewProjector
                .Project(source, out var view);
        if (result !=
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                    .Projected ||
            view is null)
        {
            return result switch
            {
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                    .CompletionMismatch =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                        .CompletionMismatch,
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewResult
                    .InvalidStepChain =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                        .InvalidStepChain,
                _ =>
                    PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                        .InvalidInput,
            };
        }

        if (!ReferenceEquals(view.Completion, source) ||
            view.AcknowledgementCount != source.TraversedSampleCount)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                    .ViewMismatch;
        }

        projection = new(source, view);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalAcknowledgementViewSourceProjectionResult
                .Projected;
    }
}
