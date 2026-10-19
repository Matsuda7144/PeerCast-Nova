namespace PeerCast.Nova.App.Tests;

public sealed class GenesisUnavailableNetworkSettingsControllerTests
{
    [Fact]
    public void SnapshotDoesNotClaimSharedListenerCapabilities()
    {
        var snapshot = GenesisUnavailableNetworkSettingsController.Instance.Snapshot;

        Assert.Equal(
            GenesisRelayNetworkCapabilities.None,
            snapshot.RuntimeStatus.Capabilities);
        Assert.Equal(
            GenesisRelayListenerRuntimeState.Unavailable,
            snapshot.RuntimeStatus.ListenerState);
        Assert.Equal(0, snapshot.RuntimeStatus.ActiveRelayConnections);
        Assert.Null(snapshot.RuntimeStatus.BoundPort);
        Assert.Null(snapshot.RuntimeStatus.BoundAddress);
    }

    [Fact]
    public async Task ApplyIsRejectedUntilBackendExists()
    {
        var outcome = await GenesisUnavailableNetworkSettingsController.Instance.ApplyAsync(
            new GenesisNetworkBasicSettings(
                8000,
                ipv6Enabled: false,
                automaticPortMappingEnabled: false,
                allowLocalRelay: false,
                allowGlobalRelay: false));

        Assert.Equal(GenesisNetworkSettingsApplyOutcome.Rejected, outcome);
    }
}
