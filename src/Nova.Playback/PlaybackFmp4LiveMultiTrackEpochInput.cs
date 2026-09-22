using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMultiTrackEpochInputBindingResult
{
    Bound,
    MissingResolvedRunProjection,
    InitializationProfileMismatch,
    UnsupportedTrackCount,
    UnknownTrackId,
    DuplicateTrackId,
    MissingDecodeTimeline,
    InvalidTimedRunProjection,
}

public sealed class PlaybackFmp4LiveMultiTrackEpochInputTrack
{
    internal PlaybackFmp4LiveMultiTrackEpochInputTrack(
        PlaybackFmp4LiveInitializationTrackIdentity initializationIdentity,
        PlaybackIsoBmffHandlerKind mediaKind,
        PlaybackFmp4LiveMediaFragmentResolvedTrackRun resolvedRun,
        PlaybackIsoBmffTimedFragmentRun timedRun)
    {
        InitializationIdentity = initializationIdentity;
        MediaKind = mediaKind;
        ResolvedRun = resolvedRun;
        TimedRun = timedRun;
    }

    public PlaybackFmp4LiveInitializationTrackIdentity InitializationIdentity
    { get; }

    public PlaybackIsoBmffHandlerKind MediaKind { get; }

    public PlaybackFmp4LiveMediaFragmentResolvedTrackRun ResolvedRun { get; }

    public PlaybackIsoBmffTimedFragmentRun TimedRun { get; }

    public uint TrackId => TimedRun.TrackId;
}

public sealed class PlaybackFmp4LiveMultiTrackEpochInput
{
    internal PlaybackFmp4LiveMultiTrackEpochInput(
        PlaybackFmp4LiveInitializationProfileProjection initializationProfile,
        PlaybackFmp4LiveMediaFragmentResolvedRunProjection resolvedRuns,
        PlaybackIsoBmffMovieFragmentHeaderBox fragmentHeader,
        ReadOnlyCollection<PlaybackFmp4LiveMultiTrackEpochInputTrack> tracks,
        ReadOnlyCollection<PlaybackIsoBmffTimedFragmentRun> timedRuns)
    {
        InitializationProfile = initializationProfile;
        ResolvedRuns = resolvedRuns;
        FragmentHeader = fragmentHeader;
        Tracks = tracks;
        TimedRuns = timedRuns;
    }

    public PlaybackFmp4LiveInitializationProfileProjection InitializationProfile
    { get; }

    public PlaybackFmp4LiveMediaFragmentResolvedRunProjection ResolvedRuns
    { get; }

    public PlaybackIsoBmffMovieFragmentHeaderBox FragmentHeader { get; }

    public IReadOnlyList<PlaybackFmp4LiveMultiTrackEpochInputTrack> Tracks
    { get; }

    public IReadOnlyList<PlaybackIsoBmffTimedFragmentRun> TimedRuns
    { get; }

    public uint SequenceNumber => FragmentHeader.SequenceNumber;
}

public static class PlaybackFmp4LiveMultiTrackEpochInputBinder
{
    public static PlaybackFmp4LiveMultiTrackEpochInputBindingResult Bind(
        PlaybackFmp4LiveMediaFragmentResolvedRunProjection? resolvedRuns,
        out PlaybackFmp4LiveMultiTrackEpochInput? input)
    {
        input = null;
        if (resolvedRuns is null)
        {
            return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                .MissingResolvedRunProjection;
        }

        var initializationProfile = resolvedRuns.InitializationDefaults.Source;
        if (!ReferenceEquals(
                initializationProfile,
                resolvedRuns.FragmentBinding.InitializationProfile))
        {
            return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                .InitializationProfileMismatch;
        }

        if (resolvedRuns.Tracks.Count is < 1 or
            > PlaybackFmp4LiveMultiTrackEpochStateMachine.MaximumTrackCount)
        {
            return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                .UnsupportedTrackCount;
        }

        var tracks = new PlaybackFmp4LiveMultiTrackEpochInputTrack[
            resolvedRuns.Tracks.Count];
        var trackIds = new HashSet<uint>();
        for (var index = 0; index < tracks.Length; index++)
        {
            var resolvedTrack = resolvedRuns.Tracks[index];
            if (!trackIds.Add(resolvedTrack.TrackId))
            {
                return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                    .DuplicateTrackId;
            }

            PlaybackFmp4LiveInitializationTrackIdentity initializationIdentity;
            PlaybackIsoBmffHandlerKind mediaKind;
            if (resolvedTrack.TrackId == initializationProfile.VideoTrackId)
            {
                initializationIdentity = initializationProfile.VideoIdentity;
                mediaKind = PlaybackIsoBmffHandlerKind.Video;
            }
            else if (resolvedTrack.TrackId == initializationProfile.AudioTrackId)
            {
                initializationIdentity = initializationProfile.AudioIdentity;
                mediaKind = PlaybackIsoBmffHandlerKind.Audio;
            }
            else
            {
                return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                    .UnknownTrackId;
            }

            var decodeTimeline = FindDecodeTimeline(
                resolvedRuns.FragmentBinding.DecodeTimeline.Tracks,
                resolvedTrack.TrackId);
            if (decodeTimeline is null)
            {
                return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                    .MissingDecodeTimeline;
            }

            if (PlaybackIsoBmffFragmentSampleTimingProjector.Project(
                    decodeTimeline.DecodeTime,
                    resolvedTrack.ResolvedRun,
                    out var timedRun) !=
                    PlaybackIsoBmffFragmentSampleTimingProjectResult.Projected ||
                timedRun is null)
            {
                return PlaybackFmp4LiveMultiTrackEpochInputBindingResult
                    .InvalidTimedRunProjection;
            }

            tracks[index] = new(
                initializationIdentity,
                mediaKind,
                resolvedTrack,
                timedRun);
        }

        Array.Sort(tracks, static (left, right) =>
            left.TrackId.CompareTo(right.TrackId));
        var timedRuns = new PlaybackIsoBmffTimedFragmentRun[tracks.Length];
        for (var index = 0; index < timedRuns.Length; index++)
        {
            timedRuns[index] = tracks[index].TimedRun;
        }

        input = new(
            initializationProfile,
            resolvedRuns,
            resolvedRuns.FragmentBinding.FragmentIdentity.MovieFragmentHeader,
            Array.AsReadOnly(tracks),
            Array.AsReadOnly(timedRuns));
        return PlaybackFmp4LiveMultiTrackEpochInputBindingResult.Bound;
    }

    private static PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline?
        FindDecodeTimeline(
            IReadOnlyList<PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline>
                timelines,
            uint trackId)
    {
        for (var index = 0; index < timelines.Count; index++)
        {
            if (timelines[index].TrackId == trackId)
            {
                return timelines[index];
            }
        }

        return null;
    }
}
