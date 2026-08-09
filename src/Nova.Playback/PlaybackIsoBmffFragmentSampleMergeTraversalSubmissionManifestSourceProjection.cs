namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult
{
    Projected,
    MissingBatchProjection,
    MissingManifestProjection,
    SourceMismatch,
}

public sealed class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection
{
    internal
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection(
            PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
                source,
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection
                manifest)
    {
        Source = source;
        Manifest = manifest;
    }

    public PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection
        Source
    { get; }

    public
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection
            Manifest
    { get; }
}

public static class
    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjector
{
    public static
        PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult
            Project(
                PlaybackIsoBmffFragmentSampleMergeTraversalConsumerBatchProjection?
                    source,
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestProjection?
                    manifest,
                out
                    PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult
                    .MissingBatchProjection;
        }

        if (manifest is null)
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult
                    .MissingManifestProjection;
        }

        var manifestBatch = manifest.Source.Source.Source.Source;
        if (!ReferenceEquals(manifestBatch, source))
        {
            return
                PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult
                    .SourceMismatch;
        }

        projection = new(source, manifest);
        return
            PlaybackIsoBmffFragmentSampleMergeTraversalSubmissionManifestSourceProjectionResult
                .Projected;
    }
}
