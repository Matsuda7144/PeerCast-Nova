namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
{
    Prepared,
    ConsistencyFactsRejected,
    ConsistencyDecisionRejected,
    DecoderPreparationRejected,
}

public sealed class PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation :
    IDisposable,
    IAsyncDisposable
{
    internal PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation(
        PlaybackIsoBmffAacSampleEntryConsistencyDecision consistencyDecision,
        PlaybackIsoBmffAacManagedDecoderPreparation decoderPreparation)
    {
        ConsistencyDecision = consistencyDecision;
        DecoderPreparation = decoderPreparation;
    }

    public PlaybackIsoBmffAacSampleEntryConsistencyDecision ConsistencyDecision
    {
        get;
    }

    public PlaybackIsoBmffAacManagedDecoderPreparation DecoderPreparation
    {
        get;
    }

    public PlaybackIsoBmffAudioSampleEntry AudioEntry =>
        ConsistencyDecision.Facts.AudioEntry;

    public PlaybackIsoBmffAacDecoderFormatIntent FormatIntent =>
        DecoderPreparation.FormatIntent;

    public void Dispose() => DecoderPreparation.Dispose();

    public ValueTask DisposeAsync() => DecoderPreparation.DisposeAsync();
}

public static class PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationComposer
{
    public static PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
        Compose(
            PlaybackIsoBmffAudioSampleEntry? audioEntry,
            PlaybackIsoBmffAacDecoderFormatIntent? formatIntent,
            PlaybackIsoBmffAacSampleEntryConsistencyPolicy consistencyPolicy,
            IReadOnlyCollection<string>? declaredSymbols,
            PlaybackIsoBmffAacDecoderCapabilityDeclaration capabilityDeclaration,
            out PlaybackIsoBmffAacSampleEntryManagedDecoderPreparation? preparation,
            PlaybackIsoBmffAacDecoderExtradataMaterializerOptions?
                extradataOptions = null)
    {
        preparation = null;
        if (PlaybackIsoBmffAacSampleEntryConsistencyFactsProjector.Project(
                audioEntry, formatIntent, out var consistencyFacts) !=
            PlaybackIsoBmffAacSampleEntryConsistencyFactsResult.Projected ||
            consistencyFacts is null)
        {
            return PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
                .ConsistencyFactsRejected;
        }

        if (PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver.Resolve(
                consistencyFacts,
                consistencyPolicy,
                out var consistencyDecision) !=
            PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult.Admitted ||
            consistencyDecision is null)
        {
            return PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
                .ConsistencyDecisionRejected;
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
            return PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult
                .DecoderPreparationRejected;
        }

        preparation = new(consistencyDecision, decoderPreparation);
        return PlaybackIsoBmffAacSampleEntryManagedDecoderPreparationResult.Prepared;
    }
}
