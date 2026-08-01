namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleMediaDataBoundsValidationResult
{
    Contained,
    InvalidSampleRange,
    InvalidMediaDataBox,
    OffsetOverflow,
    OutsideMediaData,
}

public static class PlaybackIsoBmffSampleMediaDataBoundsValidator
{
    private const uint MediaDataType = 0x6D646174;

    public static PlaybackIsoBmffSampleMediaDataBoundsValidationResult Validate(
        PlaybackIsoBmffSampleByteRange? range,
        PlaybackIsoBmffBoxLocation? mediaDataBox,
        ulong containerOffset)
    {
        if (PlaybackIsoBmffSampleFileBoundsValidator.Validate(
                range,
                ulong.MaxValue) ==
            PlaybackIsoBmffSampleFileBoundsValidationResult.InvalidRange)
        {
            return PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .InvalidSampleRange;
        }

        if (mediaDataBox is null ||
            mediaDataBox.Header.Type != MediaDataType)
        {
            return PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .InvalidMediaDataBox;
        }

        var relativePayloadOffset = (ulong)mediaDataBox.PayloadOffset;
        var payloadLength = (ulong)mediaDataBox.PayloadLength;
        if (containerOffset > ulong.MaxValue - relativePayloadOffset)
        {
            return PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .OffsetOverflow;
        }

        var payloadOffset = containerOffset + relativePayloadOffset;
        if (payloadOffset > ulong.MaxValue - payloadLength)
        {
            return PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .OffsetOverflow;
        }

        var payloadEndOffsetExclusive = payloadOffset + payloadLength;
        return range!.Value.Offset >= payloadOffset &&
            range.Value.EndOffsetExclusive <= payloadEndOffsetExclusive
            ? PlaybackIsoBmffSampleMediaDataBoundsValidationResult.Contained
            : PlaybackIsoBmffSampleMediaDataBoundsValidationResult
                .OutsideMediaData;
    }
}
