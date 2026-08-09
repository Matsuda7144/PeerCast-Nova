using System.Collections.ObjectModel;

namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecPacketEventProjectionResult
{
    Projected,
    MissingSelection,
    StreamSourceMismatch,
    MissingAppearance,
    AppearanceLimitExceeded,
    InvalidAppearance,
}

public sealed class PlaybackIsoBmffMovieCodecPacketEvent
{
    internal PlaybackIsoBmffMovieCodecPacketEvent(
        PlaybackIsoBmffIndexedMovieTrackCodecPacket stream,
        int appearanceIndex,
        PlaybackIsoBmffSampleEditMovieOverlap appearance)
    {
        Stream = stream;
        AppearanceIndex = appearanceIndex;
        Appearance = appearance;
    }

    public PlaybackIsoBmffIndexedMovieTrackCodecPacket Stream { get; }

    public int AppearanceIndex { get; }

    public PlaybackIsoBmffSampleEditMovieOverlap Appearance { get; }

    public int StreamIndex => Stream.StreamIndex;

    public uint TrackId => Stream.TrackId;

    public PlaybackIsoBmffHandlerKind MediaKind => Stream.MediaKind;

    public PlaybackIsoBmffCodecPacketProjection Packet =>
        Stream.Packet.TrackPacket.Packet;

    public Int128 MovieStartNumerator => Appearance.MovieStartNumerator;

    public Int128 MovieEndNumeratorExclusive =>
        Appearance.MovieEndNumeratorExclusive;

    public uint Denominator => Appearance.Denominator;

    public uint MovieTimescale => Stream.Packet.MovieTimescale;
}

public sealed class PlaybackIsoBmffMovieCodecPacketEventProjection
{
    internal PlaybackIsoBmffMovieCodecPacketEventProjection(
        PlaybackIsoBmffMovieCodecStreamSelection selection,
        ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEvent> videoEvents,
        ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEvent> audioEvents)
    {
        Selection = selection;
        VideoEvents = videoEvents;
        AudioEvents = audioEvents;
    }

    public PlaybackIsoBmffMovieCodecStreamSelection Selection { get; }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEvent> VideoEvents
    {
        get;
    }

    public IReadOnlyList<PlaybackIsoBmffMovieCodecPacketEvent> AudioEvents
    {
        get;
    }

    public int EventCount => VideoEvents.Count + AudioEvents.Count;
}

public static class PlaybackIsoBmffMovieCodecPacketEventProjector
{
    public const int MaximumAppearancesPerStream =
        PlaybackIsoBmffSampleEditOverlapLookupOptions
            .MaximumOverlapCountHardLimit;
    public const int MaximumEventCount = MaximumAppearancesPerStream * 2;

    public static PlaybackIsoBmffMovieCodecPacketEventProjectionResult Project(
        PlaybackIsoBmffMovieCodecStreamSelection? selection,
        out PlaybackIsoBmffMovieCodecPacketEventProjection? projection)
    {
        projection = null;
        if (selection is null)
        {
            return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .MissingSelection;
        }

        if (!IsExactSelectedStream(
                selection.Index,
                selection.VideoStream) ||
            !IsExactSelectedStream(
                selection.Index,
                selection.AudioStream))
        {
            return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .StreamSourceMismatch;
        }

        var videoResult = ProjectStream(
            selection.VideoStream,
            out var videoEvents);
        if (videoResult !=
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected)
        {
            return videoResult;
        }

        var audioResult = ProjectStream(
            selection.AudioStream,
            out var audioEvents);
        if (audioResult !=
            PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected)
        {
            return audioResult;
        }

        if (videoEvents.Count + audioEvents.Count > MaximumEventCount)
        {
            return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .AppearanceLimitExceeded;
        }

        projection = new(selection, videoEvents, audioEvents);
        return PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected;
    }

    private static PlaybackIsoBmffMovieCodecPacketEventProjectionResult
        ProjectStream(
            PlaybackIsoBmffIndexedMovieTrackCodecPacket? stream,
            out ReadOnlyCollection<PlaybackIsoBmffMovieCodecPacketEvent>
                events)
    {
        if (stream is null)
        {
            events = Array.AsReadOnly(
                Array.Empty<PlaybackIsoBmffMovieCodecPacketEvent>());
            return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .Projected;
        }

        var appearances = stream.Packet.Appearances;
        if (appearances.Count == 0)
        {
            events = null!;
            return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .MissingAppearance;
        }

        if (appearances.Count > MaximumAppearancesPerStream)
        {
            events = null!;
            return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                .AppearanceLimitExceeded;
        }

        var projected = new PlaybackIsoBmffMovieCodecPacketEvent[
            appearances.Count];
        for (var appearanceIndex = 0;
            appearanceIndex < appearances.Count;
            appearanceIndex++)
        {
            var appearance = appearances[appearanceIndex];
            if (appearance.Denominator != stream.Packet.MediaTimescale ||
                appearance.MovieStartNumerator < 0 ||
                appearance.MovieStartNumerator >=
                    appearance.MovieEndNumeratorExclusive ||
                !appearance.ContainsCompleteSample)
            {
                events = null!;
                return PlaybackIsoBmffMovieCodecPacketEventProjectionResult
                    .InvalidAppearance;
            }

            projected[appearanceIndex] = new(
                stream,
                appearanceIndex,
                appearance);
        }

        events = Array.AsReadOnly(projected);
        return PlaybackIsoBmffMovieCodecPacketEventProjectionResult.Projected;
    }

    private static bool IsExactSelectedStream(
        PlaybackIsoBmffMovieCodecPacketIndex index,
        PlaybackIsoBmffIndexedMovieTrackCodecPacket? stream) =>
        stream is null ||
        ((uint)stream.StreamIndex < (uint)index.Streams.Count &&
            ReferenceEquals(index.Streams[stream.StreamIndex], stream));
}
