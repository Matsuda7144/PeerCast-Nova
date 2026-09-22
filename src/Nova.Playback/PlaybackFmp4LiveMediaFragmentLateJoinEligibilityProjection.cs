namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
{
    Projected,
    MissingFragmentIdentity,
    InvalidVideoTrackId,
    VideoTrackMissing,
    MissingVideoRun,
    VideoRunTrackIdMismatch,
    EmptyVideoRun,
    InvalidFirstVideoSampleFlags,
}

public enum PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind
{
    Eligible,
    NotEligible,
    Indeterminate,
}

public sealed class PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjection
{
    internal PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjection(
        PlaybackFmp4LiveMediaFragmentIdentityProjection source,
        PlaybackIsoBmffResolvedFragmentRun videoRun,
        PlaybackFmp4LiveRandomAccessSampleClassification firstVideoSample,
        PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind kind)
    {
        Source = source;
        VideoRun = videoRun;
        FirstVideoSample = firstVideoSample;
        Kind = kind;
    }

    public PlaybackFmp4LiveMediaFragmentIdentityProjection Source { get; }

    public PlaybackIsoBmffResolvedFragmentRun VideoRun { get; }

    public PlaybackFmp4LiveRandomAccessSampleClassification FirstVideoSample
    { get; }

    public uint SequenceNumber => Source.SequenceNumber;

    public uint VideoTrackId => VideoRun.TrackId;

    public PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind Kind { get; }

    public bool IsEligible =>
        Kind == PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.Eligible;
}

public static class PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjector
{
    public static
        PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult Project(
            PlaybackFmp4LiveMediaFragmentIdentityProjection? identity,
            uint videoTrackId,
            PlaybackIsoBmffResolvedFragmentRun? videoRun,
            out PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjection?
                projection)
    {
        projection = null;
        if (identity is null)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .MissingFragmentIdentity;
        }

        if (videoTrackId == 0)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .InvalidVideoTrackId;
        }

        var videoTrackFound = false;
        for (var index = 0; index < identity.Tracks.Count; index++)
        {
            if (identity.Tracks[index].TrackId == videoTrackId)
            {
                videoTrackFound = true;
                break;
            }
        }

        if (!videoTrackFound)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .VideoTrackMissing;
        }

        if (videoRun is null)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .MissingVideoRun;
        }

        if (videoRun.TrackId != videoTrackId)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .VideoRunTrackIdMismatch;
        }

        if (videoRun.Samples.Count == 0)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .EmptyVideoRun;
        }

        if (PlaybackFmp4LiveRandomAccessSampleClassifier.Classify(
                videoRun.Samples[0].SampleFlags,
                out var firstVideoSample) !=
            PlaybackFmp4LiveRandomAccessSampleClassificationResult.Classified)
        {
            return
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                    .InvalidFirstVideoSampleFlags;
        }

        var kind = firstVideoSample.Kind switch
        {
            PlaybackFmp4LiveRandomAccessSampleKind.IndependentlyDecodable =>
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.Eligible,
            PlaybackFmp4LiveRandomAccessSampleKind.NotIndependentlyDecodable =>
                PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.NotEligible,
            _ => PlaybackFmp4LiveMediaFragmentLateJoinEligibilityKind.Indeterminate,
        };

        projection = new(identity, videoRun, firstVideoSample, kind);
        return
            PlaybackFmp4LiveMediaFragmentLateJoinEligibilityProjectionResult
                .Projected;
    }
}
