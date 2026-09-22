namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMediaFragmentProfileBindingResult
{
    Bound,
    MissingInitializationProfile,
    MissingFragmentIdentity,
    MissingDecodeTimeline,
    DecodeTimelineIdentityMismatch,
    UnknownTrackId,
}

public sealed class PlaybackFmp4LiveMediaFragmentProfileBinding
{
    internal PlaybackFmp4LiveMediaFragmentProfileBinding(
        PlaybackFmp4LiveInitializationProfileProjection initializationProfile,
        PlaybackFmp4LiveMediaFragmentIdentityProjection fragmentIdentity,
        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection decodeTimeline,
        PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? videoTrack,
        PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? audioTrack)
    {
        InitializationProfile = initializationProfile;
        FragmentIdentity = fragmentIdentity;
        DecodeTimeline = decodeTimeline;
        VideoTrack = videoTrack;
        AudioTrack = audioTrack;
    }

    public PlaybackFmp4LiveInitializationProfileProjection InitializationProfile
    { get; }

    public PlaybackFmp4LiveMediaFragmentIdentityProjection FragmentIdentity
    { get; }

    public PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection DecodeTimeline
    { get; }

    public PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? VideoTrack { get; }

    public PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? AudioTrack { get; }

    public uint SequenceNumber => FragmentIdentity.SequenceNumber;

    public bool ContainsVideo => VideoTrack is not null;

    public bool ContainsAudio => AudioTrack is not null;
}

public static class PlaybackFmp4LiveMediaFragmentProfileBinder
{
    public static PlaybackFmp4LiveMediaFragmentProfileBindingResult Bind(
        PlaybackFmp4LiveInitializationProfileProjection? initializationProfile,
        PlaybackFmp4LiveMediaFragmentIdentityProjection? fragmentIdentity,
        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection? decodeTimeline,
        out PlaybackFmp4LiveMediaFragmentProfileBinding? binding)
    {
        binding = null;
        if (initializationProfile is null)
        {
            return PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .MissingInitializationProfile;
        }

        if (fragmentIdentity is null)
        {
            return PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .MissingFragmentIdentity;
        }

        if (decodeTimeline is null)
        {
            return PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .MissingDecodeTimeline;
        }

        if (!IdentityMatches(fragmentIdentity, decodeTimeline))
        {
            return PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .DecodeTimelineIdentityMismatch;
        }

        PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? videoTrack = null;
        PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? audioTrack = null;
        for (var index = 0; index < decodeTimeline.Tracks.Count; index++)
        {
            var track = decodeTimeline.Tracks[index];
            if (track.TrackId == initializationProfile.VideoTrackId)
            {
                videoTrack = track;
                continue;
            }

            if (track.TrackId == initializationProfile.AudioTrackId)
            {
                audioTrack = track;
                continue;
            }

            return PlaybackFmp4LiveMediaFragmentProfileBindingResult
                .UnknownTrackId;
        }

        binding = new(
            initializationProfile,
            fragmentIdentity,
            decodeTimeline,
            videoTrack,
            audioTrack);
        return PlaybackFmp4LiveMediaFragmentProfileBindingResult.Bound;
    }

    private static bool IdentityMatches(
        PlaybackFmp4LiveMediaFragmentIdentityProjection identity,
        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection timeline)
    {
        if (identity.SequenceNumber != timeline.SequenceNumber ||
            identity.Tracks.Count != timeline.Tracks.Count ||
            timeline.Source.SequenceNumber != identity.SequenceNumber ||
            timeline.Source.Tracks.Count != identity.Tracks.Count)
        {
            return false;
        }

        for (var index = 0; index < identity.Tracks.Count; index++)
        {
            var expectedTrackId = identity.Tracks[index].TrackId;
            if (timeline.Source.Tracks[index].TrackId != expectedTrackId ||
                timeline.Tracks[index].TrackId != expectedTrackId)
            {
                return false;
            }
        }

        return true;
    }
}
