using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsApplyFailureTruthfulnessTests
{
    [Fact]
    public async Task AppliedOutcomeWithUnreadableSnapshotHidesPriorRuntimeUntilRefreshRecovers()
    {
        var initialSettings = CreateSettings(port: 7144);
        var controller = new FailableNetworkSettingsController(
            CreateAppliedSnapshot(
                initialSettings,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelays: 3));
        controller.ApplyHandler = (requested, _) =>
        {
            controller.SetSnapshot(CreateAppliedSnapshot(
                requested,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelays: 1));
            controller.ThrowOnSnapshot = true;
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Applied);
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync();

        Assert.Equal(1, controller.ApplyCount);
        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Equal("Network settings failed.", viewModel.ErrorMessage);
        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanEditPort);
        Assert.False(viewModel.CanApply);
        var failed = GenesisNetworkSettingsPresentation.Create(viewModel);
        Assert.Equal("Unknown (refresh failed)", failed.ListenerStateText);
        Assert.Equal("—", failed.BoundEndpointText);
        Assert.Equal("Unknown", failed.RelayConnectionsText);

        controller.ThrowOnSnapshot = false;
        viewModel.RefreshStatus();

        Assert.True(viewModel.IsRuntimeStatusCurrent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsDirty);
        Assert.True(viewModel.CanEditPort);
        var recovered = GenesisNetworkSettingsPresentation.Create(viewModel);
        Assert.Equal("Stopped", recovered.ListenerStateText);
        Assert.Equal("—", recovered.BoundEndpointText);
        Assert.Equal("1 / 8", recovered.RelayConnectionsText);
    }

    [Fact]
    public async Task ControllerExceptionHidesPriorRuntimeUntilSnapshotIsVerifiedAgain()
    {
        var controller = new FailableNetworkSettingsController(
            CreateAppliedSnapshot(
                CreateSettings(port: 7144),
                GenesisRelayListenerRuntimeState.Listening,
                activeRelays: 2))
        {
            ApplyHandler = static (_, _) =>
                throw new InvalidOperationException("Synthetic apply failure."),
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync();

        Assert.Equal(1, controller.ApplyCount);
        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Equal("Network settings failed.", viewModel.ErrorMessage);
        Assert.False(viewModel.CanEditPort);
        Assert.False(viewModel.CanApply);
        var failed = GenesisNetworkSettingsPresentation.Create(viewModel);
        Assert.Equal("Unknown (refresh failed)", failed.ListenerStateText);
        Assert.Equal("—", failed.BoundEndpointText);
        Assert.Equal("Unknown", failed.RelayConnectionsText);

        viewModel.RefreshStatus();

        Assert.True(viewModel.IsRuntimeStatusCurrent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanEditPort);
        Assert.True(viewModel.CanApply);
        var recovered = GenesisNetworkSettingsPresentation.Create(viewModel);
        Assert.Equal("Listening", recovered.ListenerStateText);
        Assert.Equal("127.0.0.1:7144", recovered.BoundEndpointText);
        Assert.Equal("2 / 8", recovered.RelayConnectionsText);
    }

    [Fact]
    public async Task UnknownApplyOutcomeFailsClosedUntilSnapshotIsVerifiedAgain()
    {
        var controller = new FailableNetworkSettingsController(
            CreateAppliedSnapshot(
                CreateSettings(port: 7144),
                GenesisRelayListenerRuntimeState.Listening,
                activeRelays: 1))
        {
            ApplyHandler = static (_, _) => ValueTask.FromResult(
                (GenesisNetworkSettingsApplyOutcome)int.MaxValue),
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync();

        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Equal("Network settings failed.", viewModel.ErrorMessage);
        Assert.False(viewModel.CanApply);

        viewModel.RefreshStatus();

        Assert.True(viewModel.IsRuntimeStatusCurrent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanApply);
    }

    private static GenesisNetworkBasicSettings CreateSettings(int port) =>
        new(
            port,
            ipv6Enabled: false,
            automaticPortMappingEnabled: false,
            allowLocalRelay: true,
            allowGlobalRelay: true);

    private static GenesisNetworkSettingsAppliedSnapshot CreateAppliedSnapshot(
        GenesisNetworkBasicSettings settings,
        GenesisRelayListenerRuntimeState listenerState,
        int activeRelays)
    {
        var listening = listenerState == GenesisRelayListenerRuntimeState.Listening;
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            FullCapabilities,
            listenerState,
            activeRelayConnections: activeRelays,
            relayConnectionLimit: 8,
            addressFamily: listening ? AddressFamily.InterNetwork : null,
            boundAddress: listening ? IPAddress.Loopback : null,
            boundPort: listening ? settings.Port : null,
            allowsLocalRelay: settings.AllowLocalRelay,
            allowsGlobalRelay: settings.AllowGlobalRelay);
        return new GenesisNetworkSettingsAppliedSnapshot(settings, runtime);
    }

    private static GenesisRelayNetworkCapabilities FullCapabilities { get; } = new(
        SupportsSharedListener: true,
        SupportsLocalRelayPolicy: true,
        SupportsGlobalRelayPolicy: true,
        SupportsDirectPlay: false,
        SupportsAutomaticPortMapping: true,
        SupportsIpv6: true);

    private sealed class FailableNetworkSettingsController :
        IGenesisNetworkSettingsController
    {
        private GenesisNetworkSettingsAppliedSnapshot _snapshot;
        private int _applyCount;

        public FailableNetworkSettingsController(
            GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public bool ThrowOnSnapshot { get; set; }

        public Func<
            GenesisNetworkBasicSettings,
            CancellationToken,
            ValueTask<GenesisNetworkSettingsApplyOutcome>>? ApplyHandler
        { get; set; }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot =>
            ThrowOnSnapshot
                ? throw new InvalidOperationException("Synthetic snapshot failure.")
                : _snapshot;

        public int ApplyCount => Volatile.Read(ref _applyCount);

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            Interlocked.Increment(ref _applyCount);
            return ApplyHandler?.Invoke(settings, cancellationToken) ??
                ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
        }

        public void SetSnapshot(GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            _snapshot = snapshot;
        }
    }
}
