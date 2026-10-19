using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRelayNetworkStatusTests
{
    [Fact]
    public void NoneCapabilitiesKeepUnsupportedFeaturesExplicit()
    {
        var capabilities = GenesisRelayNetworkCapabilities.None;

        Assert.False(capabilities.SupportsSharedListener);
        Assert.False(capabilities.SupportsLocalRelayPolicy);
        Assert.False(capabilities.SupportsGlobalRelayPolicy);
        Assert.False(capabilities.SupportsDirectPlay);
        Assert.False(capabilities.SupportsAutomaticPortMapping);
        Assert.False(capabilities.SupportsIpv6);
    }

    [Fact]
    public void ListeningSnapshotProjectsAppliedEndpointAndRelayState()
    {
        var capabilities = new GenesisRelayNetworkCapabilities(
            SupportsSharedListener: true,
            SupportsLocalRelayPolicy: true,
            SupportsGlobalRelayPolicy: true,
            SupportsDirectPlay: false,
            SupportsAutomaticPortMapping: false,
            SupportsIpv6: true);

        var snapshot = new GenesisRelayNetworkStatusSnapshot(
            capabilities,
            GenesisRelayListenerRuntimeState.Listening,
            activeRelayConnections: 2,
            relayConnectionLimit: 8,
            addressFamily: AddressFamily.InterNetwork,
            boundAddress: IPAddress.Any,
            boundPort: 7144,
            allowsLocalRelay: true,
            allowsGlobalRelay: true);

        Assert.Same(capabilities, snapshot.Capabilities);
        Assert.True(snapshot.IsListening);
        Assert.Equal(AddressFamily.InterNetwork, snapshot.AddressFamily);
        Assert.Equal(IPAddress.Any, snapshot.BoundAddress);
        Assert.Equal(7144, snapshot.BoundPort);
        Assert.True(snapshot.AllowsLocalRelay);
        Assert.True(snapshot.AllowsGlobalRelay);
        Assert.Equal(2, snapshot.ActiveRelayConnections);
        Assert.Equal(8, snapshot.RelayConnectionLimit);
        Assert.Null(snapshot.ActiveDirectListeners);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, -1)]
    public void SnapshotRejectsNegativeRelayCounts(
        int activeRelayConnections,
        int? relayConnectionLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections,
                relayConnectionLimit));
    }

    [Fact]
    public void ListeningSnapshotRequiresAppliedFamilyAndPort()
    {
        Assert.Throws<ArgumentException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelayConnections: 0));

        Assert.Throws<ArgumentException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelayConnections: 0,
                addressFamily: AddressFamily.InterNetwork));
    }

    [Fact]
    public void BoundAddressRequiresMatchingFamily()
    {
        Assert.Throws<ArgumentException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 0,
                boundAddress: IPAddress.Any));

        Assert.Throws<ArgumentException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 0,
                addressFamily: AddressFamily.InterNetwork,
                boundAddress: IPAddress.IPv6Any));
    }

    [Fact]
    public void SnapshotRejectsInvalidAppliedPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 0,
                addressFamily: AddressFamily.InterNetwork,
                boundPort: 0));
    }

    [Fact]
    public void UnsupportedDirectPlayCannotClaimListenerCount()
    {
        Assert.Throws<ArgumentException>(() =>
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 0,
                activeDirectListeners: 1));
    }

    [Fact]
    public void SupportedDirectPlayProjectsCurrentCount()
    {
        var capabilities = new GenesisRelayNetworkCapabilities(
            SupportsSharedListener: false,
            SupportsLocalRelayPolicy: false,
            SupportsGlobalRelayPolicy: false,
            SupportsDirectPlay: true,
            SupportsAutomaticPortMapping: false,
            SupportsIpv6: false);

        var snapshot = new GenesisRelayNetworkStatusSnapshot(
            capabilities,
            GenesisRelayListenerRuntimeState.Unavailable,
            activeRelayConnections: 0,
            activeDirectListeners: 3);

        Assert.False(snapshot.IsListening);
        Assert.Equal(3, snapshot.ActiveDirectListeners);
    }

    [Fact]
    public void SnapshotCanRepresentConnectionsAboveNewLimitDuringDrain()
    {
        var snapshot = new GenesisRelayNetworkStatusSnapshot(
            GenesisRelayNetworkCapabilities.None,
            GenesisRelayListenerRuntimeState.Stopping,
            activeRelayConnections: 3,
            relayConnectionLimit: 1);

        Assert.Equal(3, snapshot.ActiveRelayConnections);
        Assert.Equal(1, snapshot.RelayConnectionLimit);
    }
}
