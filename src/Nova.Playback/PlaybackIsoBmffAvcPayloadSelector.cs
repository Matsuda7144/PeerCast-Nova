namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcPayloadFormat
{
    OriginalLengthPrefixed,
    ParameterSetPrefixedLengthPrefixed,
    SampleOnlyAnnexB,
    ParameterSetPrefixedAnnexB,
}

public enum PlaybackIsoBmffAvcPayloadSelectionResult
{
    Selected,
    MissingClassification,
    UnsupportedFormat,
    MissingPayload,
    SourceMismatch,
}

public sealed class PlaybackIsoBmffAvcPayloadSelection
{
    internal PlaybackIsoBmffAvcPayloadSelection(
        PlaybackIsoBmffAvcNalUnitClassification classification,
        PlaybackIsoBmffAvcPayloadFormat format,
        bool includesConfigurationParameterSets,
        bool annexB,
        ReadOnlyMemory<byte> payload)
    {
        Classification = classification;
        Format = format;
        IncludesConfigurationParameterSets = includesConfigurationParameterSets;
        IsAnnexB = annexB;
        Payload = payload;
    }

    public PlaybackIsoBmffAvcNalUnitClassification Classification { get; }

    public PlaybackIsoBmffAvcPayloadFormat Format { get; }

    public bool IncludesConfigurationParameterSets { get; }

    public bool IsAnnexB { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}

public static class PlaybackIsoBmffAvcPayloadSelector
{
    public static PlaybackIsoBmffAvcPayloadSelectionResult Select(
        PlaybackIsoBmffAvcNalUnitClassification? classification,
        PlaybackIsoBmffAvcPayloadFormat format,
        out PlaybackIsoBmffAvcPayloadSelection? selection,
        PlaybackIsoBmffAvcLengthPrefixedPayload? prefixedLengthPayload = null,
        PlaybackIsoBmffAvcSampleAnnexBPayload? sampleAnnexBPayload = null,
        PlaybackIsoBmffAvcAnnexBPayload? prefixedAnnexBPayload = null)
    {
        selection = null;
        if (classification is null)
        {
            return PlaybackIsoBmffAvcPayloadSelectionResult
                .MissingClassification;
        }

        ReadOnlyMemory<byte> payload;
        var includesConfigurationParameterSets = false;
        var annexB = false;
        switch (format)
        {
            case PlaybackIsoBmffAvcPayloadFormat.OriginalLengthPrefixed:
                payload = classification.Frame.Candidate.Payload;
                break;

            case PlaybackIsoBmffAvcPayloadFormat
                .ParameterSetPrefixedLengthPrefixed:
                if (prefixedLengthPayload is null)
                {
                    return PlaybackIsoBmffAvcPayloadSelectionResult
                        .MissingPayload;
                }

                if (!ReferenceEquals(
                        prefixedLengthPayload.Plan.Classification,
                        classification))
                {
                    return PlaybackIsoBmffAvcPayloadSelectionResult
                        .SourceMismatch;
                }

                payload = prefixedLengthPayload.Payload;
                includesConfigurationParameterSets = true;
                break;

            case PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB:
                if (sampleAnnexBPayload is null)
                {
                    return PlaybackIsoBmffAvcPayloadSelectionResult
                        .MissingPayload;
                }

                if (!ReferenceEquals(
                        sampleAnnexBPayload.Classification,
                        classification))
                {
                    return PlaybackIsoBmffAvcPayloadSelectionResult
                        .SourceMismatch;
                }

                payload = sampleAnnexBPayload.Payload;
                annexB = true;
                break;

            case PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedAnnexB:
                if (prefixedAnnexBPayload is null)
                {
                    return PlaybackIsoBmffAvcPayloadSelectionResult
                        .MissingPayload;
                }

                if (!ReferenceEquals(
                        prefixedAnnexBPayload.Plan.Classification,
                        classification))
                {
                    return PlaybackIsoBmffAvcPayloadSelectionResult
                        .SourceMismatch;
                }

                payload = prefixedAnnexBPayload.Payload;
                includesConfigurationParameterSets = true;
                annexB = true;
                break;

            default:
                return PlaybackIsoBmffAvcPayloadSelectionResult
                    .UnsupportedFormat;
        }

        if (payload.IsEmpty)
        {
            return PlaybackIsoBmffAvcPayloadSelectionResult.MissingPayload;
        }

        selection = new(
            classification,
            format,
            includesConfigurationParameterSets,
            annexB,
            payload);
        return PlaybackIsoBmffAvcPayloadSelectionResult.Selected;
    }
}
