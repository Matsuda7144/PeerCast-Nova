namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAvcPayloadComposeResult
{
    Composed,
    InvalidConfiguration,
    MissingClassification,
    MissingResolution,
    PrefixPlanFailed,
    PayloadMaterializationFailed,
    PayloadSelectionFailed,
    PayloadLimitExceeded,
}

public sealed record PlaybackIsoBmffAvcPayloadComposerOptions
{
    public int MaximumNalUnitCount { get; init; } =
        PlaybackIsoBmffAvcSampleFramingOptions.DefaultMaximumNalUnitCount;

    public int MaximumPayloadBytes { get; init; } =
        PlaybackBoundedDemuxOptions.DefaultMaximumPacketPayloadBytes;
}

public sealed class PlaybackIsoBmffAvcPayloadComposition
{
    internal PlaybackIsoBmffAvcPayloadComposition(
        PlaybackIsoBmffAvcPayloadRequirementsResolution resolution,
        PlaybackIsoBmffAvcPayloadSelection selection,
        PlaybackIsoBmffAvcParameterSetPrefixPlan? prefixPlan,
        PlaybackIsoBmffAvcLengthPrefixedPayload? prefixedLengthPayload,
        PlaybackIsoBmffAvcSampleAnnexBPayload? sampleAnnexBPayload,
        PlaybackIsoBmffAvcAnnexBPayload? prefixedAnnexBPayload)
    {
        Resolution = resolution;
        Selection = selection;
        PrefixPlan = prefixPlan;
        PrefixedLengthPayload = prefixedLengthPayload;
        SampleAnnexBPayload = sampleAnnexBPayload;
        PrefixedAnnexBPayload = prefixedAnnexBPayload;
    }

    public PlaybackIsoBmffAvcPayloadRequirementsResolution Resolution
    {
        get;
    }

    public PlaybackIsoBmffAvcPayloadSelection Selection { get; }

    public PlaybackIsoBmffAvcParameterSetPrefixPlan? PrefixPlan { get; }

    public PlaybackIsoBmffAvcLengthPrefixedPayload? PrefixedLengthPayload
    {
        get;
    }

    public PlaybackIsoBmffAvcSampleAnnexBPayload? SampleAnnexBPayload { get; }

    public PlaybackIsoBmffAvcAnnexBPayload? PrefixedAnnexBPayload { get; }
}

public static class PlaybackIsoBmffAvcPayloadComposer
{
    public static PlaybackIsoBmffAvcPayloadComposeResult Compose(
        PlaybackIsoBmffAvcNalUnitClassification? classification,
        PlaybackIsoBmffAvcPayloadRequirementsResolution? resolution,
        out PlaybackIsoBmffAvcPayloadComposition? composition,
        PlaybackIsoBmffAvcPayloadComposerOptions? options = null)
    {
        composition = null;
        options ??= new PlaybackIsoBmffAvcPayloadComposerOptions();
        if (options.MaximumNalUnitCount is < 1 or
                > PlaybackIsoBmffAvcSampleFramingOptions
                    .MaximumNalUnitCountHardLimit ||
            options.MaximumPayloadBytes is < 1 or
                > PlaybackBoundedDemuxOptions
                    .MaximumPacketPayloadBytesHardLimit)
        {
            return PlaybackIsoBmffAvcPayloadComposeResult.InvalidConfiguration;
        }

        if (classification is null)
        {
            return PlaybackIsoBmffAvcPayloadComposeResult.MissingClassification;
        }

        if (resolution is null)
        {
            return PlaybackIsoBmffAvcPayloadComposeResult.MissingResolution;
        }

        PlaybackIsoBmffAvcParameterSetPrefixPlan? prefixPlan = null;
        PlaybackIsoBmffAvcLengthPrefixedPayload? prefixedLengthPayload = null;
        PlaybackIsoBmffAvcSampleAnnexBPayload? sampleAnnexBPayload = null;
        PlaybackIsoBmffAvcAnnexBPayload? prefixedAnnexBPayload = null;
        var materializerOptions = new PlaybackIsoBmffAvcPayloadMaterializerOptions
        {
            MaximumPayloadBytes = options.MaximumPayloadBytes,
        };

        if (resolution.Format is PlaybackIsoBmffAvcPayloadFormat
                .ParameterSetPrefixedLengthPrefixed or
            PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedAnnexB)
        {
            var planResult =
                PlaybackIsoBmffAvcParameterSetPrefixPlanBuilder.Build(
                    classification,
                    out prefixPlan,
                    new()
                    {
                        MaximumNalUnitCount = options.MaximumNalUnitCount,
                        MaximumPayloadBytes = options.MaximumPayloadBytes,
                    });
            if (planResult !=
                PlaybackIsoBmffAvcParameterSetPrefixPlanResult.Planned)
            {
                return PlaybackIsoBmffAvcPayloadComposeResult.PrefixPlanFailed;
            }
        }

        switch (resolution.Format)
        {
            case PlaybackIsoBmffAvcPayloadFormat.OriginalLengthPrefixed:
                break;

            case PlaybackIsoBmffAvcPayloadFormat
                .ParameterSetPrefixedLengthPrefixed:
                if (PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializer
                        .Materialize(
                            prefixPlan,
                            out prefixedLengthPayload,
                            materializerOptions) !=
                    PlaybackIsoBmffAvcLengthPrefixedPayloadMaterializeResult
                        .Materialized)
                {
                    return PlaybackIsoBmffAvcPayloadComposeResult
                        .PayloadMaterializationFailed;
                }

                break;

            case PlaybackIsoBmffAvcPayloadFormat.SampleOnlyAnnexB:
                if (PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializer
                        .Materialize(
                            classification,
                            out sampleAnnexBPayload,
                            materializerOptions) !=
                    PlaybackIsoBmffAvcSampleAnnexBPayloadMaterializeResult
                        .Materialized)
                {
                    return PlaybackIsoBmffAvcPayloadComposeResult
                        .PayloadMaterializationFailed;
                }

                break;

            case PlaybackIsoBmffAvcPayloadFormat.ParameterSetPrefixedAnnexB:
                if (PlaybackIsoBmffAvcAnnexBPayloadMaterializer.Materialize(
                        prefixPlan,
                        out prefixedAnnexBPayload,
                        materializerOptions) !=
                    PlaybackIsoBmffAvcAnnexBPayloadMaterializeResult
                        .Materialized)
                {
                    return PlaybackIsoBmffAvcPayloadComposeResult
                        .PayloadMaterializationFailed;
                }

                break;

            default:
                return PlaybackIsoBmffAvcPayloadComposeResult
                    .PayloadSelectionFailed;
        }

        if (PlaybackIsoBmffAvcPayloadSelector.Select(
                classification,
                resolution.Format,
                out var selection,
                prefixedLengthPayload,
                sampleAnnexBPayload,
                prefixedAnnexBPayload) !=
            PlaybackIsoBmffAvcPayloadSelectionResult.Selected)
        {
            return PlaybackIsoBmffAvcPayloadComposeResult
                .PayloadSelectionFailed;
        }

        if (selection!.Payload.Length > options.MaximumPayloadBytes)
        {
            return PlaybackIsoBmffAvcPayloadComposeResult.PayloadLimitExceeded;
        }

        composition = new(
            resolution,
            selection,
            prefixPlan,
            prefixedLengthPayload,
            sampleAnnexBPayload,
            prefixedAnnexBPayload);
        return PlaybackIsoBmffAvcPayloadComposeResult.Composed;
    }
}
