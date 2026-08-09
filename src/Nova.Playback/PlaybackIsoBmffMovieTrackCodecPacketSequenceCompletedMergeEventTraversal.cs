using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
{
    Traversed,
    MissingCursor,
    InvalidEventLimit,
    InvalidCursor,
    AdvanceFailed,
    AcknowledgementFailed,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal :
    IPlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalSource
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                initialCursor,
            int maximumEventCount,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement>
                acknowledgements,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                finalCursor,
            bool isExhausted)
    {
        InitialCursor = initialCursor;
        MaximumEventCount = maximumEventCount;
        Acknowledgements = acknowledgements;
        FinalCursor = finalCursor;
        IsExhausted = isExhausted;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        InitialCursor
    { get; }

    public int MaximumEventCount { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement>
        Acknowledgements
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
        FinalCursor
    { get; }

    public int EventCount => Acknowledgements.Count;

    public int TotalEventCount => EventCount;

    public bool IsExhausted { get; }

    public bool IsTruncated => !IsExhausted;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraverser
{
    public const int MaximumEventCount =
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
            .MaximumEventCount;

    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
            TryTraverse(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor?
                    cursor,
                int maximumEventCount,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal?
                    traversal)
    {
        traversal = null;
        if (cursor is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .MissingCursor;
        }

        if (maximumEventCount <= 0 || maximumEventCount > MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .InvalidEventLimit;
        }

        if (cursor.NextEventIndex < 0 ||
            cursor.NextEventIndex > cursor.Projection.EventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                    .InvalidCursor;
        }

        var acknowledgements = new List<
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement>(
                Math.Min(maximumEventCount, 4_096));
        var current = cursor;
        while (acknowledgements.Count < maximumEventCount)
        {
            if (current.IsExhausted)
            {
                return Complete(
                    cursor,
                    maximumEventCount,
                    acknowledgements,
                    current,
                    true,
                    out traversal);
            }

            if (PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorFactory
                    .TryAdvance(
                        current,
                        out var packetEvent,
                        out var successor) !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursorAdvanceResult
                    .Advanced ||
                packetEvent is null || successor is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                        .AdvanceFailed;
            }

            if (PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementFactory
                    .TryCreate(
                        current,
                        packetEvent,
                        successor,
                        out var acknowledgement) !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgementResult
                    .Acknowledged ||
                acknowledgement is null)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                        .AcknowledgementFailed;
            }

            acknowledgements.Add(acknowledgement);
            current = successor;
        }

        return Complete(
            cursor,
            maximumEventCount,
            acknowledgements,
            current,
            current.IsExhausted,
            out traversal);
    }

    private static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
            Complete(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                    initialCursor,
                int maximumEventCount,
                List<
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventAcknowledgement>
                    acknowledgements,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventCursor
                    finalCursor,
                bool isExhausted,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversal?
                    traversal)
    {
        traversal = new(
            initialCursor,
            maximumEventCount,
            Array.AsReadOnly(acknowledgements.ToArray()),
            finalCursor,
            isExhausted);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalResult
                .Traversed;
    }
}
