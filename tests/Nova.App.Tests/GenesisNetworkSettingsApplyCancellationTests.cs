using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsApplyCancellationTests
{
    [Fact]
    public async Task CallerCancellationHidesPriorRuntimeUntilSnapshotIsVerifiedAgain()
    {
        using var cancellation = new CancellationTokenSource();
        var initialSettings = CreateSettings(port: 7144);
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(
                initialSettings,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelays: 3));
        controller.ApplyHandler = (requested, cancellationToken) =>
        {
            controller.SetSnapshot(CreateAppliedSnapshot(
                requested,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelays: 1));
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Applied);
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync(cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, controller.ApplyCount);
        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanEditPort);
        Assert.False(viewModel.CanApply);
        var cancelled = GenesisNetworkSettingsPresentation.Create(viewModel);
        Assert.Equal("Unknown (refresh failed)", cancelled.ListenerStateText);
        Assert.Equal("—", cancelled.BoundEndpointText);
        Assert.Equal("Unknown", cancelled.RelayConnectionsText);
        Assert.NotEqual("127.0.0.1:7144", cancelled.BoundEndpointText);

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
    public async Task UnrelatedOperationCanceledExceptionIsApplyFailure()
    {
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(
                CreateSettings(port: 7144),
                GenesisRelayListenerRuntimeState.Listening,
                activeRelays: 2))
        {
            ApplyHandler = static (_, _) =>
                throw new OperationCanceledException("Synthetic unrelated cancellation."),
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync();

        Assert.Equal(1, controller.ApplyCount);
        Assert.False(viewModel.IsRuntimeStatusCurrent);
        Assert.Equal("Network settings failed.", viewModel.ErrorMessage);
        Assert.True(viewModel.IsDirty);
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
    }

    [Fact]
    public async Task CancelledOutcomeKeepsFreshVerifiedSnapshotCurrent()
    {
        var settings = CreateSettings(port: 7144);
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(
                settings,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelays: 1));
        controller.ApplyHandler = (_, _) =>
        {
            controller.SetSnapshot(CreateAppliedSnapshot(
                settings,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelays: 4));
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Cancelled);
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync();

        Assert.Equal(1, controller.ApplyCount);
        Assert.True(viewModel.IsRuntimeStatusCurrent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.IsDirty);
        Assert.True(viewModel.CanEditPort);
        Assert.True(viewModel.CanApply);
        var cancelled = GenesisNetworkSettingsPresentation.Create(viewModel);
        Assert.Equal("Stopped", cancelled.ListenerStateText);
        Assert.Equal("—", cancelled.BoundEndpointText);
        Assert.Equal("4 / 8", cancelled.RelayConnectionsText);
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

    private sealed class FakeNetworkSettingsController :
        IGenesisNetworkSettingsController
    {
        private GenesisNetworkSettingsAppliedSnapshot _snapshot;
        private int _applyCount;

        public FakeNetworkSettingsController(
            GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public Func<
            GenesisNetworkBasicSettings,
            CancellationToken,
            ValueTask<GenesisNetworkSettingsApplyOutcome>>? ApplyHandler
        { get; set; }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot => _snapshot;

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
