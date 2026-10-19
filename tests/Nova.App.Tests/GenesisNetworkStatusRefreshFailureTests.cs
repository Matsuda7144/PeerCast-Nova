using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkStatusRefreshFailureTests
{
    [Fact]
    public void RefreshFailureHidesLastSuccessfulRuntimeUntilSnapshotRecovers()
    {
        var capabilities = new GenesisRelayNetworkCapabilities(
            SupportsSharedListener: true,
            SupportsLocalRelayPolicy: true,
            SupportsGlobalRelayPolicy: true,
            SupportsDirectPlay: true,
            SupportsAutomaticPortMapping: true,
            SupportsIpv6: true);
        var settings = new GenesisNetworkBasicSettings(
            port: 7144,
            ipv6Enabled: true,
            automaticPortMappingEnabled: true,
            allowLocalRelay: true,
            allowGlobalRelay: true);
        var controller = new FailableSnapshotController(new(
            settings,
            new GenesisRelayNetworkStatusSnapshot(
                capabilities,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelayConnections: 3,
                relayConnectionLimit: 8,
                addressFamily: AddressFamily.InterNetwork,
                boundAddress: IPAddress.Loopback,
                boundPort: 7144,
                allowsLocalRelay: true,
                allowsGlobalRelay: true,
                activeDirectListeners: 2)));
        var viewModel = new GenesisNetworkSettingsViewModel(controller);

        var successful = GenesisNetworkSettingsWindowContent.Create(viewModel);
        Assert.True(viewModel.IsRuntimeStatusCurrent);
        Assert.Contains("Listener: Listening", successful.StatusLines);
        Assert.Contains("Bound endpoint: 127.0.0.1:7144", successful.StatusLines);
        Assert.Contains("Relay connections: 3 / 8", successful.StatusLines);
        Assert.Contains("Direct listeners: 2", successful.StatusLines);

        controller.ThrowOnSnapshot = true;

        viewModel.RefreshStatus();

        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Equal("Network status refresh failed.", viewModel.ErrorMessage);
        Assert.False(viewModel.CanEditPort);
        Assert.False(viewModel.CanEditIpv6);
        Assert.False(viewModel.CanEditAutomaticPortMapping);
        Assert.False(viewModel.CanEditLocalRelay);
        Assert.False(viewModel.CanEditGlobalRelay);
        var failed = GenesisNetworkSettingsWindowContent.Create(viewModel);
        Assert.Contains("Listener: Unknown (refresh failed)", failed.StatusLines);
        Assert.Contains("Bound endpoint: —", failed.StatusLines);
        Assert.Contains("Relay connections: Unknown", failed.StatusLines);
        Assert.Contains("Direct listeners: Unknown", failed.StatusLines);
        Assert.Contains("IPv4 reachability: Unknown", failed.StatusLines);
        Assert.Contains("IPv6 reachability: Unknown", failed.StatusLines);
        Assert.Contains("External IPv4: —", failed.StatusLines);
        Assert.Contains("External IPv6: —", failed.StatusLines);
        Assert.DoesNotContain("Bound endpoint: 127.0.0.1:7144", failed.StatusLines);
        Assert.DoesNotContain("Relay connections: 3 / 8", failed.StatusLines);
        Assert.DoesNotContain("Direct listeners: 2", failed.StatusLines);

        controller.SetSnapshot(new GenesisNetworkSettingsAppliedSnapshot(
            settings,
            new GenesisRelayNetworkStatusSnapshot(
                capabilities,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 1,
                relayConnectionLimit: 8,
                allowsLocalRelay: true,
                allowsGlobalRelay: true,
                activeDirectListeners: 0)));
        controller.ThrowOnSnapshot = false;

        viewModel.RefreshStatus();

        Assert.True(viewModel.IsRuntimeStatusCurrent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.CanEditPort);
        var recovered = GenesisNetworkSettingsWindowContent.Create(viewModel);
        Assert.Contains("Listener: Stopped", recovered.StatusLines);
        Assert.Contains("Bound endpoint: —", recovered.StatusLines);
        Assert.Contains("Relay connections: 1 / 8", recovered.StatusLines);
        Assert.Contains("Direct listeners: 0", recovered.StatusLines);
    }

    private sealed class FailableSnapshotController :
        IGenesisNetworkSettingsController
    {
        private GenesisNetworkSettingsAppliedSnapshot _snapshot;

        public FailableSnapshotController(
            GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public bool ThrowOnSnapshot { get; set; }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot =>
            ThrowOnSnapshot
                ? throw new InvalidOperationException("Synthetic snapshot failure.")
                : _snapshot;

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
        }

        public void SetSnapshot(GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            _snapshot = snapshot;
        }
    }
}
