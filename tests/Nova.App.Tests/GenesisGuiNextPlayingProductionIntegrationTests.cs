namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPlayingProductionIntegrationTests
{
    private const string ChannelId = "531DC8DFC7FB42928AC2C0A626517A87";
    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

    [Fact]
    public async Task SelectedChannelLifecycleProjectsOnlyCurrentOwnedChannel()
    {
        await using var harness = new PlaybackHarness();
        var channel = new GenesisChannelItem(
            "Selected station",
            Source,
            Description: "Known description",
            Type: "FLV",
            ChannelId: ChannelId);
        var playRelease = NewSignal();
        harness.Daily.PlayAsyncHandler = (_, cancellationToken) =>
            playRelease.Task.WaitAsync(cancellationToken);
        harness.Main.ReplaceChannels([channel]);
        harness.Main.SelectedChannel = channel;
        harness.Tabs.SelectedTab = GenesisGuiNextTab.Playing;

        var play = harness.Main.PlayAsync().AsTask();
        await harness.Daily.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            AssertPlayingProjection(
                harness,
                PlaybackSessionState.Starting,
                channel.Name,
                channel.Source,
                channel.ChannelId);
        }
        finally
        {
            playRelease.TrySetResult();
        }

        await play.WaitAsync(TimeSpan.FromSeconds(5));

        AssertPlayingProjection(
            harness,
            PlaybackSessionState.Playing,
            channel.Name,
            channel.Source,
            channel.ChannelId);

        var stopRelease = NewSignal();
        harness.Daily.StopAsyncHandler = () =>
        {
            harness.Daily.StopStarted.TrySetResult();
            return stopRelease.Task;
        };
        var stop = harness.Main.StopAsync().AsTask();
        await harness.Daily.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            AssertPlayingProjection(
                harness,
                PlaybackSessionState.Stopping,
                channel.Name,
                channel.Source,
                channel.ChannelId);
        }
        finally
        {
            stopRelease.TrySetResult();
        }

        await stop.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(harness.Tabs.Playing);
        Assert.Empty(GenesisGuiNextNativeTabProjection.Create(
            harness.Tabs,
            GenesisGuiNextTab.Playing));
    }

    [Fact]
    public async Task NaturalTerminationRemovesOwnedPlayingRow()
    {
        await using var harness = new PlaybackHarness();
        harness.Main.SourceText = Source;
        harness.Tabs.SelectedTab = GenesisGuiNextTab.Playing;

        await harness.Main.PlayAsync();
        Assert.Single(harness.Tabs.Playing);

        harness.Daily.CompletePlayback(failed: false);

        Assert.Equal(PlaybackSessionState.Stopped, harness.Controller.Snapshot.State);
        Assert.Empty(harness.Tabs.Playing);
    }

    [Fact]
    public async Task FailedPlaybackDoesNotRemainInPlayingTab()
    {
        await using var harness = new PlaybackHarness();
        harness.Daily.PlayAsyncException = new InvalidOperationException("startup failed");
        harness.Main.SourceText = Source;
        harness.Tabs.SelectedTab = GenesisGuiNextTab.Playing;

        await harness.Main.PlayAsync();

        Assert.Equal(PlaybackSessionState.Faulted, harness.Controller.Snapshot.State);
        Assert.Empty(harness.Tabs.Playing);
    }

    [Fact]
    public async Task ManualPcpPlaybackUsesKnownIdentityAndSourceAsFallbackName()
    {
        await using var harness = new PlaybackHarness();
        harness.Main.SourceText = Source;
        harness.Tabs.SelectedTab = GenesisGuiNextTab.Playing;

        await harness.Main.PlayAsync();

        var entry = Assert.Single(harness.Tabs.Playing);
        Assert.Equal(Source, entry.Source);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(
            harness.Tabs,
            GenesisGuiNextTab.Playing));
        Assert.Equal(Source, row.DisplayChannel.Name);
        Assert.Equal(Source, row.DisplayChannel.Source);
        Assert.Equal(ChannelId, row.DisplayChannel.ChannelId);
        Assert.Null(row.InteractiveChannel);
    }

    [Fact]
    public async Task StalePlayOutcomeCannotReplaceCurrentPlayingProjection()
    {
        var currentSource =
            "pcp://current.example:7144/channel/11111111111141118111111111111111";
        var controller = new ControlledSnapshotPlaybackController();
        var main = new GenesisMainViewModel(controller) { SourceText = Source };
        var tabs = new GenesisGuiNextTabsViewModel(
            new GenesisFavoriteChannelCollection())
        {
            SelectedTab = GenesisGuiNextTab.Playing,
        };
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        await using var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            main,
            publisher);

        var play = main.PlayAsync().AsTask();
        await controller.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.Publish(new(
            PlaybackSessionState.Playing,
            currentSource,
            null));
        controller.PublishNotificationWithoutChangingSnapshot();
        controller.CompletePlay(new(
            PlaybackSessionCommandResult.Completed,
            new(PlaybackSessionState.Playing, Source, null)));
        await play.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(currentSource, main.CurrentSource);
        var entry = Assert.Single(tabs.Playing);
        Assert.Equal(currentSource, entry.Source);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.Equal(currentSource, row.DisplayChannel.Source);
        Assert.DoesNotContain(Source, row.DisplayChannel.DetailsText, StringComparison.Ordinal);
    }

    private static void AssertPlayingProjection(
        PlaybackHarness harness,
        PlaybackSessionState state,
        string? name,
        string source,
        string? channelId)
    {
        var entry = Assert.Single(harness.Tabs.Playing);
        Assert.Equal(state, entry.State);
        Assert.Equal(source, entry.Source);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(
            harness.Tabs,
            GenesisGuiNextTab.Playing));
        Assert.Equal(name, row.DisplayChannel.Name);
        Assert.Equal(source, row.DisplayChannel.Source);
        Assert.Equal(channelId, row.DisplayChannel.ChannelId);
        Assert.Contains(state.ToString(), row.DisplayChannel.DetailsText);
        Assert.Null(row.InteractiveChannel);
    }

    private static TaskCompletionSource NewSignal() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class PlaybackHarness : IAsyncDisposable
    {
        public PlaybackHarness()
        {
            Daily = new RecordingDailyPlaybackController();
            Controller = new GuiNextDailyPlaybackSessionController(Daily);
            Main = new GenesisMainViewModel(Controller);
            Tabs = new GenesisGuiNextTabsViewModel(
                new GenesisFavoriteChannelCollection());
            var publisher = new GenesisGuiNextChannelPresencePublisher(
                Tabs,
                EmptyGenesisChannelPresenceNotificationPublisher.Instance);
            Session = GenesisChannelCatalogSession.Create(
                EmptyGenesisChannelCatalog.Instance,
                Main,
                publisher);
        }

        public RecordingDailyPlaybackController Daily { get; }

        public GuiNextDailyPlaybackSessionController Controller { get; }

        public GenesisMainViewModel Main { get; }

        public GenesisGuiNextTabsViewModel Tabs { get; }

        private GenesisChannelCatalogSession Session { get; }

        public async ValueTask DisposeAsync()
        {
            await Session.DisposeAsync();
            await Controller.DisposeAsync();
        }
    }

    private sealed class RecordingDailyPlaybackController : INovaDailyPlaybackController
    {
        private readonly TaskCompletionSource<GenesisChannelItem> _playStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded;

        public TaskCompletionSource<GenesisChannelItem> PlayStarted => _playStarted;

        public TaskCompletionSource StopStarted { get; } = NewSignal();

        public GenesisChannelItem? PlayedChannel { get; private set; }

        public Func<GenesisChannelItem, CancellationToken, Task>? PlayAsyncHandler { get; set; }

        public Func<Task>? StopAsyncHandler { get; set; }

        public Exception? PlayAsyncException { get; set; }

        public async Task PlayAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken)
        {
            PlayedChannel = channel;
            _playStarted.TrySetResult(channel);
            if (PlayAsyncException is not null)
            {
                throw PlayAsyncException;
            }

            if (PlayAsyncHandler is not null)
            {
                await PlayAsyncHandler(channel, cancellationToken);
            }
        }

        public Task StopAsync() => StopAsyncHandler?.Invoke() ?? Task.CompletedTask;

        public void CompletePlayback(bool failed) =>
            PlaybackEnded?.Invoke(new(PlayedChannel?.Source ?? string.Empty, failed));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControlledSnapshotPlaybackController :
        IPlaybackSessionController,
        IPlaybackSessionSnapshotNotifications
    {
        private readonly TaskCompletionSource<PlaybackSessionCommandOutcome> _playResult = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public event EventHandler? SnapshotChanged;

        public PlaybackSessionSnapshot Snapshot { get; private set; } =
            PlaybackSessionSnapshot.Stopped;

        public TaskCompletionSource PlayStarted { get; } = NewSignal();

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            _ = source;
            _ = cancellationToken;
            PlayStarted.TrySetResult();
            return new(_playResult.Task);
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }

        public void Publish(PlaybackSessionSnapshot snapshot)
        {
            Snapshot = snapshot;
            PublishNotificationWithoutChangingSnapshot();
        }

        public void PublishNotificationWithoutChangingSnapshot() =>
            SnapshotChanged?.Invoke(this, EventArgs.Empty);

        public void CompletePlay(PlaybackSessionCommandOutcome outcome) =>
            _playResult.TrySetResult(outcome);
    }
}
