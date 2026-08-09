using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
{
    Projected,
    MissingAcknowledgementView,
    EventLimitExceeded,
    InvalidMovieTimescale,
    MovieTimescaleMismatch,
    InvalidEvent,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement
                acknowledgement,
            uint movieTimescale,
            ulong secondsDenominator)
    {
        Acknowledgement = acknowledgement;
        MovieTimescale = movieTimescale;
        SecondsDenominator = secondsDenominator;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgement
            Acknowledgement
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEvent Event
        => Acknowledgement.Event;

    public uint MovieTimescale { get; }

    public Int128 StartSecondsNumerator => Event.MovieStartNumerator;

    public Int128 EndSecondsNumeratorExclusive =>
        Event.MovieEndNumeratorExclusive;

    public Int128 DurationSecondsNumerator =>
        EndSecondsNumeratorExclusive - StartSecondsNumerator;

    public ulong SecondsDenominator { get; }
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection
{
    internal
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView
                acknowledgementView,
            ReadOnlyCollection<
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime>
                events)
    {
        AcknowledgementView = acknowledgementView;
        Events = events;
    }

    public
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView
            AcknowledgementView
    { get; }

    public IReadOnlyList<
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime>
        Events
    { get; }

    public int EventCount => Events.Count;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
            TryProject(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalAcknowledgementView?
                    acknowledgementView,
                out PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjection?
                    projection)
    {
        projection = null;
        if (acknowledgementView is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .MissingAcknowledgementView;
        }

        if (acknowledgementView.EventCount >
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventProjector
                .MaximumEventCount)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .EventLimitExceeded;
        }

        var movieTimescale = acknowledgementView.Completion.InitialCursor
            .Projection.StepView.Completion.InitialCursorSet.Selection
            .SequenceSet.MovieTiming.Timescale;
        if (movieTimescale == 0)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                    .InvalidMovieTimescale;
        }

        var events = new
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTime[
                acknowledgementView.EventCount];
        for (var index = 0; index < acknowledgementView.EventCount; index++)
        {
            var acknowledgement = acknowledgementView.Acknowledgements[index];
            var packetEvent = acknowledgement.Event;
            if (packetEvent.Packet.MovieTimescale != movieTimescale)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                        .MovieTimescaleMismatch;
            }

            if (packetEvent.Denominator == 0 ||
                packetEvent.MovieStartNumerator < 0 ||
                packetEvent.MovieStartNumerator >=
                    packetEvent.MovieEndNumeratorExclusive)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                        .InvalidEvent;
            }

            events[index] = new(
                acknowledgement,
                movieTimescale,
                (ulong)packetEvent.Denominator * movieTimescale);
        }

        projection = new(acknowledgementView, Array.AsReadOnly(events));
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceCompletedMergeEventTraversalTimeProjectionResult
                .Projected;
    }
}
