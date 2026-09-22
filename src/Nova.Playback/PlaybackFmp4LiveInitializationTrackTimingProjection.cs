namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationTrackTimingProjectionResult
{
    Projected,
    InvalidInitializationProfile,
    InvalidTrackBox,
    MissingMediaBox,
    DuplicateMediaBox,
    MissingMediaHeaderBox,
    DuplicateMediaHeaderBox,
    InvalidMediaHeaderBox,
}

public sealed class PlaybackFmp4LiveInitializationTrackTiming
{
    internal PlaybackFmp4LiveInitializationTrackTiming(
        PlaybackFmp4LiveInitializationTrackIdentity identity,
        PlaybackIsoBmffMediaHeaderTimingBox timing)
    {
        Identity = identity;
        Timing = timing;
    }

    public PlaybackFmp4LiveInitializationTrackIdentity Identity { get; }

    public PlaybackIsoBmffMediaHeaderTimingBox Timing { get; }

    public uint TrackId => Identity.TrackId;

    public uint Timescale => Timing.Timescale;
}

public sealed class PlaybackFmp4LiveInitializationTrackTimingProjection
{
    internal PlaybackFmp4LiveInitializationTrackTimingProjection(
        PlaybackFmp4LiveInitializationProfileProjection source,
        PlaybackFmp4LiveInitializationTrackTiming videoTrack,
        PlaybackFmp4LiveInitializationTrackTiming audioTrack)
    {
        Source = source;
        VideoTrack = videoTrack;
        AudioTrack = audioTrack;
    }

    public PlaybackFmp4LiveInitializationProfileProjection Source { get; }

    public PlaybackFmp4LiveInitializationTrackTiming VideoTrack { get; }

    public PlaybackFmp4LiveInitializationTrackTiming AudioTrack { get; }

    public uint VideoTrackId => VideoTrack.TrackId;

    public uint AudioTrackId => AudioTrack.TrackId;

    public uint VideoTimescale => VideoTrack.Timescale;

    public uint AudioTimescale => AudioTrack.Timescale;
}

public static class PlaybackFmp4LiveInitializationTrackTimingProjector
{
    private const uint MediaBoxType = 0x6d646961;
    private const uint MediaHeaderBoxType = 0x6d646864;

    private enum ChildLookupResult
    {
        Found,
        Missing,
        Duplicate,
        InvalidSequence,
    }

    public static PlaybackFmp4LiveInitializationTrackTimingProjectionResult Project(
        ReadOnlySpan<byte> initializationSegment,
        int maxSegmentBytes,
        out PlaybackFmp4LiveInitializationTrackTimingProjection? projection)
    {
        projection = null;
        if (PlaybackFmp4LiveInitializationProfileProjector.Project(
                initializationSegment,
                maxSegmentBytes,
                out var profile) !=
                PlaybackFmp4LiveInitializationProfileProjectionResult.Projected ||
            profile is null)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidInitializationProfile;
        }

        var videoResult = ProjectTrack(
            initializationSegment,
            profile.VideoIdentity,
            out var videoTrack);
        if (videoResult !=
                PlaybackFmp4LiveInitializationTrackTimingProjectionResult.Projected ||
            videoTrack is null)
        {
            return videoResult;
        }

        var audioResult = ProjectTrack(
            initializationSegment,
            profile.AudioIdentity,
            out var audioTrack);
        if (audioResult !=
                PlaybackFmp4LiveInitializationTrackTimingProjectionResult.Projected ||
            audioTrack is null)
        {
            return audioResult;
        }

        projection = new(profile, videoTrack, audioTrack);
        return PlaybackFmp4LiveInitializationTrackTimingProjectionResult.Projected;
    }

    private static PlaybackFmp4LiveInitializationTrackTimingProjectionResult
        ProjectTrack(
            ReadOnlySpan<byte> initializationSegment,
            PlaybackFmp4LiveInitializationTrackIdentity identity,
            out PlaybackFmp4LiveInitializationTrackTiming? timing)
    {
        timing = null;
        var recordedTrack = identity.Track;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                initializationSegment,
                recordedTrack.Offset,
                out var actualTrack) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            actualTrack is null ||
            !SameBox(recordedTrack, actualTrack))
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidTrackBox;
        }

        var trackPayload = initializationSegment.Slice(
            actualTrack.PayloadOffset,
            actualTrack.PayloadLength);
        var mediaResult = FindUniqueChild(
            trackPayload,
            MediaBoxType,
            out var media);
        if (mediaResult == ChildLookupResult.Missing)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .MissingMediaBox;
        }

        if (mediaResult == ChildLookupResult.Duplicate)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .DuplicateMediaBox;
        }

        if (mediaResult != ChildLookupResult.Found || media is null)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidTrackBox;
        }

        var mediaPayload = trackPayload.Slice(
            media.PayloadOffset,
            media.PayloadLength);
        var mediaHeaderResult = FindUniqueChild(
            mediaPayload,
            MediaHeaderBoxType,
            out var mediaHeader);
        if (mediaHeaderResult == ChildLookupResult.Missing)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .MissingMediaHeaderBox;
        }

        if (mediaHeaderResult == ChildLookupResult.Duplicate)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .DuplicateMediaHeaderBox;
        }

        if (mediaHeaderResult != ChildLookupResult.Found ||
            mediaHeader is null)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidTrackBox;
        }

        if (PlaybackIsoBmffMediaHeaderTimingBoxParser.Parse(
                mediaPayload.Slice(mediaHeader.Offset, mediaHeader.BoxLength),
                out var parsedTiming) !=
                PlaybackIsoBmffMediaHeaderTimingBoxParseResult.Parsed ||
            parsedTiming is null)
        {
            return PlaybackFmp4LiveInitializationTrackTimingProjectionResult
                .InvalidMediaHeaderBox;
        }

        timing = new(identity, parsedTiming);
        return PlaybackFmp4LiveInitializationTrackTimingProjectionResult.Projected;
    }

    private static bool SameBox(
        PlaybackIsoBmffBoxLocation left,
        PlaybackIsoBmffBoxLocation right) =>
        left.Offset == right.Offset &&
        left.BoxLength == right.BoxLength &&
        left.Header.Type == right.Header.Type &&
        left.Header.HeaderSize == right.Header.HeaderSize;

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
