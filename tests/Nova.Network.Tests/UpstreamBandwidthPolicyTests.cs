using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.Network.Tests;

public sealed class UpstreamBandwidthPolicyTests
{
    [Theory]
    [InlineData("192.0.2.1", AddressFamily.InterNetwork)]
    [InlineData("2001:db8::1", AddressFamily.InterNetworkV6)]
    [InlineData("::ffff:192.0.2.1", AddressFamily.InterNetwork)]
    [InlineData("127.0.0.1", AddressFamily.InterNetwork)]
    [InlineData("::1", AddressFamily.InterNetworkV6)]
    public void ClassifiesActualRemoteAddressIncludingMappedIpv4(string address, AddressFamily expected) =>
        Assert.Equal(expected, UpstreamBandwidthPolicy.ClassifyAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("192.0.2.1")]
    [InlineData("2001:db8::1")]
    [InlineData("::ffff:192.0.2.1")]
    public void UnlimitedAndExplicitZeroRemainDifferentWithUnknownAccounting(string address)
    {
        var remote = IPAddress.Parse(address);
        var unlimited = new UpstreamBandwidthPolicy();
        var zero = new UpstreamBandwidthPolicy(new(0, 0));
        Assert.Equal(BandwidthAdmissionDecision.Allowed, unlimited.Evaluate(remote, new(null, null), null));
        Assert.Null(unlimited.GetLimitBytesPerSecond(remote));
        Assert.Equal(BandwidthAdmissionDecision.LimitReached, zero.Evaluate(remote, new(null, null), null));
        Assert.Equal(BandwidthAdmissionDecision.LimitReached, zero.Evaluate(remote, new(0, 0), 0));
        Assert.Equal(0, zero.GetLimitBytesPerSecond(remote));
    }

    [Fact]
    public void FamilyBudgetsAreSeparateAndExactBoundaryIsAllowed()
    {
        var policy = new UpstreamBandwidthPolicy(new(1000, 2000));
        var current = new BandwidthAccountingSnapshot(900, 1901);
        Assert.Equal(BandwidthAdmissionDecision.Allowed,
            policy.Evaluate(IPAddress.Parse("192.0.2.1"), current, 100));
        Assert.Equal(BandwidthAdmissionDecision.Allowed,
            policy.Evaluate(IPAddress.Parse("::ffff:192.0.2.1"), current, 100));
        Assert.Equal(BandwidthAdmissionDecision.LimitReached,
            policy.Evaluate(IPAddress.Parse("2001:db8::1"), current, 100));
        Assert.Equal(BandwidthAdmissionDecision.LimitReached,
            policy.Evaluate(IPAddress.Parse("192.0.2.1"), current, 101));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void UnknownBitrateCannotBecomeZeroCostAdmission(int? bitrate)
    {
        var policy = new UpstreamBandwidthPolicy(new(1000));
        Assert.Equal(BandwidthAdmissionDecision.BitrateUnavailable,
            policy.Evaluate(IPAddress.Parse("192.0.2.1"), new(0, null), bitrate));
    }

    [Fact]
    public void MissingOrIncompleteFamilyAccountingIsIndeterminate()
    {
        var policy = new UpstreamBandwidthPolicy(new(1000, 2000));
        Assert.Equal(BandwidthAdmissionDecision.AccountingUnavailable,
            policy.Evaluate(IPAddress.Parse("192.0.2.1"), new(null, 0), 100));
        Assert.Equal(BandwidthAdmissionDecision.AccountingUnavailable,
            policy.Evaluate(IPAddress.Parse("2001:db8::1"), new(0, null), 100));
    }

    [Fact]
    public void ArithmeticDoesNotOverflowForVeryLargeAccountingOrLimits()
    {
        var policy = new UpstreamBandwidthPolicy(new(int.MaxValue));
        var remote = IPAddress.Parse("192.0.2.1");
        Assert.Equal((long)int.MaxValue * 125, policy.GetLimitBytesPerSecond(remote));
        Assert.Equal(BandwidthAdmissionDecision.LimitReached, policy.Evaluate(remote, new(long.MaxValue, 0), 1));
        Assert.Equal(BandwidthAdmissionDecision.Allowed, policy.Evaluate(remote, new(0, 0), int.MaxValue));
        Assert.Equal(125000, new UpstreamBandwidthPolicy(new(1000)).GetLimitBytesPerSecond(remote));
    }

    [Fact]
    public void InvalidNumericalInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UpstreamBandwidthPolicy(new(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UpstreamBandwidthPolicy(new(null, -1)));
        var policy = new UpstreamBandwidthPolicy();
        var remote = IPAddress.Loopback;
        Assert.Throws<ArgumentNullException>(() => UpstreamBandwidthPolicy.ClassifyAddress(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Evaluate(remote, new(-1, 0), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Evaluate(remote, new(0, -1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.Evaluate(remote, new(0, 0), -1));
    }
}
