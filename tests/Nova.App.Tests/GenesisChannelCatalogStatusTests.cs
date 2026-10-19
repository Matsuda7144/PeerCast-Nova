namespace PeerCast.Nova.App.Tests;

public sealed class GenesisChannelCatalogStatusTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 21, 5, 17, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshShowsLoadingThenReturnsToReady()
    {
        var catalog = new PendingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        var refresh = coordinator.RefreshAsync().AsTask();
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(viewModel.IsChannelCatalogRefreshing);
        Assert.Equal("Loading channels...", viewModel.StatusText);

        catalog.Complete(GenesisChannelCatalogSnapshot.Empty);
        var result = await refresh;

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, result);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Null(viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public void ReadyPresentationAddsChannelCountWithoutChangingStatusContract()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels(
        [
            new GenesisChannelItem("One", "pcp://example/one"),
            new GenesisChannelItem("Two", "pcp://example/two"),
        ]);

        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Equal("Ready · 2 channels", GenesisStatusPresentation.Format(viewModel));
    }

    [Fact]
    public void ReadyPresentationComposesGuiNextRefreshDiagnostics()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromSeconds(13),
            LastSuccessfulAt: Now - TimeSpan.FromSeconds(12),
            LastResult: GenesisChannelCatalogRefreshResult.Completed);

        Assert.Equal(
            "Ready · YP: updated 12s ago",
            GenesisStatusPresentation.Format(viewModel, diagnostics, Now));
    }

    [Fact]
    public void GuiNextRefreshingPresentationDoesNotDuplicateLoadingState()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.SetChannelCatalogRefreshState(isRefreshing: true, errorMessage: null);
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now,
            LastSuccessfulAt: Now - TimeSpan.FromMinutes(2),
            LastResult: GenesisChannelCatalogRefreshResult.Completed);

        Assert.Equal(
            "Ready · YP: updating · last updated 2m ago",
            GenesisStatusPresentation.Format(viewModel, diagnostics, Now));
    }

    [Fact]
    public void GuiNextFailedPresentationDoesNotDuplicateCatalogFailure()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.SetChannelCatalogRefreshState(
            isRefreshing: false,
            errorMessage: "Channel refresh failed.");
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromSeconds(5),
            LastSuccessfulAt: Now - TimeSpan.FromHours(3),
            LastResult: GenesisChannelCatalogRefreshResult.Faulted);

        Assert.Equal(
            "Ready · YP: refresh failed 5s ago · last updated 3h ago",
            GenesisStatusPresentation.Format(viewModel, diagnostics, Now));
    }

    [Fact]
    public async Task PlaybackPresentationKeepsPlaybackPrimaryWithRefreshDiagnostics()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example/playback",
        };
        await viewModel.PlayAsync();
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromMinutes(1),
            LastSuccessfulAt: Now - TimeSpan.FromMinutes(1),
            LastResult: GenesisChannelCatalogRefreshResult.Completed);

        Assert.Equal(
            "Playing · YP: updated 1m ago",
            GenesisStatusPresentation.Format(viewModel, diagnostics, Now));
    }

    [Fact]
    public async Task GuiNextFailedPresentationKeepsPlaybackPrimaryWithoutDuplicateFailure()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example/playback",
        };
        await viewModel.PlayAsync();
        viewModel.SetChannelCatalogRefreshState(
            isRefreshing: false,
            errorMessage: "Channel refresh failed.");
        var diagnostics = new GenesisChannelCatalogRefreshDiagnostics(
            LastAttemptedAt: Now - TimeSpan.FromSeconds(5),
            LastSuccessfulAt: null,
            LastResult: GenesisChannelCatalogRefreshResult.Faulted);

        Assert.Equal(
            "Playing · YP: refresh failed 5s ago",
            GenesisStatusPresentation.Format(viewModel, diagnostics, Now));
    }

    [Fact]
    public async Task FailedRefreshPreservesRowsAndSurfacesCatalogStatus()
    {
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        var coordinator = new GenesisChannelCatalogCoordinator(
            new FaultingCatalog(),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Faulted, result);
        Assert.False(viewModel.IsChannelCatalogRefreshing);
        Assert.Equal("Channel refresh failed.", viewModel.ChannelCatalogErrorMessage);
        Assert.Equal("Channel refresh failed.", viewModel.StatusText);
        Assert.Equal("Channel refresh failed.", GenesisStatusPresentation.Format(viewModel));
        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
    }

    [Fact]
    public async Task FailedRefreshRemainsVisibleDuringPlayback()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example/playback",
        };
        await viewModel.PlayAsync();
        var coordinator = new GenesisChannelCatalogCoordinator(
            new FaultingCatalog(),
            viewModel);

        var result = await coordinator.RefreshAsync();

        Assert.Equal(GenesisChannelCatalogRefreshResult.Faulted, result);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal("Playing", viewModel.StatusText);
        Assert.Equal("Channel refresh failed.", viewModel.ChannelCatalogErrorMessage);
        Assert.Equal(
            "Playing: Channel refresh failed.",
            GenesisStatusPresentation.Format(viewModel));
    }

    [Fact]
    public async Task PlaybackStatusTakesPriorityOverCatalogLoading()
    {
        var catalog = new PendingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example/playback",
        };
        viewModel.ReplaceChannels(
        [
            new GenesisChannelItem("Existing", "pcp://example/existing"),
        ]);
        var coordinator = new GenesisChannelCatalogCoordinator(catalog, viewModel);

        var refresh = coordinator.RefreshAsync().AsTask();
        await catalog.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await viewModel.PlayAsync();

        Assert.True(viewModel.IsChannelCatalogRefreshing);
        Assert.Equal("Playing", viewModel.StatusText);
        Assert.Equal("Playing", GenesisStatusPresentation.Format(viewModel));

        catalog.Complete(GenesisChannelCatalogSnapshot.Empty);
        _ = await refresh;
    }

    private sealed class PendingCatalog : IGenesisChannelCatalog
    {
        private readonly TaskCompletionSource<GenesisChannelCatalogSnapshot> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return new(_completion.Task.WaitAsync(cancellationToken));
        }

        public void Complete(GenesisChannelCatalogSnapshot snapshot) =>
            _completion.TrySetResult(snapshot);
    }

    private sealed class FaultingCatalog : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<GenesisChannelCatalogSnapshot>(
                new InvalidOperationException("Catalog failed."));
    }
}
