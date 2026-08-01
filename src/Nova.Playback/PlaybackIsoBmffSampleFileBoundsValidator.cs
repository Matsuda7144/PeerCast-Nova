namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffSampleFileBoundsValidationResult
{
    InBounds,
    InvalidRange,
    OutsideFile,
}

public static class PlaybackIsoBmffSampleFileBoundsValidator
{
    public static PlaybackIsoBmffSampleFileBoundsValidationResult Validate(
        PlaybackIsoBmffSampleByteRange? range,
        ulong fileLength)
    {
        if (range is null ||
            range.Value.Offset > ulong.MaxValue - range.Value.Size ||
            range.Value.EndOffsetExclusive !=
                range.Value.Offset + range.Value.Size)
        {
            return PlaybackIsoBmffSampleFileBoundsValidationResult.InvalidRange;
        }

        return range.Value.EndOffsetExclusive <= fileLength
            ? PlaybackIsoBmffSampleFileBoundsValidationResult.InBounds
            : PlaybackIsoBmffSampleFileBoundsValidationResult.OutsideFile;
    }
}
