using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
{
    Projected,
    InvalidTrackInventory,
    InvalidTrackBox,
    MissingMediaBox,
    DuplicateMediaBox,
    InvalidMediaBox,
    MissingHandlerBox,
    DuplicateHandlerBox,
    InvalidHandlerBox,
}

public sealed class PlaybackFmp4LiveInitializationTrackHandler
{
    internal PlaybackFmp4LiveInitializationTrackHandler(
        PlaybackIsoBmffBoxLocation track,
        PlaybackIsoBmffHandlerReferenceBox handler)
    {
        Track = track;
        Handler = handler;
    }

    public PlaybackIsoBmffBoxLocation Track { get; }

    public PlaybackIsoBmffHandlerReferenceBox Handler { get; }

    public PlaybackIsoBmffHandlerKind Kind => Handler.Kind;
}

public sealed class PlaybackFmp4LiveInitializationTrackHandlerProjection
{
    internal PlaybackFmp4LiveInitializationTrackHandlerProjection(
        PlaybackFmp4LiveInitializationTrackInventory source,
        ReadOnlyCollection<PlaybackFmp4LiveInitializationTrackHandler> tracks)
    {
        Source = source;
        Tracks = tracks;
    }

    public PlaybackFmp4LiveInitializationTrackInventory Source { get; }

    public IReadOnlyList<PlaybackFmp4LiveInitializationTrackHandler> Tracks
    { get; }
}

public static class PlaybackFmp4LiveInitializationTrackHandlerProjector
{
    private const uint MediaBoxType = 0x6d646961;
    private const uint HandlerReferenceBoxType = 0x68646c72;

    private enum ChildLookupResult
    {
        Found,
        Missing,
        Duplicate,
        InvalidSequence,
    }

    public static PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
        Project(
            ReadOnlySpan<byte> initializationSegment,
            int maxSegmentBytes,
            out PlaybackFmp4LiveInitializationTrackHandlerProjection?
                projection)
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
            return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                .InvalidTrackInventory;
        }

        var tracks = new PlaybackFmp4LiveInitializationTrackHandler[
            inventory.Tracks.Count];
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
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .InvalidTrackBox;
            }

            var trackPayload = initializationSegment.Slice(
                actualTrack.PayloadOffset,
                actualTrack.PayloadLength);
            var mediaLookup = FindUniqueChild(
                trackPayload,
                MediaBoxType,
                out var media);
            if (mediaLookup == ChildLookupResult.Missing)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .MissingMediaBox;
            }

            if (mediaLookup == ChildLookupResult.Duplicate)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .DuplicateMediaBox;
            }

            if (mediaLookup != ChildLookupResult.Found || media is null)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .InvalidTrackBox;
            }

            var mediaPayload = trackPayload.Slice(
                media.PayloadOffset,
                media.PayloadLength);
            var handlerLookup = FindUniqueChild(
                mediaPayload,
                HandlerReferenceBoxType,
                out var handlerLocation);
            if (handlerLookup == ChildLookupResult.Missing)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .MissingHandlerBox;
            }

            if (handlerLookup == ChildLookupResult.Duplicate)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .DuplicateHandlerBox;
            }

            if (handlerLookup != ChildLookupResult.Found ||
                handlerLocation is null)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .InvalidMediaBox;
            }

            var handlerBox = mediaPayload.Slice(
                handlerLocation.Offset,
                handlerLocation.BoxLength);
            if (PlaybackIsoBmffHandlerReferenceBoxParser.Parse(
                    handlerBox,
                    out var handler) !=
                    PlaybackIsoBmffHandlerReferenceBoxParseResult.Parsed ||
                handler is null)
            {
                return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
                    .InvalidHandlerBox;
            }

            tracks[index] = new(recordedTrack, handler);
        }

        projection = new(inventory, Array.AsReadOnly(tracks));
        return PlaybackFmp4LiveInitializationTrackHandlerProjectionResult
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
