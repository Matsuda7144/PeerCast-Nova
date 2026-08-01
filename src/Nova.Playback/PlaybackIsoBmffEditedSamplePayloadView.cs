namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffEditedSamplePayloadViewResult
{
    Found,
    MissingMediaData,
    SampleOutsideFile,
    MediaDataLocationMismatch,
}

public static class PlaybackIsoBmffEditedSamplePayloadView
{
    public static PlaybackIsoBmffEditedSamplePayloadViewResult TryGet(
        ReadOnlySpan<byte> file,
        PlaybackIsoBmffEditedSampleMediaData? mediaData,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (mediaData is null)
        {
            return PlaybackIsoBmffEditedSamplePayloadViewResult
                .MissingMediaData;
        }

        if (PlaybackIsoBmffSampleFileBoundsValidator.Validate(
                mediaData.Range,
                (ulong)file.Length) !=
            PlaybackIsoBmffSampleFileBoundsValidationResult.InBounds)
        {
            return PlaybackIsoBmffEditedSamplePayloadViewResult
                .SampleOutsideFile;
        }

        var recordedBox = mediaData.MediaDataBox;
        if (PlaybackIsoBmffBoxSequenceReader.ReadNext(
                file,
                recordedBox.Offset,
                out var actualBox) !=
                PlaybackIsoBmffBoxSequenceReadResult.BoxAvailable ||
            actualBox is null ||
            actualBox.Offset != recordedBox.Offset ||
            actualBox.BoxLength != recordedBox.BoxLength ||
            actualBox.Header.Type != recordedBox.Header.Type ||
            actualBox.Header.HeaderSize != recordedBox.Header.HeaderSize ||
            PlaybackIsoBmffSampleMediaDataBoundsValidator.Validate(
                mediaData.Range,
                actualBox,
                containerOffset: 0) !=
                PlaybackIsoBmffSampleMediaDataBoundsValidationResult.Contained)
        {
            return PlaybackIsoBmffEditedSamplePayloadViewResult
                .MediaDataLocationMismatch;
        }

        var offset = checked((int)mediaData.Range.Offset);
        var length = checked((int)mediaData.Range.Size);
        payload = file.Slice(offset, length);
        return PlaybackIsoBmffEditedSamplePayloadViewResult.Found;
    }
}
