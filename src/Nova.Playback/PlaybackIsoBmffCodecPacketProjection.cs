namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffCodecPacketProjectionResult
{
    Projected,
    MissingAvcComposition,
    MissingCandidate,
    MissingAudioEntry,
    UnsupportedCodec,
    SampleDescriptionMismatch,
    EmptyPayload,
}

public sealed class PlaybackIsoBmffCodecPacketProjection
{
    internal PlaybackIsoBmffCodecPacketProjection(
        PlaybackIsoBmffEditedSamplePacketCandidate candidate,
        PlaybackIsoBmffEditedSampleDescriptionSelection descriptionSelection,
        ReadOnlyMemory<byte> payload,
        PlaybackIsoBmffAvcPayloadComposition? avcPayloadComposition,
        PlaybackIsoBmffAudioSampleEntry? audioEntry)
    {
        Candidate = candidate;
        DescriptionSelection = descriptionSelection;
        Payload = payload;
        AvcPayloadComposition = avcPayloadComposition;
        AudioEntry = audioEntry;
    }

    public PlaybackIsoBmffEditedSamplePacketCandidate Candidate { get; }

    public PlaybackIsoBmffEditedSampleDescriptionSelection DescriptionSelection
    {
        get;
    }

    public PlaybackIsoBmffSampleEntryCodecCandidate CodecCandidate =>
        DescriptionSelection.CodecCandidate;

    public PlaybackIsoBmffHandlerKind MediaKind =>
        DescriptionSelection.MediaKind;

    public ReadOnlyMemory<byte> Payload { get; }

    public PlaybackIsoBmffAvcPayloadComposition? AvcPayloadComposition { get; }

    public PlaybackIsoBmffAudioSampleEntry? AudioEntry { get; }
}

public static class PlaybackIsoBmffCodecPacketProjector
{
    public static PlaybackIsoBmffCodecPacketProjectionResult ProjectAvc(
        PlaybackIsoBmffAvcPayloadComposition? avcPayloadComposition,
        out PlaybackIsoBmffCodecPacketProjection? projection)
    {
        projection = null;
        if (avcPayloadComposition is null)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult
                .MissingAvcComposition;
        }

        var payloadSelection = avcPayloadComposition.Selection;
        var frame = payloadSelection.Classification.Frame;
        var candidate = frame.Candidate;
        var descriptionSelection =
            frame.DecoderConfiguration.VisualEntry.Selection;
        if (descriptionSelection.CodecCandidate is not
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc1 and not
                PlaybackIsoBmffSampleEntryCodecCandidate.Avc3 ||
            descriptionSelection.MediaKind != PlaybackIsoBmffHandlerKind.Video)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult.UnsupportedCodec;
        }

        if (candidate.OwnedPayload.MediaData.EditedRange
                .SampleDescriptionIndex !=
            descriptionSelection.SampleDescriptionIndex)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult
                .SampleDescriptionMismatch;
        }

        if (payloadSelection.Payload.IsEmpty)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult.EmptyPayload;
        }

        projection = new(
            candidate,
            descriptionSelection,
            payloadSelection.Payload,
            avcPayloadComposition,
            null);
        return PlaybackIsoBmffCodecPacketProjectionResult.Projected;
    }

    public static PlaybackIsoBmffCodecPacketProjectionResult ProjectAac(
        PlaybackIsoBmffEditedSamplePacketCandidate? candidate,
        PlaybackIsoBmffAudioSampleEntry? audioEntry,
        out PlaybackIsoBmffCodecPacketProjection? projection)
    {
        projection = null;
        if (candidate is null)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult.MissingCandidate;
        }

        if (audioEntry is null)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult.MissingAudioEntry;
        }

        var descriptionSelection = audioEntry.Selection;
        if (descriptionSelection.CodecCandidate !=
                PlaybackIsoBmffSampleEntryCodecCandidate.Mp4a ||
            descriptionSelection.MediaKind != PlaybackIsoBmffHandlerKind.Audio)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult.UnsupportedCodec;
        }

        if (candidate.OwnedPayload.MediaData.EditedRange
                .SampleDescriptionIndex !=
            descriptionSelection.SampleDescriptionIndex)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult
                .SampleDescriptionMismatch;
        }

        if (candidate.Payload.IsEmpty)
        {
            return PlaybackIsoBmffCodecPacketProjectionResult.EmptyPayload;
        }

        projection = new(
            candidate,
            descriptionSelection,
            candidate.Payload,
            null,
            audioEntry);
        return PlaybackIsoBmffCodecPacketProjectionResult.Projected;
    }
}
