namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffOwnedEditedSamplePayloadResult
{
    Copied,
    InvalidConfiguration,
    MissingMediaData,
    SampleOutsideFile,
    MediaDataLocationMismatch,
    EmptyPayload,
    PayloadLimitExceeded,
}

public sealed record PlaybackIsoBmffOwnedEditedSamplePayloadOptions
{
    public int MaximumPayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes;
}

public sealed class PlaybackIsoBmffOwnedEditedSamplePayload
{
    private readonly byte[] _payload;

    internal PlaybackIsoBmffOwnedEditedSamplePayload(
        PlaybackIsoBmffEditedSampleMediaData mediaData,
        byte[] payload)
    {
        MediaData = mediaData;
        _payload = payload;
    }

    public PlaybackIsoBmffEditedSampleMediaData MediaData { get; }

    public ulong SampleNumber => MediaData.SampleNumber;

    public ReadOnlyMemory<byte> Payload => _payload;
}

public static class PlaybackIsoBmffOwnedEditedSamplePayloadCopy
{
    public static PlaybackIsoBmffOwnedEditedSamplePayloadResult Copy(
        ReadOnlySpan<byte> file,
        PlaybackIsoBmffEditedSampleMediaData? mediaData,
        out PlaybackIsoBmffOwnedEditedSamplePayload? ownedPayload,
        PlaybackIsoBmffOwnedEditedSamplePayloadOptions? options = null)
    {
        ownedPayload = null;
        options ??= new PlaybackIsoBmffOwnedEditedSamplePayloadOptions();
        if (options.MaximumPayloadBytes is < 1 or
            > PlaybackBoundedDemuxOptions.MaximumPacketPayloadBytesHardLimit)
        {
            return PlaybackIsoBmffOwnedEditedSamplePayloadResult
                .InvalidConfiguration;
        }

        var viewResult = PlaybackIsoBmffEditedSamplePayloadView.TryGet(
            file,
            mediaData,
            out var payload);
        if (viewResult != PlaybackIsoBmffEditedSamplePayloadViewResult.Found ||
            mediaData is null)
        {
            return viewResult switch
            {
                PlaybackIsoBmffEditedSamplePayloadViewResult
                    .MissingMediaData =>
                    PlaybackIsoBmffOwnedEditedSamplePayloadResult
                        .MissingMediaData,
                PlaybackIsoBmffEditedSamplePayloadViewResult.SampleOutsideFile =>
                    PlaybackIsoBmffOwnedEditedSamplePayloadResult
                        .SampleOutsideFile,
                _ => PlaybackIsoBmffOwnedEditedSamplePayloadResult
                    .MediaDataLocationMismatch,
            };
        }

        if (payload.IsEmpty)
        {
            return PlaybackIsoBmffOwnedEditedSamplePayloadResult.EmptyPayload;
        }

        if (payload.Length > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffOwnedEditedSamplePayloadResult
                .PayloadLimitExceeded;
        }

        ownedPayload = new(mediaData, payload.ToArray());
        return PlaybackIsoBmffOwnedEditedSamplePayloadResult.Copied;
    }
}
