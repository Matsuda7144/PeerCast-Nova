using System.Collections.Immutable;

namespace PeerCast.Nova.Network.Pcp;

internal enum PcpRelayRedirectEndpointRole
{
    Global,
    Local,
}

internal sealed record PcpRelayRedirectPlanEntry(
    PcpRelayHostEndpoint Endpoint,
    PcpRelayRedirectEndpointRole Role);

internal static class PcpRelayRedirectPlanner
{
    internal const int MaximumPlanEntries =
        PcpRelayHostCandidateParser.MaximumCandidates;

    internal static ImmutableArray<PcpRelayRedirectPlanEntry> CreatePlan(
        ImmutableArray<PcpRelayHostCandidate> candidates,
        IEnumerable<PcpRelayHostEndpoint>? alreadyAttempted = null)
    {
        var attempted = new HashSet<PcpRelayHostEndpoint>();
        if (alreadyAttempted is not null)
        {
            foreach (var endpoint in alreadyAttempted)
            {
                attempted.Add(endpoint);
            }
        }

        var plan = ImmutableArray.CreateBuilder<PcpRelayRedirectPlanEntry>(
            MaximumPlanEntries);

        var candidateCount = 0;
        foreach (var candidate in candidates)
        {
            if (candidateCount++ >= MaximumPlanEntries)
            {
                break;
            }

            var (endpoint, role) = candidate.GlobalEndpoint is { } global
                ? (global, PcpRelayRedirectEndpointRole.Global)
                : candidate.LocalEndpoint is { } local
                    ? (local, PcpRelayRedirectEndpointRole.Local)
                    : (null, default(PcpRelayRedirectEndpointRole));

            if (endpoint is null || !attempted.Add(endpoint))
            {
                continue;
            }

            plan.Add(new(endpoint, role));
        }

        return plan.ToImmutable();
    }
}
