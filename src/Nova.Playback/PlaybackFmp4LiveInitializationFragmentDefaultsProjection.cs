namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
{
    Projected,
    InvalidInitializationProfile,
    InvalidMovieBox,
    MissingMovieExtendsBox,
    DuplicateMovieExtendsBox,
    InvalidMovieExtendsBox,
    InvalidTrackExtendsBox,
    UnknownTrackId,
    DuplicateTrackExtends,
    UnsupportedSampleDescriptionIndex,
    MissingVideoTrackExtends,
    MissingAudioTrackExtends,
}

public sealed class PlaybackFmp4LiveInitializationFragmentDefaultsProjection
{
    internal PlaybackFmp4LiveInitializationFragmentDefaultsProjection(
        PlaybackFmp4LiveInitializationProfileProjection source,
        PlaybackIsoBmffTrackExtendsBox videoTrackExtends,
        PlaybackIsoBmffTrackExtendsBox audioTrackExtends)
    {
        Source = source;
        VideoTrackExtends = videoTrackExtends;
        AudioTrackExtends = audioTrackExtends;
    }

    public PlaybackFmp4LiveInitializationProfileProjection Source { get; }

    public PlaybackIsoBmffTrackExtendsBox VideoTrackExtends { get; }

    public PlaybackIsoBmffTrackExtendsBox AudioTrackExtends { get; }

    public uint VideoTrackId => VideoTrackExtends.TrackId;

    public uint AudioTrackId => AudioTrackExtends.TrackId;
}

public static class PlaybackFmp4LiveInitializationFragmentDefaultsProjector
{
    private const uint MovieBoxType = 0x6d6f6f76;
    private const uint MovieExtendsBoxType = 0x6d766578;
    private const uint TrackExtendsBoxType = 0x74726578;

    private enum ChildLookupResult
    {
        Found,
        Missing,
        Duplicate,
        InvalidSequence,
    }

    public static PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
        Project(
            ReadOnlySpan<byte> initializationSegment,
            int maxSegmentBytes,
            out PlaybackFmp4LiveInitializationFragmentDefaultsProjection?
                projection)
    {
        projection = null;
        if (PlaybackFmp4LiveInitializationProfileProjector.Project(
                initializationSegment,
                maxSegmentBytes,
                out var profile) !=
                PlaybackFmp4LiveInitializationProfileProjectionResult.Projected ||
            profile is null)
        {
            return
                PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                    .InvalidInitializationProfile;
        }

        var envelope = profile.Identities.Source.Envelope;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                initializationSegment,
                envelope.FirstBoxLength,
                out var movie) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            movie is null ||
            movie.Header.Type != MovieBoxType ||
            movie.BoxLength != envelope.SecondBoxLength)
        {
            return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .InvalidMovieBox;
        }

        var moviePayload = initializationSegment.Slice(
            movie.PayloadOffset,
            movie.PayloadLength);
        var movieExtendsLookup = FindUniqueChild(
            moviePayload,
            MovieExtendsBoxType,
            out var movieExtends);
        if (movieExtendsLookup == ChildLookupResult.Missing)
        {
            return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .MissingMovieExtendsBox;
        }

        if (movieExtendsLookup == ChildLookupResult.Duplicate)
        {
            return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .DuplicateMovieExtendsBox;
        }

        if (movieExtendsLookup != ChildLookupResult.Found ||
            movieExtends is null)
        {
            return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .InvalidMovieBox;
        }

        var movieExtendsPayload = moviePayload.Slice(
            movieExtends.PayloadOffset,
            movieExtends.PayloadLength);
        PlaybackIsoBmffTrackExtendsBox? videoTrackExtends = null;
        PlaybackIsoBmffTrackExtendsBox? audioTrackExtends = null;
        var offset = 0;
        while (offset != movieExtendsPayload.Length)
        {
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    movieExtendsPayload,
                    offset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return
                    PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                        .InvalidMovieExtendsBox;
            }

            if (child.Header.Type == TrackExtendsBoxType)
            {
                if (PlaybackIsoBmffTrackExtendsBoxParser.Parse(
                        movieExtendsPayload.Slice(
                            child.Offset,
                            child.BoxLength),
                        out var trackExtends) !=
                        PlaybackIsoBmffTrackExtendsBoxParseResult.Parsed ||
                    trackExtends is null)
                {
                    return
                        PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                            .InvalidTrackExtendsBox;
                }

                if (trackExtends.DefaultSampleDescriptionIndex != 1)
                {
                    return
                        PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                            .UnsupportedSampleDescriptionIndex;
                }

                if (trackExtends.TrackId == profile.VideoTrackId)
                {
                    if (videoTrackExtends is not null)
                    {
                        return
                            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                                .DuplicateTrackExtends;
                    }

                    videoTrackExtends = trackExtends;
                }
                else if (trackExtends.TrackId == profile.AudioTrackId)
                {
                    if (audioTrackExtends is not null)
                    {
                        return
                            PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                                .DuplicateTrackExtends;
                    }

                    audioTrackExtends = trackExtends;
                }
                else
                {
                    return
                        PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                            .UnknownTrackId;
                }
            }

            offset = child.NextOffset;
        }

        if (videoTrackExtends is null)
        {
            return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .MissingVideoTrackExtends;
        }

        if (audioTrackExtends is null)
        {
            return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
                .MissingAudioTrackExtends;
        }

        projection = new(profile, videoTrackExtends, audioTrackExtends);
        return PlaybackFmp4LiveInitializationFragmentDefaultsProjectionResult
            .Projected;
    }

    private static ChildLookupResult FindUniqueChild(
        ReadOnlySpan<byte> container,
        uint type,
        out PlaybackIsoBmffBoxLocation? match)
    {
        match = null;
        var offset = 0;
        while (offset != container.Length)
        {
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    container,
                    offset,
                    out var child) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                child is null)
            {
                return ChildLookupResult.InvalidSequence;
            }

            if (child.Header.Type == type)
            {
                if (match is not null)
                {
                    return ChildLookupResult.Duplicate;
                }

                match = child;
            }

            offset = child.NextOffset;
        }

        return match is null ? ChildLookupResult.Missing : ChildLookupResult.Found;
    }
}
