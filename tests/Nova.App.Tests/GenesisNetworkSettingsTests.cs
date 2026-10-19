using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsTests
{
    [Fact]
    public void ListenerSettingsNormalizeIdentityAndPreservePermissions()
    {
        var settings = new GenesisNetworkListenerSettings(
            id: "  primary-v4  ",
            addressFamily: AddressFamily.InterNetwork,
            port: 7144,
            bindAddress: IPAddress.Any,
            isEnabled: true,
            allowLocalRelay: true,
            allowGlobalRelay: false,
            allowLocalDirectPlay: true,
            allowGlobalDirectPlay: false);

        Assert.Equal("primary-v4", settings.Id);
        Assert.Equal(AddressFamily.InterNetwork, settings.AddressFamily);
        Assert.Equal(7144, settings.Port);
        Assert.Equal(IPAddress.Any, settings.BindAddress);
        Assert.True(settings.IsEnabled);
        Assert.True(settings.AllowLocalRelay);
        Assert.False(settings.AllowGlobalRelay);
        Assert.True(settings.AllowLocalDirectPlay);
        Assert.False(settings.AllowGlobalDirectPlay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ListenerSettingsRejectInvalidPersistentPorts(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GenesisNetworkListenerSettings(
                "primary-v4",
                AddressFamily.InterNetwork,
                port));
    }

    [Fact]
    public void ListenerSettingsRejectBindAddressFromAnotherFamily()
    {
        Assert.Throws<ArgumentException>(() =>
            new GenesisNetworkListenerSettings(
                "primary-v4",
                AddressFamily.InterNetwork,
                7144,
                IPAddress.IPv6Any));
    }

    [Fact]
    public void ReachabilityRequestNormalizesCandidatePorts()
    {
        var request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            [7145, 7144, 7145],
            IPAddress.Any);

        Assert.Equal(AddressFamily.InterNetwork, request.AddressFamily);
        Assert.Equal(IPAddress.Any, request.LocalAddress);
        Assert.Equal([7144, 7145], request.Ports);
    }

    [Fact]
    public void ReachabilityRequestRequiresAtLeastOnePort()
    {
        Assert.Throws<ArgumentException>(() =>
            new GenesisNetworkReachabilityRequest(
                AddressFamily.InterNetwork,
                Array.Empty<int>()));
    }

    [Fact]
    public void OpenResultRequiresExternalConfirmationOfRequestedPort()
    {
        var request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            [7144, 7145]);
        var checkedAt = new DateTimeOffset(2026, 8, 20, 4, 0, 0, TimeSpan.Zero);

        var result = GenesisNetworkReachabilityResult.Open(
            request,
            [7145],
            TimeSpan.FromMilliseconds(125),
            checkedAt,
            IPAddress.Parse("203.0.113.10"));

        Assert.True(result.IsOpen);
        Assert.Equal(GenesisNetworkReachabilityOutcome.Open, result.Outcome);
        Assert.Equal([7145], result.ReachablePorts);
        Assert.Equal(IPAddress.Parse("203.0.113.10"), result.ExternalAddress);
        Assert.Equal(TimeSpan.FromMilliseconds(125), result.Elapsed);
        Assert.Equal(checkedAt, result.CheckedAt);
    }

    [Fact]
    public void OpenResultRejectsEmptyOrUnrequestedConfirmation()
    {
        var request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            [7144]);
        var checkedAt = DateTimeOffset.UnixEpoch;

        Assert.Throws<ArgumentException>(() =>
            GenesisNetworkReachabilityResult.Open(
                request,
                Array.Empty<int>(),
                TimeSpan.Zero,
                checkedAt));
        Assert.Throws<ArgumentException>(() =>
            GenesisNetworkReachabilityResult.Open(
                request,
                [7145],
                TimeSpan.Zero,
                checkedAt));
    }

    [Fact]
    public void ClosedFailedAndCancelledNeverClaimOpenPortEvidence()
    {
        var request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetworkV6,
            [7144],
            IPAddress.IPv6Any);
        var checkedAt = DateTimeOffset.UnixEpoch;

        var closed = GenesisNetworkReachabilityResult.Closed(
            request,
            TimeSpan.FromMilliseconds(20),
            checkedAt,
            IPAddress.Parse("2001:db8::10"));
        var failed = GenesisNetworkReachabilityResult.Failed(
            request,
            TimeSpan.FromMilliseconds(20),
            checkedAt,
            "  timeout  ");
        var cancelled = GenesisNetworkReachabilityResult.Cancelled(
            request,
            TimeSpan.FromMilliseconds(5),
            checkedAt);

        Assert.False(closed.IsOpen);
        Assert.Empty(closed.ReachablePorts);
        Assert.Equal(GenesisNetworkReachabilityOutcome.Closed, closed.Outcome);
        Assert.False(failed.IsOpen);
        Assert.Empty(failed.ReachablePorts);
        Assert.Equal(GenesisNetworkReachabilityOutcome.Failed, failed.Outcome);
        Assert.Equal("timeout", failed.DiagnosticCode);
        Assert.False(cancelled.IsOpen);
        Assert.Empty(cancelled.ReachablePorts);
        Assert.Equal(GenesisNetworkReachabilityOutcome.Cancelled, cancelled.Outcome);
    }

    [Fact]
    public void ReachabilityEvidenceRejectsExternalAddressFromAnotherFamily()
    {
        var request = new GenesisNetworkReachabilityRequest(
            AddressFamily.InterNetwork,
            [7144]);

        Assert.Throws<ArgumentException>(() =>
            GenesisNetworkReachabilityResult.Closed(
                request,
                TimeSpan.Zero,
                DateTimeOffset.UnixEpoch,
                IPAddress.IPv6Loopback));
    }
}
