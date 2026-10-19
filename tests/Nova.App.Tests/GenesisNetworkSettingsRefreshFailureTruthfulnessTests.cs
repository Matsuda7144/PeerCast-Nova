using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsRefreshFailureTruthfulnessTests
{
    [Fact]
    public void RefreshFailureMasksCachedRuntimeAndExplainsUnavailableControls()
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
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            capabilities,
            GenesisRelayListenerRuntimeState.Listening,
            activeRelayConnections: 3,
            relayConnectionLimit: 8,
            addressFamily: AddressFamily.InterNetwork,
            boundAddress: IPAddress.Any,
            boundPort: 7144,
            allowsLocalRelay: true,
            allowsGlobalRelay: true,
            activeDirectListeners: 2);
        var controller = new FailingSnapshotController(
            new GenesisNetworkSettingsAppliedSnapshot(settings, runtime));
        var viewModel = new GenesisNetworkSettingsViewModel(controller);
        controller.FailSnapshotRead = true;

        viewModel.RefreshStatus();
        var content = GenesisNetworkSettingsWindowContent.Create(viewModel);

        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Equal("Network status refresh failed.", viewModel.ErrorMessage);
        Assert.Contains("Listener: Unknown (refresh failed)", content.StatusLines);
        Assert.Contains("Bound endpoint: —", content.StatusLines);
        Assert.Contains("Relay connections: Unknown", content.StatusLines);
        Assert.Contains("Direct listeners: Unknown", content.StatusLines);
        Assert.Contains("IPv4 reachability: Unknown", content.StatusLines);
        Assert.Contains("IPv6 reachability: Unknown", content.StatusLines);
        Assert.Contains("External IPv4: —", content.StatusLines);
        Assert.Contains("External IPv6: —", content.StatusLines);
        Assert.Equal(
            "Network status refresh failed. Listener editing and port checks are unavailable until current backend status can be confirmed.",
            content.AvailabilityNote);
        Assert.DoesNotContain("active backend", content.AvailabilityNote);
    }

    private sealed class FailingSnapshotController : IGenesisNetworkSettingsController
    {
        private readonly GenesisNetworkSettingsAppliedSnapshot _snapshot;

        public FailingSnapshotController(GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public bool FailSnapshotRead { get; set; }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot =>
            FailSnapshotRead
                ? throw new InvalidOperationException("Injected refresh failure.")
                : _snapshot;

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
