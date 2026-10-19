namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsReachabilityTruthfulnessTests
{
    [Fact]
    public void SupportedListenerWithoutReachabilityEvidenceRemainsUnknownAndExplainsNoCheck()
    {
        var capabilities = new GenesisRelayNetworkCapabilities(
            SupportsSharedListener: true,
            SupportsLocalRelayPolicy: true,
            SupportsGlobalRelayPolicy: true,
            SupportsDirectPlay: true,
            SupportsAutomaticPortMapping: false,
            SupportsIpv6: true);
        var settings = new GenesisNetworkBasicSettings(
            port: 7144,
            ipv6Enabled: true,
            automaticPortMappingEnabled: false,
            allowLocalRelay: true,
            allowGlobalRelay: true);
        var controller = new StaticNetworkSettingsController(new(
            settings,
            new GenesisRelayNetworkStatusSnapshot(
                capabilities,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 0,
                relayConnectionLimit: 4,
                activeDirectListeners: 0)));
        var viewModel = new GenesisNetworkSettingsViewModel(controller);

        var content = GenesisNetworkSettingsWindowContent.Create(viewModel);

        Assert.Contains("IPv4 reachability: Unknown", content.StatusLines);
        Assert.Contains("IPv6 reachability: Unknown", content.StatusLines);
        Assert.Contains("External IPv4: —", content.StatusLines);
        Assert.Contains("External IPv6: —", content.StatusLines);
        Assert.Contains("Port reachability has not been checked.", content.AvailabilityNote);
    }

    private sealed class StaticNetworkSettingsController : IGenesisNetworkSettingsController
    {
        public StaticNetworkSettingsController(GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot { get; }

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
        }
    }
}
