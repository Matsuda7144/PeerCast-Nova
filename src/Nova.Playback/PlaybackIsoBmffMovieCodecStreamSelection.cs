namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieCodecStreamSelectionResult
{
    Selected,
    MissingIndex,
    NoStreamsRequested,
    InvalidVideoStreamIndex,
    InvalidAudioStreamIndex,
    DuplicateStreamIndex,
    VideoMediaKindMismatch,
    AudioMediaKindMismatch,
}

public readonly record struct PlaybackIsoBmffMovieCodecStreamSelectionRequest(
    int? VideoStreamIndex,
    int? AudioStreamIndex);

public sealed class PlaybackIsoBmffMovieCodecStreamSelection
{
    internal PlaybackIsoBmffMovieCodecStreamSelection(
        PlaybackIsoBmffMovieCodecPacketIndex index,
        PlaybackIsoBmffIndexedMovieTrackCodecPacket? videoStream,
        PlaybackIsoBmffIndexedMovieTrackCodecPacket? audioStream)
    {
        Index = index;
        VideoStream = videoStream;
        AudioStream = audioStream;
    }

    public PlaybackIsoBmffMovieCodecPacketIndex Index { get; }

    public PlaybackIsoBmffIndexedMovieTrackCodecPacket? VideoStream { get; }

    public PlaybackIsoBmffIndexedMovieTrackCodecPacket? AudioStream { get; }

    public bool HasVideo => VideoStream is not null;

    public bool HasAudio => AudioStream is not null;
}

public static class PlaybackIsoBmffMovieCodecStreamSelector
{
    public static PlaybackIsoBmffMovieCodecStreamSelectionResult Select(
        PlaybackIsoBmffMovieCodecPacketIndex? index,
        PlaybackIsoBmffMovieCodecStreamSelectionRequest request,
        out PlaybackIsoBmffMovieCodecStreamSelection? selection)
    {
        selection = null;
        if (index is null)
        {
            return PlaybackIsoBmffMovieCodecStreamSelectionResult.MissingIndex;
        }

        if (request.VideoStreamIndex is null &&
            request.AudioStreamIndex is null)
        {
            return PlaybackIsoBmffMovieCodecStreamSelectionResult
                .NoStreamsRequested;
        }

        PlaybackIsoBmffIndexedMovieTrackCodecPacket? videoStream = null;
        if (request.VideoStreamIndex is { } videoStreamIndex)
        {
            if ((uint)videoStreamIndex >= (uint)index.Streams.Count)
            {
                return PlaybackIsoBmffMovieCodecStreamSelectionResult
                    .InvalidVideoStreamIndex;
            }

            videoStream = index.Streams[videoStreamIndex];
        }

        PlaybackIsoBmffIndexedMovieTrackCodecPacket? audioStream = null;
        if (request.AudioStreamIndex is { } audioStreamIndex)
        {
            if ((uint)audioStreamIndex >= (uint)index.Streams.Count)
            {
                return PlaybackIsoBmffMovieCodecStreamSelectionResult
                    .InvalidAudioStreamIndex;
            }

            audioStream = index.Streams[audioStreamIndex];
        }

        if (videoStream is not null &&
            audioStream is not null &&
            videoStream.StreamIndex == audioStream.StreamIndex)
        {
            return PlaybackIsoBmffMovieCodecStreamSelectionResult
                .DuplicateStreamIndex;
        }

        if (videoStream is not null &&
            videoStream.MediaKind != PlaybackIsoBmffHandlerKind.Video)
        {
            return PlaybackIsoBmffMovieCodecStreamSelectionResult
                .VideoMediaKindMismatch;
        }

        if (audioStream is not null &&
            audioStream.MediaKind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return PlaybackIsoBmffMovieCodecStreamSelectionResult
                .AudioMediaKindMismatch;
        }

        selection = new(index, videoStream, audioStream);
        return PlaybackIsoBmffMovieCodecStreamSelectionResult.Selected;
    }
}
