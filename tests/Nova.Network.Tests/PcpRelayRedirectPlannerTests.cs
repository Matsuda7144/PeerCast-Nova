using System.Collections.Immutable;
using System.Net;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayRedirectPlannerTests
{
    [Fact]
    public void CreatePlan_GlobalOnlyCandidateProducesGlobalEntry()
    {
        var endpoint = Endpoint("192.0.2.1", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: endpoint)]);

        var entry = Assert.Single(plan);
        Assert.Equal(endpoint, entry.Endpoint);
        Assert.Equal(PcpRelayRedirectEndpointRole.Global, entry.Role);
    }

    [Fact]
    public void CreatePlan_GlobalIsPreferredWhenBothRolesExist()
    {
        var global = Endpoint("192.0.2.1", 7144);
        var local = Endpoint("192.0.2.2", 7145);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global, local)]);

        var entry = Assert.Single(plan);
        Assert.Equal(global, entry.Endpoint);
        Assert.Equal(PcpRelayRedirectEndpointRole.Global, entry.Role);
    }

    [Fact]
    public void CreatePlan_LocalOnlyCandidateProducesLocalEntry()
    {
        var endpoint = Endpoint("192.0.2.2", 7145);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(local: endpoint)]);

        var entry = Assert.Single(plan);
        Assert.Equal(endpoint, entry.Endpoint);
        Assert.Equal(PcpRelayRedirectEndpointRole.Local, entry.Role);
    }

    [Fact]
    public void CreatePlan_PreservesCandidateOrder()
    {
        var first = Endpoint("192.0.2.1", 7144);
        var second = Endpoint("192.0.2.2", 7145);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: first), Candidate(global: second)]);

        Assert.Collection(
            plan,
            entry => Assert.Equal(first, entry.Endpoint),
            entry => Assert.Equal(second, entry.Endpoint));
    }

    [Fact]
    public void CreatePlan_LocalOnlyBeforeGlobalPreservesOrder()
    {
        var local = Endpoint("192.0.2.2", 7145);
        var global = Endpoint("192.0.2.1", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(local: local), Candidate(global: global)]);

        Assert.Collection(
            plan,
            entry =>
            {
                Assert.Equal(local, entry.Endpoint);
                Assert.Equal(PcpRelayRedirectEndpointRole.Local, entry.Role);
            },
            entry =>
            {
                Assert.Equal(global, entry.Endpoint);
                Assert.Equal(PcpRelayRedirectEndpointRole.Global, entry.Role);
            });
    }

    [Fact]
    public void CreatePlan_DuplicateGlobalEndpointKeepsFirstOccurrence()
    {
        var endpoint = Endpoint("192.0.2.1", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: endpoint), Candidate(global: endpoint)]);

        var entry = Assert.Single(plan);
        Assert.Equal(PcpRelayRedirectEndpointRole.Global, entry.Role);
    }

    [Fact]
    public void CreatePlan_GlobalThenLocalDuplicateKeepsGlobalRole()
    {
        var endpoint = Endpoint("192.0.2.1", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: endpoint), Candidate(local: endpoint)]);

        var entry = Assert.Single(plan);
        Assert.Equal(PcpRelayRedirectEndpointRole.Global, entry.Role);
    }

    [Fact]
    public void CreatePlan_LocalThenGlobalDuplicateKeepsLocalRole()
    {
        var endpoint = Endpoint("192.0.2.1", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(local: endpoint), Candidate(global: endpoint)]);

        var entry = Assert.Single(plan);
        Assert.Equal(PcpRelayRedirectEndpointRole.Local, entry.Role);
    }

    [Fact]
    public void CreatePlan_DifferentPortsOnSameIpRemainDistinct()
    {
        var first = Endpoint("192.0.2.1", 7144);
        var second = Endpoint("192.0.2.1", 7145);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: first), Candidate(global: second)]);

        Assert.Equal([first, second], plan.Select(entry => entry.Endpoint));
    }

    [Fact]
    public void CreatePlan_DifferentIpsWithSamePortRemainDistinct()
    {
        var first = Endpoint("192.0.2.1", 7144);
        var second = Endpoint("192.0.2.2", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: first), Candidate(global: second)]);

        Assert.Equal([first, second], plan.Select(entry => entry.Endpoint));
    }

    [Fact]
    public void CreatePlan_SkipsAlreadyAttemptedFirstTarget()
    {
        var first = Endpoint("192.0.2.1", 7144);
        var second = Endpoint("192.0.2.2", 7145);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: first), Candidate(global: second)],
            [first]);

        var entry = Assert.Single(plan);
        Assert.Equal(second, entry.Endpoint);
    }

    [Fact]
    public void CreatePlan_SkipsDuplicateAlreadyAttemptedTarget()
    {
        var endpoint = Endpoint("192.0.2.1", 7144);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: endpoint), Candidate(local: endpoint)],
            [endpoint, endpoint]);

        Assert.Empty(plan);
    }

    [Fact]
    public void CreatePlan_AllTargetsAlreadyAttemptedIsEmpty()
    {
        var first = Endpoint("192.0.2.1", 7144);
        var second = Endpoint("192.0.2.2", 7145);

        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate(global: first), Candidate(local: second)],
            [first, second]);

        Assert.Empty(plan);
    }

    [Fact]
    public void CreatePlan_EmptyCandidatesIsEmpty()
    {
        var plan = PcpRelayRedirectPlanner.CreatePlan(
            ImmutableArray<PcpRelayHostCandidate>.Empty);

        Assert.Empty(plan);
    }

    [Fact]
    public void CreatePlan_CandidateWithoutRolesContributesNothing()
    {
        var plan = PcpRelayRedirectPlanner.CreatePlan(
            [Candidate()]);

        Assert.Empty(plan);
    }

    [Fact]
    public void CreatePlan_DefensiveCandidateBoundIsDeterministic()
    {
        var candidates = Enumerable.Range(
                0,
                PcpRelayRedirectPlanner.MaximumPlanEntries + 2)
            .Select(index => Candidate(global: Endpoint(
                "192.0.2." + (index + 1),
                7144 + index)))
            .ToImmutableArray();

        var plan = PcpRelayRedirectPlanner.CreatePlan(candidates);

        Assert.Equal(
            PcpRelayRedirectPlanner.MaximumPlanEntries,
            plan.Length);
        Assert.Equal(
            candidates.Take(PcpRelayRedirectPlanner.MaximumPlanEntries)
                .Select(candidate => candidate.GlobalEndpoint),
            plan.Select(entry => entry.Endpoint));
    }

    [Fact]
    public void CreatePlan_DoesNotMutateCandidates()
    {
        var global = Endpoint("192.0.2.1", 7144);
        var local = Endpoint("192.0.2.2", 7145);
        var candidate = Candidate(global, local);
        var candidates = ImmutableArray.Create(candidate);

        _ = PcpRelayRedirectPlanner.CreatePlan(candidates);

        Assert.Same(global, candidate.GlobalEndpoint);
        Assert.Same(local, candidate.LocalEndpoint);
        Assert.Same(candidate, candidates[0]);
    }

    private static PcpRelayHostCandidate Candidate(
        PcpRelayHostEndpoint? global = null,
        PcpRelayHostEndpoint? local = null)
    {
        return new(global, local);
    }

    private static PcpRelayHostEndpoint Endpoint(string address, int port)
    {
        return new(IPAddress.Parse(address), port);
    }
}
