using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
{
    Projected,
    InvalidMovieFragment,
    MissingMovieFragmentHeader,
    DuplicateMovieFragmentHeader,
    InvalidMovieFragmentHeader,
    MissingTrackFragment,
    TrackFragmentLimitExceeded,
    InvalidTrackFragment,
    MissingTrackFragmentHeader,
    DuplicateTrackFragmentHeader,
    InvalidTrackFragmentHeader,
    DuplicateTrackId,
}

public sealed class PlaybackFmp4LiveMediaFragmentTrackIdentity
{
    internal PlaybackFmp4LiveMediaFragmentTrackIdentity(
        PlaybackIsoBmffBoxLocation trackFragment,
        PlaybackIsoBmffTrackFragmentHeaderBox trackFragmentHeader)
    {
        TrackFragment = trackFragment;
        TrackFragmentHeader = trackFragmentHeader;
    }

    public PlaybackIsoBmffBoxLocation TrackFragment { get; }

    public PlaybackIsoBmffTrackFragmentHeaderBox TrackFragmentHeader { get; }

    public uint TrackId => TrackFragmentHeader.TrackId;
}

public sealed class PlaybackFmp4LiveMediaFragmentIdentityProjection
{
    internal PlaybackFmp4LiveMediaFragmentIdentityProjection(
        PlaybackIsoBmffMovieFragmentHeaderBox movieFragmentHeader,
        ReadOnlyCollection<PlaybackFmp4LiveMediaFragmentTrackIdentity> tracks)
    {
        MovieFragmentHeader = movieFragmentHeader;
        Tracks = tracks;
    }

    public PlaybackIsoBmffMovieFragmentHeaderBox MovieFragmentHeader { get; }

    public uint SequenceNumber => MovieFragmentHeader.SequenceNumber;

    public IReadOnlyList<PlaybackFmp4LiveMediaFragmentTrackIdentity> Tracks
    { get; }
}

public static class PlaybackFmp4LiveMediaFragmentIdentityProjector
{
    public const int MaximumTrackFragmentCount = 64;

    private const uint MovieFragmentBoxType = 0x6d6f6f66;
    private const uint MovieFragmentHeaderBoxType = 0x6d666864;
    private const uint TrackFragmentBoxType = 0x74726166;
    private const uint TrackFragmentHeaderBoxType = 0x74666864;

    public static PlaybackFmp4LiveMediaFragmentIdentityProjectionResult Project(
        ReadOnlySpan<byte> movieFragmentBox,
        out PlaybackFmp4LiveMediaFragmentIdentityProjection? projection)
    {
        projection = null;
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
            return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .InvalidMovieFragment;
        }

        var payload = movieFragmentBox.Slice(
            movieFragment.PayloadOffset,
            movieFragment.PayloadLength);
        PlaybackIsoBmffMovieFragmentHeaderBox? movieFragmentHeader = null;
        var tracks = new List<PlaybackFmp4LiveMediaFragmentTrackIdentity>();
        var trackIds = new HashSet<uint>();
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
                return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                    .InvalidMovieFragment;
            }

            if (child.Header.Type == MovieFragmentHeaderBoxType)
            {
                if (movieFragmentHeader is not null)
                {
                    return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                        .DuplicateMovieFragmentHeader;
                }

                var childBox = payload.Slice(child.Offset, child.BoxLength);
                if (PlaybackIsoBmffMovieFragmentHeaderBoxParser.Parse(
                        childBox,
                        out movieFragmentHeader) !=
                        PlaybackIsoBmffMovieFragmentHeaderBoxParseResult.Parsed ||
                    movieFragmentHeader is null)
                {
                    return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                        .InvalidMovieFragmentHeader;
                }
            }
            else if (child.Header.Type == TrackFragmentBoxType)
            {
                if (tracks.Count >= MaximumTrackFragmentCount)
                {
                    return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                        .TrackFragmentLimitExceeded;
                }

                var trackResult = ProjectTrack(
                    payload.Slice(child.Offset, child.BoxLength),
                    child,
                    out var track);
                if (trackResult !=
                        PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                            .Projected ||
                    track is null)
                {
                    return trackResult;
                }

                if (!trackIds.Add(track.TrackId))
                {
                    return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                        .DuplicateTrackId;
                }

                tracks.Add(track);
            }

            offset = child.NextOffset;
        }

        if (movieFragmentHeader is null)
        {
            return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .MissingMovieFragmentHeader;
        }

        if (tracks.Count == 0)
        {
            return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .MissingTrackFragment;
        }

        projection = new(
            movieFragmentHeader,
            Array.AsReadOnly(tracks.ToArray()));
        return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected;
    }

    private static PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
        ProjectTrack(
            ReadOnlySpan<byte> trackFragmentBox,
            PlaybackIsoBmffBoxLocation source,
            out PlaybackFmp4LiveMediaFragmentTrackIdentity? track)
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
            return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .InvalidTrackFragment;
        }

        var payload = trackFragmentBox.Slice(
            trackFragment.PayloadOffset,
            trackFragment.PayloadLength);
        PlaybackIsoBmffTrackFragmentHeaderBox? trackFragmentHeader = null;
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
                return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                    .InvalidTrackFragment;
            }

            if (child.Header.Type == TrackFragmentHeaderBoxType)
            {
                if (trackFragmentHeader is not null)
                {
                    return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                        .DuplicateTrackFragmentHeader;
                }

                var childBox = payload.Slice(child.Offset, child.BoxLength);
                if (PlaybackIsoBmffTrackFragmentHeaderBoxParser.Parse(
                        childBox,
                        out trackFragmentHeader) !=
                        PlaybackIsoBmffTrackFragmentHeaderBoxParseResult.Parsed ||
                    trackFragmentHeader is null)
                {
                    return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                        .InvalidTrackFragmentHeader;
                }
            }

            offset = child.NextOffset;
        }

        if (trackFragmentHeader is null)
        {
            return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult
                .MissingTrackFragmentHeader;
        }

        track = new(source, trackFragmentHeader);
        return PlaybackFmp4LiveMediaFragmentIdentityProjectionResult.Projected;
    }
}
