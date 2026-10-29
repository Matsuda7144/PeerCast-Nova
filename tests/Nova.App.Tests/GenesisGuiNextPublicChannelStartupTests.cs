using System.Net;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Windows;
using static PeerCast.Nova.App.Tests.PublicChannelTestFixture;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPublicChannelStartupTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task SharedGenesisStartupPassesInertOwnerBoundaryBeforeBridgeAndOwnsShutdown(
        bool hasState, bool rejectBridge, bool throwBridge)
    {
        await using var fixture = await CreateAsync(new() { ListenPort = 7144, MaxRelaysTotal = 0 });
        await using var producer = new PcpRelayFanout();
        GenesisMainViewModel? model = null;
        GenesisGuiNextPublicChannelComposition.Owner? prepared = null;
        var bridge = new Bridge(viewModel =>
        {
            model = viewModel;
            if (hasState)
            {
                var channels = Assert.IsType<GenesisGuiNextPublicChannelComposition>(viewModel.PublicChannels);
                Assert.Same(fixture.Channels, channels);
                Assert.Same(fixture.State.RelayActivation, viewModel.RelayActivation);
                Assert.True(viewModel.RelaySnapshot.CleanupConfirmed);
                Assert.Same(fixture.State.DownstreamRelays, channels.Registry);
                Assert.Equal(0, channels.OwnerCount);
                prepared = channels.PrepareBroadcastAsync(new(Guid.NewGuid()), new(Guid.NewGuid()),
                    producer, new(IPAddress.Any, 0)).AsTask().GetAwaiter().GetResult();
                Assert.False(prepared.ActivationStarted);
                Assert.Equal(PcpDownstreamRelayServerState.Created, prepared.DownstreamSnapshot.State);
                Assert.Throws<InvalidOperationException>(() => prepared.LocalEndPoint);
            }
            else Assert.Null(viewModel.PublicChannels);
        }, rejectBridge, throwBridge);
        var surface = new SystemGenesisInteractiveMainWindowSurfaceTarget(new Surface(), bridge,
            EmptyGenesisChannelCatalog.Instance, TimeSpan.FromDays(1),
            _ => NoopLifetime.Instance, _ => hasState ? fixture.State : null,
            static (_, _) => GenesisChannelNotificationRuntime.Empty, _ => null,
            GenesisUnavailableNetworkSettingsController.Instance);
        var host = new Host(() =>
        {
            Assert.NotNull(model);
            if (hasState)
            {
                Assert.False(prepared!.ActivationStarted);
                Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
            }
        });
        var composition = new NovaWindowsStartupComposition(new(), new HostFactory(host));
        // Same RunGenesisAsync -> host/projection -> interactive surface path as
        // normal startup, with only native host/surface and stores replaced.
        var result = await composition.RunGenesisAsync(surface, new FakePlaybackSessionController());

        Assert.Equal(rejectBridge || throwBridge ? NovaWindowsStartupResult.Faulted : NovaWindowsStartupResult.Completed, result);
        Assert.Equal(1, host.StartCount);
        Assert.True(host.Stopped);
        Assert.True(host.Disposed);
        Assert.NotNull(model);
        Assert.Null(model.PublicChannels); // No stale command boundary after close/rejection.
        Assert.Null(model.RelayActivation);
        if (hasState)
        {
            Assert.True(fixture.Channels.IsDisposed);
            Assert.True(prepared!.IsDisposed);
            Assert.False(prepared.ActivationStarted);
            Assert.Equal(0, fixture.Channels.OwnerCount);
        }
        else Assert.False(fixture.Channels.IsDisposed);
        Assert.Equal(1, fixture.Store.LoadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BorrowedStateKeepsOneRegistryAliveUntilApplicationRootDisposes(bool rejectBridge)
    {
        await using var fixture = await CreateAsync();
        var model = new GenesisMainViewModel(new FakePlaybackSessionController());
        var surface = new SystemGenesisInteractiveMainWindowSurfaceTarget(new Surface(), new Bridge(_ => { }, rejectBridge),
            EmptyGenesisChannelCatalog.Instance, TimeSpan.FromDays(1),
            _ => NoopLifetime.Instance, _ => fixture.State,
            static (_, _) => GenesisChannelNotificationRuntime.Empty, _ => null,
            GenesisUnavailableNetworkSettingsController.Instance, ownsGuiNextState: false);
        var lifetime = await surface.OpenAsync(123, model, CancellationToken.None);
        if (lifetime is not null) await lifetime.DisposeAsync();
        Assert.False(fixture.State.PublicChannels.IsDisposed);
        await using var fanout = new PcpRelayFanout();
        var factory = new SystemNovaObsPublishingRuntimeFactory(fixture.State);
        using var registration = factory.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), fanout);
        Assert.Same(fixture.State.DownstreamRelays, fixture.State.PublicChannels.Registry);
        await fixture.DisposeAsync();
        Assert.Throws<PeerCast.Nova.Network.Publishing.ObsPublishLanException>(() =>
            factory.Register(new(Guid.NewGuid()), new(Guid.NewGuid()), fanout));
    }

    [Fact]
    public async Task WindowLifetimeCloseStopsRealRelayAndRemovesCommandBoundary()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var fixture = await CreateAsync();
        await using var upstream = new RelayActivationTestUpstream(timeout.Token);
        var model = new GenesisMainViewModel(new FakePlaybackSessionController());
        var surface = new SystemGenesisInteractiveMainWindowSurfaceTarget(new Surface(), new Bridge(_ => { }, false),
            EmptyGenesisChannelCatalog.Instance, TimeSpan.FromDays(1),
            _ => NoopLifetime.Instance, _ => fixture.State,
            static (_, _) => GenesisChannelNotificationRuntime.Empty, _ => null,
            GenesisUnavailableNetworkSettingsController.Instance);
        var lifetime = await surface.OpenAsync(42, model, timeout.Token);
        Assert.NotNull(lifetime);
        var start = GenesisGuiNextRelayCommandDispatch.StartAsync(model, upstream.Row);
        await upstream.SendAsync(timeout.Token);
        Assert.True(await start.WaitAsync(timeout.Token));
        var owner = model.RelayActivation!.CurrentOwner!;
        await using var peer = await Peer.ConnectAsync(owner, timeout.Token);
        await lifetime.DisposeAsync();
        Assert.Null(model.RelayActivation);
        Assert.False(await GenesisGuiNextRelayCommandDispatch.StartAsync(model, upstream.Row));
        Assert.True(owner.CleanupConfirmed);
        Assert.Equal(0, fixture.State.DownstreamRelays.ActiveRelayCount);
    }

    private sealed class Surface : IGenesisMainWindowSurfaceTarget
    {
        public ValueTask<IAsyncDisposable?> OpenAsync(long windowToken, GenesisMainViewModel viewModel, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(NoopLifetime.Instance);
    }
    private sealed class Bridge(Action<GenesisMainViewModel> opened, bool reject, bool fail = false) : IGenesisMainWindowInteractionBridgeFactory
    {
        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel)
        {
            opened(viewModel);
            if (fail) throw new InvalidOperationException("Bridge creation failed.");
            return reject ? null : NoopLifetime.Instance;
        }
    }
    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class HostFactory(Host host) : INovaWindowsStartupHostFactory
    {
        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options) => host;
    }
    private sealed class Host(Action wait) : INovaWindowsStartupHost, IGenesisMainWindowDispatcher
    {
        public long WindowToken => 42;
        public int StartCount { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Started);
        }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(CancellationToken cancellationToken)
        {
            wait();
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            return ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation, CancellationToken cancellationToken) =>
            new(GenesisMainWindowDispatcherResult.Completed, await operation(WindowToken, cancellationToken));
    }
}
