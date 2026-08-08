namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffAacSampleEntryConsistencyPolicy
{
    RequireExactAgreement,
    AllowEffectiveDecoderFacts,
}

public enum PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult
{
    Admitted,
    MissingFacts,
    UndefinedPolicy,
    MismatchRejected,
}

public sealed class PlaybackIsoBmffAacSampleEntryConsistencyDecision
{
    internal PlaybackIsoBmffAacSampleEntryConsistencyDecision(
        PlaybackIsoBmffAacSampleEntryConsistencyFacts facts,
        PlaybackIsoBmffAacSampleEntryConsistencyPolicy policy)
    {
        Facts = facts;
        Policy = policy;
    }

    public PlaybackIsoBmffAacSampleEntryConsistencyFacts Facts { get; }

    public PlaybackIsoBmffAacSampleEntryConsistencyPolicy Policy { get; }

    public uint DecoderSamplingFrequency => Facts.EffectiveSamplingFrequency;

    public int DecoderChannelCount => Facts.EffectiveChannelCount;

    public bool UsedMismatchOverride => !Facts.IsExactMatch;
}

public static class PlaybackIsoBmffAacSampleEntryConsistencyDecisionResolver
{
    public static PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult Resolve(
        PlaybackIsoBmffAacSampleEntryConsistencyFacts? facts,
        PlaybackIsoBmffAacSampleEntryConsistencyPolicy policy,
        out PlaybackIsoBmffAacSampleEntryConsistencyDecision? decision)
    {
        decision = null;
        if (facts is null)
        {
            return PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult
                .MissingFacts;
        }

        if (!Enum.IsDefined(policy))
        {
            return PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult
                .UndefinedPolicy;
        }

        if (!facts.IsExactMatch &&
            policy == PlaybackIsoBmffAacSampleEntryConsistencyPolicy
                .RequireExactAgreement)
        {
            return PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult
                .MismatchRejected;
        }

        decision = new(facts, policy);
        return PlaybackIsoBmffAacSampleEntryConsistencyDecisionResult.Admitted;
    }
}
