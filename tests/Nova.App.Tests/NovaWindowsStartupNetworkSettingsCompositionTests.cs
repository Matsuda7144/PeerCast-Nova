namespace PeerCast.Nova.App.Tests;

public sealed class NovaWindowsStartupNetworkSettingsCompositionTests
{
    [Fact]
    public void DefaultOptionsUseUnavailableNetworkSettingsController()
    {
        var options = new NovaWindowsStartupOptions();

        Assert.Same(
            GenesisUnavailableNetworkSettingsController.Instance,
            options.NetworkSettingsController);
    }

    [Fact]
    public void OptionsCanCarryApplicationOwnedNetworkSettingsController()
    {
        var controller = new RecordingNetworkSettingsController();
        var options = new NovaWindowsStartupOptions
        {
            NetworkSettingsController = controller,
        };

        Assert.Same(controller, options.NetworkSettingsController);
        _ = new NovaWindowsStartupComposition(options);
    }

    [Fact]
    public void StartupCompositionRejectsMissingNetworkSettingsController()
    {
        var options = new NovaWindowsStartupOptions
        {
            NetworkSettingsController = null!,
        };

        Assert.Throws<ArgumentNullException>(() =>
            new NovaWindowsStartupComposition(options));
    }

    private sealed class RecordingNetworkSettingsController :
        IGenesisNetworkSettingsController
    {
        private static readonly GenesisNetworkSettingsAppliedSnapshot AppliedSnapshot = new(
            new GenesisNetworkBasicSettings(
                port: 7144,
                ipv6Enabled: false,
                automaticPortMappingEnabled: false,
                allowLocalRelay: false,
                allowGlobalRelay: false),
            new GenesisRelayNetworkStatusSnapshot(
                GenesisRelayNetworkCapabilities.None,
                GenesisRelayListenerRuntimeState.Unavailable,
                activeRelayConnections: 0));

        public GenesisNetworkSettingsAppliedSnapshot Snapshot => AppliedSnapshot;

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
