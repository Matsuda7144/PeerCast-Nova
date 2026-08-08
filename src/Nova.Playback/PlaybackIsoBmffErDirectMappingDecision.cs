namespace PeerCast.Nova.Playback;

public enum PlaybackIsoBmffErDirectMappingPolicy
{
    RequireEnabled,
}

public enum PlaybackIsoBmffErDirectMappingDecisionResult
{
    Admitted,
    MissingMapping,
    UndefinedPolicy,
    DisabledRejected,
}

public sealed class PlaybackIsoBmffErDirectMappingDecision
{
    internal PlaybackIsoBmffErDirectMappingDecision(
        PlaybackIsoBmffErDirectMapping mapping,
        PlaybackIsoBmffErDirectMappingPolicy policy)
    {
        Mapping = mapping;
        Policy = policy;
    }

    public PlaybackIsoBmffErDirectMapping Mapping { get; }
    public PlaybackIsoBmffErDirectMappingPolicy Policy { get; }
}

public static class PlaybackIsoBmffErDirectMappingDecisionResolver
{
    public static PlaybackIsoBmffErDirectMappingDecisionResult Resolve(
        PlaybackIsoBmffErDirectMapping? mapping,
        PlaybackIsoBmffErDirectMappingPolicy policy,
        out PlaybackIsoBmffErDirectMappingDecision? decision)
    {
        decision = null;
        if (mapping is null)
            return PlaybackIsoBmffErDirectMappingDecisionResult.MissingMapping;
        if (!Enum.IsDefined(policy))
            return PlaybackIsoBmffErDirectMappingDecisionResult.UndefinedPolicy;
        if (!mapping.Enabled)
            return PlaybackIsoBmffErDirectMappingDecisionResult.DisabledRejected;

        decision = new(mapping, policy);
        return PlaybackIsoBmffErDirectMappingDecisionResult.Admitted;
    }
}
