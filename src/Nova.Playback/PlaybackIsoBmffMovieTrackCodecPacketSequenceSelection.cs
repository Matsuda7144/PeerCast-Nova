namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
{
    Selected,
    MissingSequenceSet,
    NoSequencesRequested,
    InvalidVideoStreamIndex,
    InvalidAudioStreamIndex,
    DuplicateStreamIndex,
    VideoMediaKindMismatch,
    AudioMediaKindMismatch,
}

public readonly record struct
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest(
        int? VideoStreamIndex,
        int? AudioStreamIndex);

public sealed class PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSet sequenceSet,
        PlaybackIsoBmffIndexedTrackCodecPacketSequence? videoStream,
        PlaybackIsoBmffIndexedTrackCodecPacketSequence? audioStream)
    {
        SequenceSet = sequenceSet;
        VideoStream = videoStream;
        AudioStream = audioStream;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSet SequenceSet
    { get; }

    public PlaybackIsoBmffIndexedTrackCodecPacketSequence? VideoStream
    { get; }

    public PlaybackIsoBmffIndexedTrackCodecPacketSequence? AudioStream
    { get; }

    public bool HasVideo => VideoStream is not null;

    public bool HasAudio => AudioStream is not null;
}

public static class PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult Select(
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSet? sequenceSet,
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
                request,
            out PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection?
                selection)
    {
        selection = null;
        if (sequenceSet is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .MissingSequenceSet;
        }

        if (request.VideoStreamIndex is null &&
            request.AudioStreamIndex is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .NoSequencesRequested;
        }

        PlaybackIsoBmffIndexedTrackCodecPacketSequence? videoStream = null;
        if (request.VideoStreamIndex is { } videoStreamIndex)
        {
            if ((uint)videoStreamIndex >= (uint)sequenceSet.Streams.Count)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                        .InvalidVideoStreamIndex;
            }

            videoStream = sequenceSet.Streams[videoStreamIndex];
        }

        PlaybackIsoBmffIndexedTrackCodecPacketSequence? audioStream = null;
        if (request.AudioStreamIndex is { } audioStreamIndex)
        {
            if ((uint)audioStreamIndex >= (uint)sequenceSet.Streams.Count)
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                        .InvalidAudioStreamIndex;
            }

            audioStream = sequenceSet.Streams[audioStreamIndex];
        }

        if (videoStream is not null &&
            audioStream is not null &&
            videoStream.StreamIndex == audioStream.StreamIndex)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .DuplicateStreamIndex;
        }

        if (videoStream is not null &&
            videoStream.MediaKind != PlaybackIsoBmffHandlerKind.Video)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .VideoMediaKindMismatch;
        }

        if (audioStream is not null &&
            audioStream.MediaKind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .AudioMediaKindMismatch;
        }

        selection = new(sequenceSet, videoStream, audioStream);
        return PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
            .Selected;
    }
}
