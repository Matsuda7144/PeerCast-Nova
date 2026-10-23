using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextPublishingCompositionTests
{
    [Fact]
    public void PublishingCommandsPermitOnlyNavigationAndStatusRefresh()
    {
        Assert.True(GenesisGuiNextAppShellBridge.IsPublishingNavigationCommand(GenesisGuiNextAppShellBridge.RefreshControlId));
        Assert.True(GenesisGuiNextAppShellBridge.IsPublishingNavigationCommand(1106));
        Assert.False(GenesisGuiNextAppShellBridge.IsPublishingNavigationCommand(GenesisGuiNextAppShellBridge.PlayControlId));
        Assert.False(GenesisGuiNextAppShellBridge.IsPublishingNavigationCommand(GenesisGuiNextAppShellBridge.RelayStartControlId));
        Assert.False(GenesisGuiNextAppShellBridge.IsPublishingNavigationCommand(GenesisGuiNextAppShellBridge.RelayStopControlId));
        Assert.False(GenesisGuiNextAppShellBridge.IsPublishingNavigationCommand(GenesisGuiNextAppShellBridge.RecordingHistoryRefreshControlId));
    }

    [Fact]
    public async Task RootReleasesPublishingAfterHostTeardownAndBeforeReturning()
    {
        var order = new List<string>();
        var publishing = new Factory();
        publishing.Owner.OnDispose = () => order.Add("publishing");
        var hosts = new RejectedHostFactory { OnDispose = () => order.Add("host") };
        var startup = new NovaWindowsStartupComposition(new(), hosts,
            () => new FakePlaybackSessionController(), publishingRuntimeFactory: publishing);
        Assert.Equal(NovaWindowsStartupResult.PlatformUnsupported, await startup.RunGenesisAsync());
        Assert.Collection(order, item => Assert.Equal("host", item), item => Assert.Equal("publishing", item));
        Assert.Equal(1, publishing.Creates);
        Assert.Equal(1, publishing.Owner.Disposals);
        Assert.Equal(0, publishing.Owner.Starts);
    }

    [Fact]
    public async Task SurfaceConstructionFailureStillReleasesUnstartedOwner()
    {
        var publishing = new Factory();
        var startup = new NovaWindowsStartupComposition(new(), new RejectedHostFactory(),
            () => new FakePlaybackSessionController(), publishingRuntimeFactory: publishing);
        Assert.Equal(NovaWindowsStartupResult.Faulted,
            await startup.RunOwnedGenesisAsync((_, _) => throw new InvalidOperationException("surface failure")));
        Assert.Equal(1, publishing.Creates);
        Assert.Equal(1, publishing.Owner.Disposals);
        Assert.Equal(0, publishing.Owner.Starts);
    }

    [Fact]
    public async Task PublishingViewCannotPlayOrStopAnExistingPlayback()
    {
        var catalog = new Catalog();
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var playback = new FakePlaybackSessionController();
        var vm = new GenesisMainViewModel(playback);
        await using var session = GenesisChannelCatalogSession.Create(catalog, vm,
            new GenesisGuiNextChannelPresencePublisher(tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance));
        var interaction = new GenesisMainWindowInteraction(vm, session);
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        Assert.False(interaction.IsYellowPageCatalogView);
        await interaction.PlaySourceAsync("must-not-play");
        Assert.Equal(PlaybackSessionState.Stopped, playback.Snapshot.State);
        await playback.PlayAsync("existing-playback");
        await interaction.StopAsync();
        Assert.Equal(PlaybackSessionState.Playing, playback.Snapshot.State);
        await interaction.RefreshCurrentViewAsync(Guid.NewGuid());
        Assert.Equal(0, catalog.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishingRowsShowObservedOrTypedTerminalFactsWithoutConnectionFields(bool terminal)
    {
        var factory = new Factory();
        factory.Owner.Current = factory.Owner.Current with
        {
            State = terminal ? ChannelSessionState.Stopped : ChannelSessionState.Receiving,
            HeadAvailable = !terminal,
            EndReason = terminal ? ObsPublishChannelEndReason.ProtocolRejected : null,
            RejectionStage = terminal ? PeerCast.Nova.Network.Rtmp.ObsRtmpRejectionStage.AacSequenceHeader : null,
            RejectionReason = terminal ? PeerCast.Nova.Network.Rtmp.ObsRtmpRejectionReason.UnsupportedAudioSpecificConfig : null,
        };
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(terminal ? NovaObsPublishingAvailability.Stopped : NovaObsPublishingAvailability.Observed,
            row.Publishing!.Availability);
        var values = GenesisGuiNextRecordingListPresentation.Values(row);
        Assert.Equal(factory.Owner.Current.ChannelId.ToString(), values[1]);
        Assert.Equal(factory.Owner.Current.SessionId.ToString(), values[2]);
        Assert.DoesNotContain("DO-NOT-DISPLAY", string.Join('|', values));
        if (terminal) Assert.Contains("UnsupportedAudioSpecificConfig", values[3]);
        Assert.False(row.IsInteractive);
        Assert.Equal(0, factory.Owner.Starts);
    }

    [Fact]
    public async Task OwnsOneUnstartedRuntimeAndReleasesExactlyOnce()
    {
        var factory = new Factory();
        var composition = NovaGuiNextPublishingComposition.Create(factory);
        Assert.Equal(1, factory.Creates);
        Assert.Equal(0, factory.Owner.Reads);
        Assert.Equal(NovaObsPublishingAvailability.NotStarted, composition.Status.Read().Availability);
        Assert.Equal(0, factory.Owner.Starts);
        await Task.WhenAll(composition.DisposeAsync().AsTask(), composition.DisposeAsync().AsTask());
        Assert.Equal(1, factory.Owner.Disposals);
        Assert.Equal(NovaObsPublishingAvailability.Stopped, composition.Status.Read().Availability);
        Assert.Equal(0, factory.Owner.Starts);
    }

    [Fact]
    public async Task ExistingProductionOwnerCanBeObservedAndDisposedWithoutStartingListeners()
    {
        await using var composition = NovaGuiNextPublishingComposition.Create(SystemNovaObsPublishingRuntimeFactory.Instance);
        var snapshot = composition.Status.Read();
        Assert.Equal(NovaObsPublishingAvailability.NotStarted, snapshot.Availability);
        Assert.NotNull(snapshot.ChannelId);
        Assert.NotNull(snapshot.SessionId);
        await composition.DisposeAsync();
        var terminal = composition.Status.Read();
        Assert.Equal(NovaObsPublishingAvailability.Stopped, terminal.Availability);
        Assert.Equal(snapshot.ChannelId, terminal.ChannelId);
        Assert.Equal(snapshot.SessionId, terminal.SessionId);
        Assert.Equal(ObsPublishChannelEndReason.HostCancelled, terminal.EndReason);
    }

    [Fact]
    public async Task FactoryFailureIsTruthfullyUnavailableAndDoesNotPreventCleanup()
    {
        await using var composition = NovaGuiNextPublishingComposition.Create(new Factory { Fails = true });
        var snapshot = composition.Status.Read();
        Assert.Equal(NovaObsPublishingAvailability.Unavailable, snapshot.Availability);
        Assert.Null(snapshot.ChannelId);
        Assert.DoesNotContain("SECRET", snapshot.DiagnosticText);
    }

    [Fact]
    public async Task NormalStartupRejectionStillReleasesOwnerAndNeverStartsIt()
    {
        var publishing = new Factory();
        var startup = new NovaWindowsStartupComposition(new(), new RejectedHostFactory(),
            () => new FakePlaybackSessionController(), publishingRuntimeFactory: publishing);
        Assert.Equal(NovaWindowsStartupResult.PlatformUnsupported, await startup.RunGenesisAsync());
        Assert.Equal(1, publishing.Creates);
        Assert.Equal(1, publishing.Owner.Disposals);
        Assert.Equal(0, publishing.Owner.Starts);
    }

    [Fact]
    public async Task StartupDisposalFailureIsNotReportedAsCompleted()
    {
        var publishing = new Factory();
        publishing.Owner.FailDispose = true;
        var startup = new NovaWindowsStartupComposition(new(), new RejectedHostFactory(),
            () => new FakePlaybackSessionController(), publishingRuntimeFactory: publishing);
        Assert.Equal(NovaWindowsStartupResult.Faulted, await startup.RunGenesisAsync());
        Assert.Equal(1, publishing.Owner.Disposals);
        Assert.Equal(0, publishing.Owner.Starts);
    }

    [Fact]
    public async Task NavigationAndSchemasKeepPublishingRowsReadOnlyAndLogsEnabled()
    {
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        tabs.BindPublishingStatus(composition.Status);
        Assert.True(GenesisGuiNextNavigationPresentation.TryResolveExistingView(6, out var publishing));
        Assert.Equal(GenesisGuiNextTab.Publishing, publishing);
        Assert.True(GenesisGuiNextNavigationPresentation.IsImplemented(8));
        tabs.SelectedTab = publishing;
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.NotNull(row.Publishing);
        Assert.Null(row.InteractiveChannel);
        Assert.False(row.IsInteractive);
        Assert.False(GenesisGuiNextFavoriteButtonPresentationResolver.Resolve(tabs, row).IsEnabled);
        Assert.Equal(4, GenesisGuiNextRecordingListPresentation.Columns(publishing, tabs.RecordingPageMode).Count);
        var values = GenesisGuiNextRecordingListPresentation.Values(row);
        Assert.Equal("未起動（待受未開始）", values[0]);
        Assert.Equal(factory.Owner.Current.ChannelId.ToString(), values[1]);
        Assert.DoesNotContain("DO-NOT-DISPLAY", string.Join('|', values));
        Assert.False(GenesisChannelDirectContextMenuDispatch.TryOpen(row,
            _ => throw new InvalidOperationException("Must not select channel"),
            _ => throw new InvalidOperationException("Must not open channel menu")));
        tabs.SelectedTab = GenesisGuiNextTab.Channels;
        Assert.Equal(7, GenesisGuiNextRecordingListPresentation.Columns(tabs.SelectedTab, tabs.RecordingPageMode).Count);
        tabs.SelectedTab = publishing;
        Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(0, factory.Owner.Starts);
    }

    [Fact]
    public async Task F5AndRefreshReadSnapshotsWithoutCatalogNetworkWorkAndDisposalRejectsLateReads()
    {
        var catalog = new Catalog();
        var factory = new Factory();
        await using var composition = NovaGuiNextPublishingComposition.Create(factory);
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        tabs.BindPublishingStatus(composition.Status, composition.Commands);
        var vm = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(catalog, vm,
            new GenesisGuiNextChannelPresencePublisher(tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance));
        var interaction = new GenesisMainWindowInteraction(vm, session);
        tabs.SelectedTab = GenesisGuiNextTab.Publishing;
        var rows = tabs.PublishingRows;
        await interaction.RefreshCurrentViewAsync();
        await interaction.RefreshCurrentViewAsync();
        Assert.Same(rows, tabs.PublishingRows);
        Assert.Equal(0, catalog.Reads);
        await tabs.DisposeAsync();
        var readCount = factory.Owner.Reads;
        tabs.RefreshPublishingStatus();
        Assert.Equal(readCount, factory.Owner.Reads);
        Assert.Equal(0, factory.Owner.Starts);
    }

    private sealed class Factory : INovaObsPublishingRuntimeFactory
    {
        public Owner Owner { get; } = new();
        public int Creates { get; private set; }
        public bool Fails { get; init; }
        public INovaObsPublishingRuntime Create()
        {
            Creates++;
            if (Fails) throw new InvalidOperationException("SECRET");
            return Owner;
        }
    }

    private sealed class Owner : INovaObsPublishingLifecycleRuntime
    {
        public ObsPublishStatusSnapshot Current { get; set; } = new(
            new ChannelId(Guid.NewGuid()), new ChannelSessionId(Guid.NewGuid()), 1,
            ChannelSessionState.Created, false, false, false,
            "rtmp://DO-NOT-DISPLAY", "DO-NOT-DISPLAY", null, null, null, null);
        public int Starts { get; private set; }
        public int Reads { get; private set; }
        public int Disposals { get; private set; }
        private readonly TaskCompletionSource<ObsPublishChannelResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ObsPublishChannelResult> Completion => _completion.Task;
        public bool FailDispose { get; set; }
        public Action? OnDispose { get; set; }
        public ObsPublishStatusSnapshot Snapshot { get { Reads++; return Current; } }
        public void Start(CancellationToken cancellationToken) { Starts++; throw new InvalidOperationException("Forbidden Start"); }
        public ValueTask DisposeAsync()
        {
            Disposals++;
            OnDispose?.Invoke();
            if (FailDispose) throw new InvalidOperationException("Cleanup failed");
            Current = Current with { Revision = 2, State = ChannelSessionState.Stopped, EndReason = ObsPublishChannelEndReason.HostCancelled };
            _completion.TrySetResult(new(ObsPublishChannelEndReason.HostCancelled, 0, 0, 0, 0,
                PeerCast.Nova.Network.Pcp.PcpRelayDrainOutcome.NotRequired, false, true, null, null));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Catalog : IGenesisChannelCatalog
    {
        public int Reads { get; private set; }
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty); }
    }

    private sealed class RejectedHostFactory : INovaWindowsStartupHostFactory
    {
        public Action? OnDispose { get; init; }
        public INovaWindowsStartupHost Create(PlaybackWindowsPhysicalRunnerOptions options) => new RejectedHost(OnDispose);
    }

    private sealed class RejectedHost(Action? onDispose) : INovaWindowsStartupHost
    {
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported);
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> WaitForExitAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Must not wait after rejection");
        public ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
        public ValueTask DisposeAsync() { onDispose?.Invoke(); return ValueTask.CompletedTask; }
    }
}
