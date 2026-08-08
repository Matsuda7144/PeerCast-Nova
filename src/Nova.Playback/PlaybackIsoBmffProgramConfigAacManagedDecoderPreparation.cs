namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
{
    Prepared,
    MissingReorderPlan,
    MissingFormatIntent,
    TopologySourceMismatch,
    DecoderPreparationRejected,
}

public sealed class PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation(
        PlaybackIsoBmffProgramConfigChannelReorderPlan reorderPlan,
        PlaybackIsoBmffAacManagedDecoderPreparation decoderPreparation)
    {
        ReorderPlan = reorderPlan;
        DecoderPreparation = decoderPreparation;
    }

    public PlaybackIsoBmffProgramConfigChannelReorderPlan ReorderPlan { get; }

    public PlaybackIsoBmffAacManagedDecoderPreparation DecoderPreparation
    {
        get;
    }

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent =>
        DecoderPreparation.FormatIntent;

    public void Dispose() => DecoderPreparation.Dispose();

    public ValueTask DisposeAsync() => DecoderPreparation.DisposeAsync();
}

public static class
    PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationComposer
{
    public static
        PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult Compose(
            PlaybackIsoBmffProgramConfigChannelReorderPlan? reorderPlan,
            PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
            IReadOnlyCollection<string>? declaredSymbols,
            PlaybackIsoBmffAacDecoderCapabilityDeclaration capabilityDeclaration,
            out PlaybackIsoBmffProgramConfigAacManagedDecoderPreparation?
                preparation,
            PlaybackIsoBmffAacDecoderExtradataMaterializerOptions?
                extradataOptions = null)
    {
        preparation = null;
        if (reorderPlan is null)
        {
            return PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .MissingReorderPlan;
        }

        if (formatIntent is null)
        {
            return PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .MissingFormatIntent;
        }

        var formatTopology = formatIntent.ExtensionSignallingFacts.CoreFormatFacts
            .ProgramConfigTopology;
        var planTopology = reorderPlan.Layout.Assignment.SlotMap.Topology;
        if (!ReferenceEquals(formatTopology, planTopology))
        {
            return PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .TopologySourceMismatch;
        }

        if (PlaybackIsoBmffAacManagedDecoderPreparationComposer.Compose(
                formatIntent,
                declaredSymbols,
                capabilityDeclaration,
                out var decoderPreparation,
                extradataOptions) !=
            PlaybackIsoBmffAacManagedDecoderPreparationResult.Prepared ||
            decoderPreparation is null)
        {
            decoderPreparation?.Dispose();
            return PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
                .DecoderPreparationRejected;
        }

        preparation = new(reorderPlan, decoderPreparation);
        return PlaybackIsoBmffProgramConfigAacManagedDecoderPreparationResult
            .Prepared;
    }
}
