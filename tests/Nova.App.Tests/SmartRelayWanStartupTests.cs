using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App.Tests;

public sealed partial class SmartRelayWanTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupBindsWithoutChannelAndKeepsPortUntilApplicationShutdown(bool autoMapping)
    {
        await using var test = await Fixture.Create();
        test.Manager.SetEnabled(autoMapping);
        Assert.True(await test.Base.Channels.StartPublicAsync());
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => test.Base.Channels.StartPublicAsync()));
        Assert.Equal(test.Port, test.Host!.LocalEndPoint!.Port);
        Assert.Equal(GenesisSharedPublicRelayState.Listening, test.Host.Snapshot.State);
        Assert.Equal(0, test.Host.Snapshot.Channels);
        Assert.Equal(0, test.Base.Channels.OwnerCount);
        Assert.Equal(0, test.Base.State.DownstreamRelays.ActiveRelayCount);
        await test.Manager.ReconcileAsync(test.Token);
        Assert.Equal(autoMapping ? 1 : 0, test.Router.Calls.Count);
        Assert.Equal(new[] { test.Port }, test.Manager.ReadState().RegisteredInternalPorts);

        await using (var absent = await WirePeer.Connect(test.Port, new(Guid.NewGuid()), test.Token))
            Assert.Equal(404, absent.Handshake.HttpStatusCode);
        await using (var channel = await test.Base.ViewingAsync(test.Token))
        {
            await channel.Owner.PublicReady.WaitAsync(test.Token);
            await using var child = await WirePeer.Connect(test.Port, channel.Owner.ChannelId, test.Token);
            Assert.Equal(200, child.Handshake.HttpStatusCode);
            await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x64616568, 0, [7]), test.Token);
            await channel.ProducerFanout.ConsumeAsync(Packet(channel.Owner, 0x61746164, 100, [8, 9]), test.Token);
            Assert.Equal(new byte[] { 7 }, await child.Payload(test.Token));
            Assert.Equal(new byte[] { 8, 9 }, await child.Payload(test.Token));
            await child.Quit(test.Token);
        }
        Assert.Equal(0, test.Host.Snapshot.Channels);
        Assert.Equal(GenesisSharedPublicRelayState.Listening, test.Host.Snapshot.State);
        await test.Manager.ReconcileAsync(test.Token);
        Assert.DoesNotContain(test.Router.Calls, c => c.Operation == NatMappingOperation.Unmap);

        await test.Host.DisposeAsync();
        await test.Manager.ReconcileAsync(test.Token);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        Assert.Equal(autoMapping ? 1 : 0, test.Router.Calls.Count(c => c.Operation == NatMappingOperation.Unmap));
        Assert.Equal(GenesisSharedPublicRelayState.Disposed, test.Host.Snapshot.State);
        Assert.False(await test.Base.Channels.StartPublicAsync());
    }

    [Fact]
    public async Task StartupOffRemainsInertUntilSavedOnWhichStartsListenerImmediately()
    {
        await using var test = await Fixture.Create(wan: false);
        Assert.True(await test.Base.Channels.StartPublicAsync());
        Assert.Null(test.Host);
        Assert.True((await test.Base.State.Connection.SaveAsync(
            GenesisGuiNextConnectionInput.From(test.Base.State.Connection.Saved) with { WanRelayEnabled = true })).Saved);
        Assert.NotNull(test.Host);
        Assert.Equal(GenesisSharedPublicRelayState.Listening, test.Host.Snapshot.State);
        Assert.Equal(new[] { test.Port }, test.Manager.ReadState().RegisteredInternalPorts);
    }

    [Fact]
    public async Task StartupBindFailureRetainsSafeErrorAndDoesNotRequestMapping()
    {
        await using var test = await Fixture.Create();
        using var occupied = new TcpListener(IPAddress.Any, test.Port);
        occupied.Start();
        Assert.False(await test.Base.Channels.StartPublicAsync());
        Assert.Equal(GenesisSharedPublicRelayState.Failed, test.Host!.Snapshot.State);
        Assert.NotNull(test.Host.Snapshot.Error);
        Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        Assert.Empty(test.Router.Calls);
        Assert.Equal(0, test.Base.Channels.OwnerCount);
        occupied.Stop();
        Assert.True(await test.Base.Channels.StartPublicAsync());
        Assert.Null(test.Host.Snapshot.Error);
        Assert.Equal(GenesisSharedPublicRelayState.Listening, test.Host.Snapshot.State);
    }

    [Fact]
    public async Task ApplicationStopCompensatesLateStartupMapAndPreservesOtherDemand()
    {
        await using var test = await Fixture.Create();
        Assert.True(await test.Base.Channels.StartPublicAsync());
        var held = test.Router.HoldMap = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var mapping = test.Manager.ReconcileAsync(test.Token);
        try
        {
            await PublicChannelTestFixture.UntilAsync(() => !test.Router.Calls.IsEmpty, test.Token);
            await test.Host!.DisposeAsync();
            Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
        }
        finally { held.TrySetResult(new(NatBackendOutcome.Success, test.Port, 30)); }
        await mapping.WaitAsync(test.Token);
        Assert.Contains(test.Router.Calls, c => c.Operation == NatMappingOperation.Unmap);

        await using var other = await Fixture.Create();
        Assert.True(await other.Base.Channels.StartPublicAsync());
        using var demand = other.Manager.AcquirePublicTcpListener(new(IPAddress.Any, other.Port));
        await other.Manager.ReconcileAsync(other.Token);
        await other.Host!.DisposeAsync();
        await other.Manager.ReconcileAsync(other.Token);
        Assert.DoesNotContain(other.Router.Calls, c => c.Operation == NatMappingOperation.Unmap);
        demand.Dispose();
        await other.Manager.ReconcileAsync(other.Token);
        Assert.Contains(other.Router.Calls, c => c.Operation == NatMappingOperation.Unmap);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task AcceptedWindowStartsSharedListenerAndNetworkOwnerOwnsShutdown(bool rejectBridge, bool conflict)
    {
        await using var test = await Fixture.Create();
        var settingsPath = Path.Combine(Path.GetTempPath(), $"nova-startup-{Guid.NewGuid():N}.json");
        var store = new NovaSettingsStore(settingsPath);
        using var occupied = new TcpListener(IPAddress.Any, test.Port);
        if (conflict) occupied.Start();
        await using var owner = new NovaApplicationNetworkOwner(store, readEnvironment: () => null,
            createRuntime: _ => new StartupNatRuntime(test.Manager));
        try
        {
            await store.SaveAsync(new NovaOptions { AutoPortMapping = true });
            Assert.True((await owner.InitializeAsync()).Initialized);
            var model = new GenesisMainViewModel(new FakePlaybackSessionController(),
                hasPlaybackWindowSurface: false,
                networkSettingsAdapter: new GuiNextNetworkSettingsAdapter(owner));
            var surface = new SystemGenesisInteractiveMainWindowSurfaceTarget(new StartupSurface(), new StartupBridge(rejectBridge),
                EmptyGenesisChannelCatalog.Instance, TimeSpan.FromDays(1),
                _ => StartupLifetime.Instance, _ => test.Base.State,
                static (_, _) => GenesisChannelNotificationRuntime.Empty, _ => null,
                GenesisUnavailableNetworkSettingsController.Instance, ownsGuiNextState: false);
            var window = await surface.OpenAsync(42, model, test.Token);
            var snapshot = owner.ReadSnapshot();
            Assert.Equal(rejectBridge ? NovaApplicationPublicListenerAvailability.Unavailable : conflict
                ? NovaApplicationPublicListenerAvailability.Failed : NovaApplicationPublicListenerAvailability.Listening,
                snapshot.PublicListenerAvailability);
            if (rejectBridge) Assert.Null(snapshot.SharedPublicRelay);
            else
            {
                Assert.NotNull(snapshot.SharedPublicRelay);
                Assert.Equal(0, snapshot.SharedPublicRelay.Channels);
                Assert.Equal(conflict ? "エラー" : "待受中・到達未確認",
                    GenesisGuiNextTopStatusPresentation.Create(null, snapshot).Port.Text);
            }
            Assert.Equal(0, test.Base.Channels.OwnerCount);
            if (window is not null) await window.DisposeAsync();
            await owner.DisposeAsync();
            Assert.Empty(test.Manager.ReadState().RegisteredInternalPorts);
            Assert.True(owner.ReadSnapshot().Disposed);
            if (!rejectBridge) Assert.Equal(GenesisSharedPublicRelayState.Disposed, owner.ReadSnapshot().SharedPublicRelay!.State);
        }
        finally { File.Delete(settingsPath); }
    }

    private sealed class StartupNatRuntime(NatPortMappingManager manager) : INovaApplicationNatRuntime
    {
        public NatPortMappingManager Manager => manager;
        public NatShutdownOutcome? ShutdownOutcome => null;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class StartupSurface : IGenesisMainWindowSurfaceTarget
    {
        public ValueTask<IAsyncDisposable?> OpenAsync(long windowToken, GenesisMainViewModel viewModel, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable?>(StartupLifetime.Instance);
    }
    private sealed class StartupBridge(bool reject) : IGenesisMainWindowInteractionBridgeFactory
    {
        public IAsyncDisposable? Create(long windowToken, GenesisMainViewModel viewModel) => reject ? null : StartupLifetime.Instance;
    }
    private sealed class StartupLifetime : IAsyncDisposable
    {
        internal static StartupLifetime Instance { get; } = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
