using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsViewModelTests
{
    [Fact]
    public void ConstructorProjectsAppliedSettingsAndCapabilities()
    {
        var applied = CreateAppliedSnapshot(
            CreateSettings(port: 7144),
            FullCapabilities,
            activeRelays: 2);
        var controller = new FakeNetworkSettingsController(applied);

        var viewModel = new GenesisNetworkSettingsViewModel(controller);

        Assert.Equal(7144, viewModel.Port);
        Assert.False(viewModel.Ipv6Enabled);
        Assert.False(viewModel.AutomaticPortMappingEnabled);
        Assert.True(viewModel.AllowLocalRelay);
        Assert.True(viewModel.AllowGlobalRelay);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanApply);
        Assert.True(viewModel.CanEditPort);
        Assert.True(viewModel.CanEditIpv6);
        Assert.True(viewModel.CanEditAutomaticPortMapping);
        Assert.True(viewModel.CanEditLocalRelay);
        Assert.True(viewModel.CanEditGlobalRelay);
        Assert.Equal(2, viewModel.RuntimeStatus.ActiveRelayConnections);
    }

    [Fact]
    public async Task UnsupportedBackendRejectsDraftChangeBeforeControllerCall()
    {
        var applied = CreateAppliedSnapshot(
            CreateSettings(port: 7144),
            GenesisRelayNetworkCapabilities.None,
            activeRelays: 0,
            listening: false);
        var controller = new FakeNetworkSettingsController(applied);
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        Assert.True(viewModel.IsDirty);
        Assert.False(viewModel.CanEditPort);
        Assert.False(viewModel.CanApply);

        await viewModel.ApplyAsync();

        Assert.Equal(
            "Application-wide listener configuration is not available.",
            viewModel.ErrorMessage);
        Assert.Equal(0, controller.ApplyCount);
        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public async Task InvalidPortIsRejectedWithoutControllerCall()
    {
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(CreateSettings(), FullCapabilities));
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 0,
        };

        Assert.False(viewModel.CanApply);

        await viewModel.ApplyAsync();

        Assert.Equal("Enter a valid port from 1 to 65535.", viewModel.ErrorMessage);
        Assert.Equal(0, controller.ApplyCount);
    }

    [Fact]
    public async Task AppliedOutcomePromotesControllerSnapshotAndClearsDraft()
    {
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(CreateSettings(), FullCapabilities));
        controller.ApplyHandler = (requested, _) =>
        {
            controller.SetSnapshot(
                CreateAppliedSnapshot(
                    requested,
                    FullCapabilities,
                    activeRelays: 1));
            return ValueTask.FromResult(
                GenesisNetworkSettingsApplyOutcome.Applied);
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
            Ipv6Enabled = true,
            AutomaticPortMappingEnabled = true,
            AllowGlobalRelay = false,
        };

        Assert.True(viewModel.CanApply);

        await viewModel.ApplyAsync();

        Assert.Equal(1, controller.ApplyCount);
        Assert.Equal(8000, controller.LastRequested?.Port);
        Assert.True(controller.LastRequested?.Ipv6Enabled);
        Assert.True(controller.LastRequested?.AutomaticPortMappingEnabled);
        Assert.False(controller.LastRequested?.AllowGlobalRelay);
        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.Ipv6Enabled);
        Assert.True(viewModel.AutomaticPortMappingEnabled);
        Assert.False(viewModel.AllowGlobalRelay);
        Assert.False(viewModel.IsDirty);
        Assert.False(viewModel.CanApply);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal(1, viewModel.RuntimeStatus.ActiveRelayConnections);
    }

    [Fact]
    public async Task RejectedOutcomeKeepsDraftButRefreshesAppliedRuntime()
    {
        var initial = CreateAppliedSnapshot(
            CreateSettings(),
            FullCapabilities,
            activeRelays: 0);
        var controller = new FakeNetworkSettingsController(initial);
        controller.ApplyHandler = (_, _) =>
        {
            controller.SetSnapshot(
                CreateAppliedSnapshot(
                    CreateSettings(),
                    FullCapabilities,
                    activeRelays: 3));
            return ValueTask.FromResult(
                GenesisNetworkSettingsApplyOutcome.Rejected);
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        await viewModel.ApplyAsync();

        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.IsDirty);
        Assert.Equal("Network settings were rejected.", viewModel.ErrorMessage);
        Assert.Equal(3, viewModel.RuntimeStatus.ActiveRelayConnections);
    }

    [Fact]
    public void RevertRestoresAppliedSettings()
    {
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(CreateSettings(), FullCapabilities));
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
            Ipv6Enabled = true,
            AllowGlobalRelay = false,
        };

        Assert.True(viewModel.IsDirty);

        viewModel.Revert();

        Assert.Equal(7144, viewModel.Port);
        Assert.False(viewModel.Ipv6Enabled);
        Assert.True(viewModel.AllowGlobalRelay);
        Assert.False(viewModel.IsDirty);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void RefreshStatusDoesNotOverwriteUnsavedDraft()
    {
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(
                CreateSettings(),
                FullCapabilities,
                activeRelays: 0));
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };
        controller.SetSnapshot(
            CreateAppliedSnapshot(
                CreateSettings(),
                FullCapabilities,
                activeRelays: 4));

        viewModel.RefreshStatus();

        Assert.Equal(8000, viewModel.Port);
        Assert.True(viewModel.IsDirty);
        Assert.Equal(4, viewModel.RuntimeStatus.ActiveRelayConnections);
    }

    [Fact]
    public async Task ConcurrentApplyAttemptDoesNotStartSecondControllerCommand()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new FakeNetworkSettingsController(
            CreateAppliedSnapshot(CreateSettings(), FullCapabilities));
        controller.ApplyHandler = async (requested, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            controller.SetSnapshot(
                CreateAppliedSnapshot(requested, FullCapabilities));
            return GenesisNetworkSettingsApplyOutcome.Applied;
        };
        var viewModel = new GenesisNetworkSettingsViewModel(controller)
        {
            Port = 8000,
        };

        var first = viewModel.ApplyAsync().AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsApplying);
        Assert.False(viewModel.CanEditPort);
        Assert.False(viewModel.CanApply);

        await viewModel.ApplyAsync();
        Assert.Equal(1, controller.ApplyCount);

        release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(viewModel.IsApplying);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(8000, viewModel.Port);
    }

    private static GenesisNetworkBasicSettings CreateSettings(
        int port = 7144,
        bool ipv6Enabled = false,
        bool automaticPortMappingEnabled = false,
        bool allowLocalRelay = true,
        bool allowGlobalRelay = true)
    {
        return new GenesisNetworkBasicSettings(
            port,
            ipv6Enabled,
            automaticPortMappingEnabled,
            allowLocalRelay,
            allowGlobalRelay);
    }

    private static GenesisNetworkSettingsAppliedSnapshot CreateAppliedSnapshot(
        GenesisNetworkBasicSettings settings,
        GenesisRelayNetworkCapabilities capabilities,
        int activeRelays = 0,
        bool listening = true)
    {
        var runtime = new GenesisRelayNetworkStatusSnapshot(
            capabilities,
            listening
                ? GenesisRelayListenerRuntimeState.Listening
                : GenesisRelayListenerRuntimeState.Unavailable,
            activeRelayConnections: activeRelays,
            relayConnectionLimit: 8,
            addressFamily: listening ? AddressFamily.InterNetwork : null,
            boundAddress: listening ? IPAddress.Any : null,
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

        public GenesisNetworkBasicSettings? LastRequested { get; private set; }

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _applyCount);
            LastRequested = settings;
            return ApplyHandler?.Invoke(settings, cancellationToken) ??
                ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
        }

        public void SetSnapshot(GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            _snapshot = snapshot;
        }
    }
}
