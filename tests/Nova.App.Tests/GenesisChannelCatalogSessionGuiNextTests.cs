namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogSessionGuiNextTests
{
    [Fact]
    public async Task GuiNextPresencePublisherExposesSharedTabsToSession()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);

        Assert.Same(tabs, session.GuiNextTabs);
    }

    [Fact]
    public async Task NonGuiNextPublisherLeavesSharedTabsUnavailable()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);

        Assert.Null(session.GuiNextTabs);
    }

    [Fact]
    public async Task GuiNextSessionTracksExactInitialAndLivePlaybackSnapshots()
    {
        var initialSnapshot = new PlaybackSessionSnapshot(
            PlaybackSessionState.Faulted,
            "pcp://example/faulted",
            "Decoder failed.");
        var controller = new RecordingPlaybackSessionController(initialSnapshot);
        var viewModel = new GenesisMainViewModel(controller);
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);

        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);

        Assert.Empty(tabs.Playing);

        viewModel.SourceText = "pcp://example/playing";
        await viewModel.PlayAsync();

        var playing = Assert.Single(tabs.Playing);
        Assert.Equal(PlaybackSessionState.Playing, playing.State);
        Assert.Equal("pcp://example/playing", playing.Source);
        Assert.Null(playing.ErrorMessage);

        await viewModel.StopAsync();

        Assert.Empty(tabs.Playing);
    }

    [Fact]
    public async Task DisposedGuiNextSessionStopsPlaybackSynchronization()
    {
        var controller = new RecordingPlaybackSessionController(
            PlaybackSessionSnapshot.Stopped);
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/playing",
        };
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);

        await viewModel.PlayAsync();
        var playingBeforeDispose = Assert.Single(tabs.Playing);
        Assert.Equal(PlaybackSessionState.Playing, playingBeforeDispose.State);

        await session.DisposeAsync();
        await viewModel.StopAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        var playingAfterDispose = Assert.Single(tabs.Playing);
        Assert.Equal(PlaybackSessionState.Playing, playingAfterDispose.State);
        Assert.Equal("pcp://example/playing", playingAfterDispose.Source);
    }

    [Fact]
    public async Task DiagnosticsChangedPublishesAttemptAndFinalResultSnapshots()
    {
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);
        var observed = new List<GenesisChannelCatalogRefreshDiagnostics>();
        session.DiagnosticsChanged += (_, _) => observed.Add(session.Diagnostics);

        var result = await session.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.Collection(
            observed,
            attempt =>
            {
                Assert.NotNull(attempt.LastAttemptedAt);
                Assert.Null(attempt.LastSuccessfulAt);
                Assert.Null(attempt.LastResult);
            },
            completed =>
            {
                Assert.NotNull(completed.LastAttemptedAt);
                Assert.NotNull(completed.LastSuccessfulAt);
                Assert.Equal(
                    GenesisChannelCatalogRefreshResult.Completed,
                    completed.LastResult);
            });
    }

    [Fact]
    public async Task NativeStatusUsesDiagnosticsOnlyForGuiNextSession()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var legacySession = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);

        Assert.Equal(
            "Ready",
            SystemGenesisMainWindowInteractionBridgeFactory.CreateStatusText(
                viewModel,
                legacySession,
                DateTimeOffset.UtcNow));

        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        await using var guiNextSession = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher);
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await guiNextSession.RefreshAsync());
        var lastSuccessfulAt = Assert.IsType<DateTimeOffset>(
            guiNextSession.Diagnostics.LastSuccessfulAt);

        Assert.Equal(
            "Ready · YP: updated 12s ago",
            SystemGenesisMainWindowInteractionBridgeFactory.CreateStatusText(
                viewModel,
                guiNextSession,
                lastSuccessfulAt + TimeSpan.FromSeconds(12)));
    }

    private sealed class RecordingPlaybackSessionController : IPlaybackSessionController
    {
        public RecordingPlaybackSessionController(PlaybackSessionSnapshot initialSnapshot)
        {
            Snapshot = initialSnapshot;
        }

        public PlaybackSessionSnapshot Snapshot { get; private set; }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            Snapshot = new(PlaybackSessionState.Playing, source, null);
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            Snapshot = PlaybackSessionSnapshot.Stopped;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }
    }
}
