using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
{
    Projected,
    InvalidTrackInventory,
    InvalidTrackBox,
    MissingTrackHeaderBox,
    DuplicateTrackHeaderBox,
    InvalidTrackHeaderBox,
    DuplicateTrackId,
}

public sealed class PlaybackFmp4LiveInitializationTrackIdentity
{
    internal PlaybackFmp4LiveInitializationTrackIdentity(
        PlaybackIsoBmffBoxLocation track,
        PlaybackIsoBmffTrackHeaderIdentityBox trackHeader)
    {
        Track = track;
        TrackHeader = trackHeader;
    }

    public PlaybackIsoBmffBoxLocation Track { get; }

    public PlaybackIsoBmffTrackHeaderIdentityBox TrackHeader { get; }

    public uint TrackId => TrackHeader.TrackId;
}

public sealed class PlaybackFmp4LiveInitializationTrackIdentityProjection
{
    internal PlaybackFmp4LiveInitializationTrackIdentityProjection(
        PlaybackFmp4LiveInitializationTrackInventory source,
        ReadOnlyCollection<PlaybackFmp4LiveInitializationTrackIdentity> tracks)
    {
        Source = source;
        Tracks = tracks;
    }

    public PlaybackFmp4LiveInitializationTrackInventory Source { get; }

    public IReadOnlyList<PlaybackFmp4LiveInitializationTrackIdentity> Tracks
    { get; }
}

public static class PlaybackFmp4LiveInitializationTrackIdentityProjector
{
    private const uint TrackHeaderBoxType = 0x746b6864;

    private enum ChildLookupResult
    {
        Found,
        Missing,
        Duplicate,
        InvalidSequence,
    }

    public static PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
        Project(
            ReadOnlySpan<byte> initializationSegment,
            int maxSegmentBytes,
            out PlaybackFmp4LiveInitializationTrackIdentityProjection? projection)
    {
        projection = null;
        if (PlaybackFmp4LiveInitializationTrackInventoryProjector.Project(
                initializationSegment,
                maxSegmentBytes,
                out var inventory) !=
                PlaybackFmp4LiveInitializationTrackInventoryProjectionResult
                    .Projected ||
            inventory is null)
        {
            return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                .InvalidTrackInventory;
        }

        var tracks = new PlaybackFmp4LiveInitializationTrackIdentity[
            inventory.Tracks.Count];
        var trackIds = new HashSet<uint>();
        for (var index = 0; index < inventory.Tracks.Count; index++)
        {
            var recordedTrack = inventory.Tracks[index];
            if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                    initializationSegment,
                    recordedTrack.Offset,
                    out var actualTrack) !=
                    PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
                actualTrack is null ||
                actualTrack.Offset != recordedTrack.Offset ||
                actualTrack.BoxLength != recordedTrack.BoxLength ||
                actualTrack.Header.Type != recordedTrack.Header.Type ||
                actualTrack.Header.HeaderSize != recordedTrack.Header.HeaderSize)
            {
                return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .InvalidTrackBox;
            }

            var trackPayload = initializationSegment.Slice(
                actualTrack.PayloadOffset,
                actualTrack.PayloadLength);
            var trackHeaderLookup = FindUniqueChild(
                trackPayload,
                TrackHeaderBoxType,
                out var trackHeaderLocation);
            if (trackHeaderLookup == ChildLookupResult.Missing)
            {
                return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .MissingTrackHeaderBox;
            }

            if (trackHeaderLookup == ChildLookupResult.Duplicate)
            {
                return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .DuplicateTrackHeaderBox;
            }

            if (trackHeaderLookup != ChildLookupResult.Found ||
                trackHeaderLocation is null)
            {
                return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .InvalidTrackBox;
            }

            var trackHeaderBox = trackPayload.Slice(
                trackHeaderLocation.Offset,
                trackHeaderLocation.BoxLength);
            if (PlaybackIsoBmffTrackHeaderIdentityBoxParser.Parse(
                    trackHeaderBox,
                    out var trackHeader) !=
                    PlaybackIsoBmffTrackHeaderIdentityBoxParseResult.Parsed ||
                trackHeader is null)
            {
                return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .InvalidTrackHeaderBox;
            }

            if (!trackIds.Add(trackHeader.TrackId))
            {
                return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult
                    .DuplicateTrackId;
            }

            tracks[index] = new(recordedTrack, trackHeader);
        }

        projection = new(inventory, Array.AsReadOnly(tracks));
        return PlaybackFmp4LiveInitializationTrackIdentityProjectionResult.Projected;
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
