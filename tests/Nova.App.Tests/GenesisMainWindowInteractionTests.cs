namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainWindowInteractionTests
{
    [Fact]
    public async Task ManualSourcePlayUsesTrimmedSourceThroughViewModel()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);

        await interaction.PlaySourceAsync("  pcp://example/channel  ");

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal("pcp://example/channel", viewModel.SourceText);
        Assert.Equal("pcp://example/channel", viewModel.CurrentSource);
        Assert.True(viewModel.IsPlaybackWindowOpen);
    }

    [Fact]
    public async Task RefreshChannelsUsesConfiguredCatalogSession()
    {
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]");
        var catalog = new SequencedCatalog(
            GenesisChannelCatalogSnapshot.Empty,
            new([channel]));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var session = GenesisChannelCatalogSession.Start(catalog, viewModel);
        var interaction = new GenesisMainWindowInteraction(viewModel, session);

        await interaction.RefreshChannelsAsync();

        Assert.Equal(2, catalog.ReadCount);
        Assert.Single(viewModel.Channels);
        Assert.Same(channel, viewModel.Channels[0]);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public async Task RefreshChannelsWithoutCatalogSessionIsNoOp()
    {
        var existing = new GenesisChannelItem(
            "Existing",
            "pcp://example/existing");
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        viewModel.ReplaceChannels([existing]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        await interaction.RefreshChannelsAsync();

        Assert.Single(viewModel.Channels);
        Assert.Same(existing, viewModel.Channels[0]);
        Assert.Equal("Ready", viewModel.StatusText);
    }

    [Fact]
    public void PlaybackPresentationCommandsAreRejectedWhileClosed()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);

        Assert.False(interaction.ShowVideoOnly());
        Assert.False(interaction.ShowFullscreen());
        Assert.False(interaction.RestorePlaybackWindow());
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(GenesisPlaybackWindowMode.Normal, viewModel.PlaybackWindowMode);
    }

    [Fact]
    public async Task PlaybackPresentationCommandsChangeOpenWindowMode()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);
        await interaction.PlaySourceAsync("pcp://example/channel");

        Assert.True(interaction.ShowVideoOnly());
        Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, viewModel.PlaybackWindowMode);

        Assert.True(interaction.ShowFullscreen());
        Assert.Equal(GenesisPlaybackWindowMode.Fullscreen, viewModel.PlaybackWindowMode);

        Assert.True(interaction.RestorePlaybackWindow());
        Assert.Equal(GenesisPlaybackWindowMode.Normal, viewModel.PlaybackWindowMode);
    }

    [Fact]
    public void SelectionCopiesChannelSourceWithoutStartingPlayback()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]");
        viewModel.ReplaceChannels([channel]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var selected = interaction.SelectChannel(0);

        Assert.True(selected);
        Assert.Same(channel, viewModel.SelectedChannel);
        Assert.Equal(channel.Source, viewModel.SourceText);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
    }

    [Fact]
    public async Task ActivationSelectsChannelAndRequestsPlayback()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]");
        viewModel.ReplaceChannels([channel]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateChannelAsync(0);

        Assert.True(activated);
        Assert.Same(channel, viewModel.SelectedChannel);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal(channel.Source, viewModel.CurrentSource);
    }

    [Fact]
    public async Task ActivationThenPlaybackWindowCloseStopsFakeSession()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem(
            "Example",
            "pcp://example/channel",
            "[game - example]");
        viewModel.ReplaceChannels([channel]);
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateChannelAsync(0);
        Assert.True(activated);
        Assert.True(viewModel.IsPlaybackWindowOpen);

        await viewModel.ClosePlaybackWindowAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Null(viewModel.PlaybackWindowSource);
    }

    [Fact]
    public async Task InvalidActivationDoesNotChangePlaybackState()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var activated = await interaction.ActivateChannelAsync(0);

        Assert.False(activated);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.Null(viewModel.SelectedChannel);
    }

    [Fact]
    public async Task StopClosesFakePlaybackState()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var interaction = new GenesisMainWindowInteraction(viewModel);
        await interaction.PlaySourceAsync("pcp://example/channel");

        await interaction.StopAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    private sealed class SequencedCatalog : IGenesisChannelCatalog
    {
        private readonly Queue<GenesisChannelCatalogSnapshot> _snapshots;
        private int _readCount;

        public SequencedCatalog(params GenesisChannelCatalogSnapshot[] snapshots)
        {
            _snapshots = new(snapshots);
        }

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return ValueTask.FromResult(_snapshots.Dequeue());
        }
    }
}
