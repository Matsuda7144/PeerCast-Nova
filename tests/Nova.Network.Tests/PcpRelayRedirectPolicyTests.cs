using System.Net;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Tests;

public sealed class PcpRelayRedirectPolicyTests
{
    [Fact]
    public void Evaluate_PublicGlobalWithNoAttemptsIsEligible()
    {
        var result = Evaluate("1.1.1.1", PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(
            new PcpRelayRedirectEligibility(
                true,
                PcpRelayRedirectEligibilityReason.Eligible),
            result);
    }

    [Fact]
    public void Evaluate_PublicGlobalAtLastBudgetSlotIsEligible()
    {
        var result = Evaluate(
            "8.8.8.8",
            PcpRelayRedirectEndpointRole.Global,
            redirectsAlreadyAttempted: 7);

        Assert.Equal(PcpRelayRedirectEligibilityReason.Eligible, result.Reason);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_AtMaximumBudgetIsIneligible()
    {
        var result = Evaluate(
            "1.1.1.1",
            PcpRelayRedirectEndpointRole.Global,
            redirectsAlreadyAttempted: 8);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.RedirectBudgetExhausted,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_OverMaximumBudgetIsIneligible()
    {
        var result = Evaluate(
            "1.1.1.1",
            PcpRelayRedirectEndpointRole.Global,
            redirectsAlreadyAttempted: 9);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.RedirectBudgetExhausted,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_NegativeAttemptCountThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Evaluate(
            "1.1.1.1",
            PcpRelayRedirectEndpointRole.Global,
            redirectsAlreadyAttempted: -1));
    }

    [Fact]
    public void Evaluate_LocalPublicAddressIsRejectedByRole()
    {
        var result = Evaluate("1.1.1.1", PcpRelayRedirectEndpointRole.Local);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.LocalRoleUnsupported,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_LocalPrivateAddressIsRejectedByRoleBeforeAddress()
    {
        var result = Evaluate(
            "192.168.1.10",
            PcpRelayRedirectEndpointRole.Local);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.LocalRoleUnsupported,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_GlobalIpv6IsRejected()
    {
        var result = Evaluate(
            "2001:db8::1",
            PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.NonIpv4Address,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Theory]
    [InlineData("224.0.0.0")]
    [InlineData("239.255.255.255")]
    public void Evaluate_MulticastRangeIsRejected(string address)
    {
        var result = Evaluate(address, PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.MulticastAddress,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Fact]
    public void Evaluate_AddressBeforeMulticastRangeIsNotRejectedAsMulticast()
    {
        var result = Evaluate(
            "223.255.255.255",
            PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(PcpRelayRedirectEligibilityReason.Eligible, result.Reason);
        Assert.True(result.IsEligible);
    }

    [Theory]
    [InlineData("240.0.0.0")]
    [InlineData("255.255.255.255")]
    public void Evaluate_Reserved240RangeIsSpecialPurpose(string address)
    {
        var result = Evaluate(address, PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(
            PcpRelayRedirectEligibilityReason.SpecialPurposeAddress,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Theory]
    [MemberData(nameof(SpecialPurposeBoundaryCases))]
    public void Evaluate_SpecialPurposeBoundariesAreClassified(
        string address,
        object expectedReason)
    {
        var result = Evaluate(address, PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(
            (PcpRelayRedirectEligibilityReason)expectedReason,
            result.Reason);
        Assert.False(result.IsEligible);
    }

    [Theory]
    [MemberData(nameof(AdjacentPublicCases))]
    public void Evaluate_AdjacentAddressesRemainEligible(string address)
    {
        var result = Evaluate(address, PcpRelayRedirectEndpointRole.Global);

        Assert.Equal(PcpRelayRedirectEligibilityReason.Eligible, result.Reason);
        Assert.True(result.IsEligible);
    }

    [Fact]
    public void Evaluate_DoesNotAlterEndpointAddressOrPort()
    {
        var endpoint = new PcpRelayHostEndpoint(
            IPAddress.Parse("192.0.2.10"),
            7144);
        var entry = new PcpRelayRedirectPlanEntry(
            endpoint,
            PcpRelayRedirectEndpointRole.Global);

        _ = PcpRelayRedirectPolicy.Evaluate(entry, 0);

        Assert.Same(endpoint.Address, entry.Endpoint.Address);
        Assert.Equal(7144, entry.Endpoint.Port);
    }

    [Fact]
    public void Evaluate_IsDeterministicAcrossRepeatedCalls()
    {
        var entry = Entry("198.51.100.10", PcpRelayRedirectEndpointRole.Global);

        var first = PcpRelayRedirectPolicy.Evaluate(entry, 3);
        var second = PcpRelayRedirectPolicy.Evaluate(entry, 3);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Planner_RemainsDescriptiveAndPolicyRejectsLocalAndSpecialPurpose()
    {
        var local = new PcpRelayHostCandidate(
            GlobalEndpoint: null,
            LocalEndpoint: new(IPAddress.Parse("192.168.1.10"), 7144));
        var specialPurpose = new PcpRelayHostCandidate(
            GlobalEndpoint: new(IPAddress.Parse("192.0.2.10"), 7144),
            LocalEndpoint: null);

        var plan = PcpRelayRedirectPlanner.CreatePlan([local, specialPurpose]);

        Assert.Equal(2, plan.Length);
        Assert.Equal(
            PcpRelayRedirectEndpointRole.Local,
            plan[0].Role);
        Assert.Equal(
            PcpRelayRedirectEligibilityReason.SpecialPurposeAddress,
            PcpRelayRedirectPolicy.Evaluate(plan[1], 0).Reason);
        Assert.Equal(
            PcpRelayRedirectEligibilityReason.LocalRoleUnsupported,
            PcpRelayRedirectPolicy.Evaluate(plan[0], 0).Reason);
    }

    public static IEnumerable<object[]> SpecialPurposeBoundaryCases()
    {
        return
        [
            ["0.0.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["0.255.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["10.0.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["10.255.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["100.64.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["100.127.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["127.0.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["127.255.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["169.254.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["169.254.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["172.16.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["172.31.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.0.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.0.0.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.0.2.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.0.2.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.31.196.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.31.196.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.52.193.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.52.193.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.88.99.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.88.99.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.168.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.168.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.175.48.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["192.175.48.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["198.18.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["198.19.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["198.51.100.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["198.51.100.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["203.0.113.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["203.0.113.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["240.0.0.0", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
            ["255.255.255.255", PcpRelayRedirectEligibilityReason.SpecialPurposeAddress],
        ];
    }

    public static IEnumerable<object[]> AdjacentPublicCases()
    {
        return
        [
            ["1.0.0.0"],
            ["9.255.255.255"],
            ["11.0.0.0"],
            ["100.63.255.255"],
            ["100.128.0.0"],
            ["126.255.255.255"],
            ["128.0.0.0"],
            ["169.253.255.255"],
            ["169.255.0.0"],
            ["172.15.255.255"],
            ["172.32.0.0"],
            ["191.255.255.255"],
            ["192.0.1.0"],
            ["192.0.1.255"],
            ["192.0.3.0"],
            ["192.31.195.255"],
            ["192.31.197.0"],
            ["192.52.192.255"],
            ["192.52.194.0"],
            ["192.88.98.255"],
            ["192.88.100.0"],
            ["192.167.255.255"],
            ["192.169.0.0"],
            ["192.175.47.255"],
            ["192.175.49.0"],
            ["198.17.255.255"],
            ["198.20.0.0"],
            ["198.51.99.255"],
            ["198.51.101.0"],
            ["203.0.112.255"],
            ["203.0.114.0"],
        ];
    }

    private static PcpRelayRedirectEligibility Evaluate(
        string address,
        PcpRelayRedirectEndpointRole role,
        int redirectsAlreadyAttempted = 0)
    {
        return PcpRelayRedirectPolicy.Evaluate(
            Entry(address, role),
            redirectsAlreadyAttempted);
    }

    private static PcpRelayRedirectPlanEntry Entry(
        string address,
        PcpRelayRedirectEndpointRole role)
    {
        return new(
            new(IPAddress.Parse(address), 7144),
            role);
    }
}
