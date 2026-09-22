using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
{
    Projected,
    MissingIdentityProjection,
    InvalidMovieFragment,
    IdentityProjectionMismatch,
    InvalidTrackFragment,
    MissingTrackDecodeTime,
    DuplicateTrackDecodeTime,
    InvalidTrackDecodeTime,
}

public sealed class PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline
{
    internal PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline(
        PlaybackFmp4LiveMediaFragmentTrackIdentity identity,
        PlaybackIsoBmffTrackFragmentDecodeTimeBox decodeTime)
    {
        Identity = identity;
        DecodeTime = decodeTime;
    }

    public PlaybackFmp4LiveMediaFragmentTrackIdentity Identity { get; }

    public PlaybackIsoBmffTrackFragmentDecodeTimeBox DecodeTime { get; }

    public uint TrackId => Identity.TrackId;

    public byte Version => DecodeTime.Version;

    public ulong BaseMediaDecodeTime => DecodeTime.BaseMediaDecodeTime;
}

public sealed class PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection
{
    internal PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection(
        PlaybackFmp4LiveMediaFragmentIdentityProjection source,
        ReadOnlyCollection<PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline> tracks)
    {
        Source = source;
        Tracks = tracks;
    }

    public PlaybackFmp4LiveMediaFragmentIdentityProjection Source { get; }

    public uint SequenceNumber => Source.SequenceNumber;

    public IReadOnlyList<PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline> Tracks
    { get; }
}

public static class PlaybackFmp4LiveMediaFragmentDecodeTimelineProjector
{
    private const uint MovieFragmentBoxType = 0x6d6f6f66;
    private const uint TrackFragmentBoxType = 0x74726166;
    private const uint TrackFragmentDecodeTimeBoxType = 0x74666474;

    public static PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult Project(
        ReadOnlySpan<byte> movieFragmentBox,
        PlaybackFmp4LiveMediaFragmentIdentityProjection? source,
        out PlaybackFmp4LiveMediaFragmentDecodeTimelineProjection? projection)
    {
        projection = null;
        if (source is null)
        {
            return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .MissingIdentityProjection;
        }

        if (PlaybackFmp4LiveMediaFragmentIdentityProjector.Project(
                movieFragmentBox,
                out var actualIdentity) !=
                PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected ||
            actualIdentity is null)
        {
            return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .InvalidMovieFragment;
        }

        if (!IdentityMatches(source, actualIdentity))
        {
            return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .IdentityProjectionMismatch;
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
            return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .InvalidMovieFragment;
        }

        var payload = movieFragmentBox.Slice(
            movieFragment.PayloadOffset,
            movieFragment.PayloadLength);
        var tracks = new PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline[
            actualIdentity.Tracks.Count];
        for (var index = 0; index < tracks.Length; index++)
        {
            var identity = actualIdentity.Tracks[index];
            var location = identity.TrackFragment;
            if (location.Offset < 0 ||
                location.BoxLength <= 0 ||
                location.NextOffset > payload.Length ||
                location.Header.Type != TrackFragmentBoxType)
            {
                return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                    .InvalidTrackFragment;
            }

            var trackResult = ProjectTrack(
                payload.Slice(location.Offset, location.BoxLength),
                source.Tracks[index],
                out var track);
            if (trackResult !=
                    PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                        .Projected ||
                track is null)
            {
                return trackResult;
            }

            tracks[index] = track;
        }

        projection = new(source, Array.AsReadOnly(tracks));
        return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected;
    }

    private static bool IdentityMatches(
        PlaybackFmp4LiveMediaFragmentIdentityProjection expected,
        PlaybackFmp4LiveMediaFragmentIdentityProjection actual)
    {
        if (expected.SequenceNumber != actual.SequenceNumber ||
            expected.Tracks.Count != actual.Tracks.Count)
        {
            return false;
        }

        for (var index = 0; index < expected.Tracks.Count; index++)
        {
            if (expected.Tracks[index].TrackId != actual.Tracks[index].TrackId)
            {
                return false;
            }
        }

        return true;
    }

    private static PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
        ProjectTrack(
            ReadOnlySpan<byte> trackFragmentBox,
            PlaybackFmp4LiveMediaFragmentTrackIdentity identity,
            out PlaybackFmp4LiveMediaFragmentTrackDecodeTimeline? track)
    {
        track = null;
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
            return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .InvalidTrackFragment;
        }

        var payload = trackFragmentBox.Slice(
            trackFragment.PayloadOffset,
            trackFragment.PayloadLength);
        PlaybackIsoBmffTrackFragmentDecodeTimeBox? decodeTime = null;
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
                return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                    .InvalidTrackFragment;
            }

            if (child.Header.Type == TrackFragmentDecodeTimeBoxType)
            {
                if (decodeTime is not null)
                {
                    return
                        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                            .DuplicateTrackDecodeTime;
                }

                if (PlaybackIsoBmffTrackFragmentDecodeTimeBoxParser.Parse(
                        payload.Slice(child.Offset, child.BoxLength),
                        out decodeTime) !=
                        PlaybackIsoBmffTrackFragmentDecodeTimeBoxParseResult.Parsed ||
                    decodeTime is null)
                {
                    return
                        PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                            .InvalidTrackDecodeTime;
                }
            }

            offset = child.NextOffset;
        }

        if (decodeTime is null)
        {
            return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult
                .MissingTrackDecodeTime;
        }

        track = new(identity, decodeTime);
        return PlaybackFmp4LiveMediaFragmentDecodeTimelineProjectionResult.Projected;
    }
}
