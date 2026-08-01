namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditedSampleAdmissionLookupResult
{
    Found,
    InvalidConfiguration,
    InvalidReconciliation,
    SampleNumberOutOfRange,
    MissingEditList,
    MissingMovieTiming,
    MissingMediaTiming,
    NotPresented,
    UnsupportedMediaRate,
    NonIntegralMediaDuration,
    OverlapLimitExceeded,
    InvalidMovieOverlap,
    PartialSampleRejected,
}

public sealed class PlaybackIsoBmffEditedSampleAdmission
{
    internal PlaybackIsoBmffEditedSampleAdmission(
        ulong sampleNumber,
        PlaybackIsoBmffCompleteSampleEditAdmission admission)
    {
        SampleNumber = sampleNumber;
        Admission = admission;
    }

    public ulong SampleNumber { get; }

    public PlaybackIsoBmffCompleteSampleEditAdmission Admission { get; }

    public PlaybackIsoBmffSamplePresentationTime Sample => Admission.Sample;

    public IReadOnlyList<PlaybackIsoBmffSampleEditMovieOverlap> Appearances =>
        Admission.Appearances;
}

public static class PlaybackIsoBmffEditedSampleAdmissionLookup
{
    public static PlaybackIsoBmffEditedSampleAdmissionLookupResult Lookup(
        PlaybackIsoBmffTimeToSampleBox? timeToSample,
        PlaybackIsoBmffCompositionTimeToSampleBox? compositionTimeToSample,
        PlaybackIsoBmffReconciledSampleCount? reconciliation,
        PlaybackIsoBmffEditListBox? editList,
        PlaybackIsoBmffMovieHeaderTimingBox? movieTiming,
        PlaybackIsoBmffMediaHeaderTimingBox? mediaTiming,
        ulong sampleNumber,
        out PlaybackIsoBmffEditedSampleAdmission? admission,
        PlaybackIsoBmffSampleEditOverlapLookupOptions? options = null)
    {
        admission = null;
        options ??= new PlaybackIsoBmffSampleEditOverlapLookupOptions();
        if (options.MaximumOverlapCount is < 0 or
            > PlaybackIsoBmffSampleEditOverlapLookupOptions
                .MaximumOverlapCountHardLimit)
        {
            return PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidConfiguration;
        }

        var presentationResult =
            PlaybackIsoBmffSamplePresentationTimeLookup.Lookup(
                timeToSample,
                compositionTimeToSample,
                reconciliation,
                sampleNumber,
                out var presentationTime);
        if (presentationResult ==
            PlaybackIsoBmffSamplePresentationTimeLookupResult
                .SampleNumberOutOfRange)
        {
            return PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .SampleNumberOutOfRange;
        }

        if (presentationResult !=
                PlaybackIsoBmffSamplePresentationTimeLookupResult.Found ||
            presentationTime is null)
        {
            return PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidReconciliation;
        }

        var overlapResult = PlaybackIsoBmffSampleEditOverlapLookup.Lookup(
            presentationTime,
            editList,
            movieTiming,
            mediaTiming,
            out var mediaOverlaps,
            options);
        if (overlapResult != PlaybackIsoBmffSampleEditOverlapLookupResult.Found ||
            mediaOverlaps is null)
        {
            return MapOverlapResult(overlapResult);
        }

        var projectionResult =
            PlaybackIsoBmffSampleEditMovieOverlapProjector.TryProject(
                mediaOverlaps,
                editList,
                movieTiming,
                mediaTiming,
                out var movieOverlaps);
        if (projectionResult !=
                PlaybackIsoBmffSampleEditMovieOverlapProjectionResult
                    .Projected ||
            movieOverlaps is null)
        {
            return PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidMovieOverlap;
        }

        var admissionResult =
            PlaybackIsoBmffCompleteSampleEditAdmissionPolicy.Admit(
                movieOverlaps,
                out var completeAdmission);
        if (admissionResult ==
                PlaybackIsoBmffCompleteSampleEditAdmissionResult.Admitted &&
            completeAdmission is not null)
        {
            admission = new(sampleNumber, completeAdmission);
            return PlaybackIsoBmffEditedSampleAdmissionLookupResult.Found;
        }

        return admissionResult switch
        {
            PlaybackIsoBmffCompleteSampleEditAdmissionResult
                .PartialSampleRejected =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .PartialSampleRejected,
            _ => PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidMovieOverlap,
        };
    }

    private static PlaybackIsoBmffEditedSampleAdmissionLookupResult
        MapOverlapResult(PlaybackIsoBmffSampleEditOverlapLookupResult result) =>
        result switch
        {
            PlaybackIsoBmffSampleEditOverlapLookupResult.InvalidConfiguration =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .InvalidConfiguration,
            PlaybackIsoBmffSampleEditOverlapLookupResult.MissingEditList =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult.MissingEditList,
            PlaybackIsoBmffSampleEditOverlapLookupResult.MissingMovieTiming =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .MissingMovieTiming,
            PlaybackIsoBmffSampleEditOverlapLookupResult.MissingMediaTiming =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .MissingMediaTiming,
            PlaybackIsoBmffSampleEditOverlapLookupResult.NotPresented =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult.NotPresented,
            PlaybackIsoBmffSampleEditOverlapLookupResult
                .UnsupportedMediaRate =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .UnsupportedMediaRate,
            PlaybackIsoBmffSampleEditOverlapLookupResult
                .NonIntegralMediaDuration =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .NonIntegralMediaDuration,
            PlaybackIsoBmffSampleEditOverlapLookupResult.OverlapLimitExceeded =>
                PlaybackIsoBmffEditedSampleAdmissionLookupResult
                    .OverlapLimitExceeded,
            _ => PlaybackIsoBmffEditedSampleAdmissionLookupResult
                .InvalidMovieOverlap,
        };
}
