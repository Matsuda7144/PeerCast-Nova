namespace PeerCast.Nova.Playback;

public enum
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
{
    Projected,
    MissingSequenceSetProjection,
    SourceMismatch,
    NoSequencesRequested,
    InvalidVideoStreamIndex,
    InvalidAudioStreamIndex,
    DuplicateStreamIndex,
    VideoMediaKindMismatch,
    AudioMediaKindMismatch,
}

public sealed class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection
{
    internal PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection(
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection source,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest request,
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection selection)
    {
        Source = source;
        Request = request;
        Selection = selection;
    }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection Source
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest Request
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceSelection Selection
    { get; }

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection?
        VideoSource => Request.VideoStreamIndex is { } index
            ? Source.Sources[index]
            : null;

    public PlaybackIsoBmffMovieTrackCodecPacketSequenceProjection?
        AudioSource => Request.AudioStreamIndex is { } index
            ? Source.Sources[index]
            : null;
}

public static class
    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjector
{
    public static
        PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
            Project(
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSetProjection?
                    source,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionRequest
                    request,
                out
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjection?
                        projection)
    {
        projection = null;
        if (source is null)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                    .MissingSequenceSetProjection;
        }

        if (source.Sources.Count != source.SequenceSet.Streams.Count)
        {
            return
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                    .SourceMismatch;
        }

        for (var index = 0; index < source.Sources.Count; index++)
        {
            if (!ReferenceEquals(
                    source.Sources[index].Sequence,
                    source.SequenceSet.Streams[index].Sequence))
            {
                return
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .SourceMismatch;
            }
        }

        var result =
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelector.Select(
                source.SequenceSet,
                request,
                out var selection);
        if (result !=
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .Selected ||
            selection is null)
        {
            return result switch
            {
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .NoSequencesRequested =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .NoSequencesRequested,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .InvalidVideoStreamIndex =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .InvalidVideoStreamIndex,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .InvalidAudioStreamIndex =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .InvalidAudioStreamIndex,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .DuplicateStreamIndex =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .DuplicateStreamIndex,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .VideoMediaKindMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .VideoMediaKindMismatch,
                PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionResult
                    .AudioMediaKindMismatch =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .AudioMediaKindMismatch,
                _ =>
                    PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                        .SourceMismatch,
            };
        }

        projection = new(source, request, selection);
        return
            PlaybackIsoBmffMovieTrackCodecPacketSequenceSelectionProjectionResult
                .Projected;
    }
}
