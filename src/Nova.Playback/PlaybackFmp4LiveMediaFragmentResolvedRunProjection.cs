using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
{
    Projected,
    MissingInitializationDefaults,
    MissingFragmentBinding,
    InitializationProfileMismatch,
    InvalidMovieFragment,
    FragmentBindingMismatch,
    InvalidTrackFragment,
    MissingTrackRun,
    DuplicateTrackRun,
    InvalidTrackRun,
    UnknownTrackId,
    ResolveFailed,
    UnsupportedSampleDescriptionIndex,
}

public sealed class PlaybackFmp4LiveMediaFragmentResolvedTrackRun
{
    internal PlaybackFmp4LiveMediaFragmentResolvedTrackRun(
        PlaybackFmp4LiveMediaFragmentTrackIdentity identity,
        PlaybackIsoBmffTrackRunBox trackRun,
        PlaybackIsoBmffResolvedFragmentRun resolvedRun)
    {
        Identity = identity;
        TrackRun = trackRun;
        ResolvedRun = resolvedRun;
    }

    public PlaybackFmp4LiveMediaFragmentTrackIdentity Identity { get; }

    public PlaybackIsoBmffTrackRunBox TrackRun { get; }

    public PlaybackIsoBmffResolvedFragmentRun ResolvedRun { get; }

    public uint TrackId => Identity.TrackId;
}

public sealed class PlaybackFmp4LiveMediaFragmentResolvedRunProjection
{
    internal PlaybackFmp4LiveMediaFragmentResolvedRunProjection(
        PlaybackFmp4LiveInitializationFragmentDefaultsProjection
            initializationDefaults,
        PlaybackFmp4LiveMediaFragmentProfileBinding fragmentBinding,
        ReadOnlyCollection<PlaybackFmp4LiveMediaFragmentResolvedTrackRun> tracks,
        PlaybackFmp4LiveMediaFragmentResolvedTrackRun? videoTrack,
        PlaybackFmp4LiveMediaFragmentResolvedTrackRun? audioTrack)
    {
        InitializationDefaults = initializationDefaults;
        FragmentBinding = fragmentBinding;
        Tracks = tracks;
        VideoTrack = videoTrack;
        AudioTrack = audioTrack;
    }

    public PlaybackFmp4LiveInitializationFragmentDefaultsProjection
        InitializationDefaults
    { get; }

    public PlaybackFmp4LiveMediaFragmentProfileBinding FragmentBinding { get; }

    public IReadOnlyList<PlaybackFmp4LiveMediaFragmentResolvedTrackRun> Tracks
    { get; }

    public PlaybackFmp4LiveMediaFragmentResolvedTrackRun? VideoTrack { get; }

    public PlaybackFmp4LiveMediaFragmentResolvedTrackRun? AudioTrack { get; }

    public uint SequenceNumber => FragmentBinding.SequenceNumber;

    public bool ContainsVideo => VideoTrack is not null;

    public bool ContainsAudio => AudioTrack is not null;
}

public static class PlaybackFmp4LiveMediaFragmentResolvedRunProjector
{
    private const uint MovieFragmentBoxType = 0x6d6f6f66;
    private const uint TrackFragmentBoxType = 0x74726166;
    private const uint TrackRunBoxType = 0x7472756e;

    public static PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult Project(
        ReadOnlySpan<byte> movieFragmentBox,
        PlaybackFmp4LiveInitializationFragmentDefaultsProjection?
            initializationDefaults,
        PlaybackFmp4LiveMediaFragmentProfileBinding? fragmentBinding,
        out PlaybackFmp4LiveMediaFragmentResolvedRunProjection? projection)
    {
        projection = null;
        if (initializationDefaults is null)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .MissingInitializationDefaults;
        }

        if (fragmentBinding is null)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .MissingFragmentBinding;
        }

        if (initializationDefaults.VideoTrackId !=
                fragmentBinding.InitializationProfile.VideoTrackId ||
            initializationDefaults.AudioTrackId !=
                fragmentBinding.InitializationProfile.AudioTrackId)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InitializationProfileMismatch;
        }

        if (PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragmentBox,
                out var actualIdentity) !=
                PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected ||
            actualIdentity is null)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InvalidMovieFragment;
        }

        if (PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector.Project(
                movieFragmentBox,
                actualIdentity,
                out var actualTimeline) !=
                PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected ||
            actualTimeline is null)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InvalidMovieFragment;
        }

        if (!BindingMatches(fragmentBinding, actualIdentity, actualTimeline))
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .FragmentBindingMismatch;
        }

        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                movieFragmentBox,
                0,
                out var movieFragment) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            movieFragment is null ||
            movieFragment.Offset != 0 ||
            movieFragment.NextOffset != movieFragmentBox.Length ||
            movieFragment.Header.Type != MovieFragmentBoxType)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InvalidMovieFragment;
        }

        var moviePayload = movieFragmentBox.Slice(
            movieFragment.PayloadOffset,
            movieFragment.PayloadLength);
        var tracks = new PlaybackFmp4LiveMediaFragmentResolvedTrackRun[
            actualIdentity.Tracks.Count];
        PlaybackFmp4LiveMediaFragmentResolvedTrackRun? videoTrack = null;
        PlaybackFmp4LiveMediaFragmentResolvedTrackRun? audioTrack = null;
        for (var index = 0; index < tracks.Length; index++)
        {
            var identity = actualIdentity.Tracks[index];
            var result = ProjectTrack(
                moviePayload,
                identity,
                initializationDefaults,
                out var track);
            if (result !=
                    PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                        .Projected ||
                track is null)
            {
                return result;
            }

            tracks[index] = track;
            if (track.TrackId == initializationDefaults.VideoTrackId)
            {
                videoTrack = track;
            }
            else if (track.TrackId == initializationDefaults.AudioTrackId)
            {
                audioTrack = track;
            }
            else
            {
                return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                    .UnknownTrackId;
            }
        }

        projection = new(
            initializationDefaults,
            fragmentBinding,
            Array.AsReadOnly(tracks),
            videoTrack,
            audioTrack);
        return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected;
    }

    private static PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
        ProjectTrack(
            ReadOnlySpan<byte> moviePayload,
            PlaybackFmp4LiveMediaFragmentTrackIdentity identity,
            PlaybackFmp4LiveInitializationFragmentDefaultsProjection
                initializationDefaults,
            out PlaybackFmp4LiveMediaFragmentResolvedTrackRun? projected)
    {
        projected = null;
        var location = identity.TrackFragment;
        if (location.Offset < 0 ||
            location.BoxLength <= 0 ||
            location.NextOffset > moviePayload.Length ||
            location.Header.Type != TrackFragmentBoxType)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InvalidTrackFragment;
        }

        var trackFragmentBox = moviePayload.Slice(
            location.Offset,
            location.BoxLength);
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                trackFragmentBox,
                0,
                out var trackFragment) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            trackFragment is null ||
            trackFragment.Offset != 0 ||
            trackFragment.NextOffset != trackFragmentBox.Length ||
            trackFragment.Header.Type != TrackFragmentBoxType)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .InvalidTrackFragment;
        }

        var payload = trackFragmentBox.Slice(
            trackFragment.PayloadOffset,
            trackFragment.PayloadLength);
        PlaybackIsoBmffTrackRunBox? trackRun = null;
        var offset = 0;
        while (offset != payload.Length)
        {
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    payload,
                    offset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                    .InvalidTrackFragment;
            }

            if (child.Header.Type == TrackRunBoxType)
            {
                if (trackRun is not null)
                {
                    return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                        .DuplicateTrackRun;
                }

                if (PlaybackIsoBmffTrackRunBoxParser.Parse(
                        payload.Slice(child.Offset, child.BoxLength),
                        out trackRun) !=
                        PlaybackIsoBmffTrackRunBoxParseResult.Parsed ||
                    trackRun is null)
                {
                    return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                        .InvalidTrackRun;
                }
            }

            offset = child.NextOffset;
        }

        if (trackRun is null)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .MissingTrackRun;
        }

        PlaybackIsoBmffTrackExtendsBox? trackExtends;
        if (identity.TrackId == initializationDefaults.VideoTrackId)
        {
            trackExtends = initializationDefaults.VideoTrackExtends;
        }
        else if (identity.TrackId == initializationDefaults.AudioTrackId)
        {
            trackExtends = initializationDefaults.AudioTrackExtends;
        }
        else
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .UnknownTrackId;
        }

        if (PlaybackIsoBmffFragmentSampleDefaultsResolver.Resolve(
                trackExtends,
                identity.TrackFragmentHeader,
                trackRun,
                out var resolvedRun) !=
                PlaybackIsoBmffFragmentSampleDefaultsResolveResult.Resolved ||
            resolvedRun is null)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .ResolveFailed;
        }

        if (resolvedRun.SampleDescriptionIndex != 1)
        {
            return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult
                .UnsupportedSampleDescriptionIndex;
        }

        projected = new(identity, trackRun, resolvedRun);
        return PlaybackFmp4LiveMediaFragmentResolvedRunProjectionResult.Projected;
    }

    private static bool BindingMatches(
        PlaybackFmp4LiveMediaFragmentProfileBinding binding,
        PlaybackFmp4LiveMediaFragmentIdentityProjection actualIdentity,
        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection actualTimeline)
    {
        if (binding.SequenceNumber != actualIdentity.SequenceNumber ||
            binding.SequenceNumber != actualTimeline.SequenceNumber ||
            binding.FragmentIdentity.Tracks.Count != actualIdentity.Tracks.Count ||
            binding.DecodeTimeline.Tracks.Count != actualTimeline.Tracks.Count)
        {
            return false;
        }

        for (var index = 0; index < actualIdentity.Tracks.Count; index++)
        {
            var actualTrackId = actualIdentity.Tracks[index].TrackId;
            if (binding.FragmentIdentity.Tracks[index].TrackId != actualTrackId ||
                binding.DecodeTimeline.Tracks[index].TrackId != actualTrackId ||
                actualTimeline.Tracks[index].TrackId != actualTrackId ||
                binding.DecodeTimeline.Tracks[index].Version !=
                    actualTimeline.Tracks[index].Version ||
                binding.DecodeTimeline.Tracks[index].BaseMediaDecodeTime !=
                    actualTimeline.Tracks[index].BaseMediaDecodeTime)
            {
                return false;
            }
        }

        return true;
    }
}
