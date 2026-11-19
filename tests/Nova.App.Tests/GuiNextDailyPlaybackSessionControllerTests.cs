namespace PeerCast.Nova.App.Tests;

public sealed class GuiNextDailyPlaybackSessionControllerTests
{
    [Fact]
    public async Task CatalogMetadataReachesDailyPlaybackOnlyForTheSelectedChannel()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var channel = new GenesisChannelItem("metadata test", Source, "details", 10, 2, 1500, Type: "FLV")
        { ContactUrl = "https://example.test/bbs" };
        await controller.PlayAsync(new PlaybackSessionRequest(Source, channel.Name) { CatalogChannel = channel });
        Assert.Equal(channel.ContactUrl, daily.PlayedChannel!.ContactUrl);
        Assert.Equal(1500, daily.PlayedChannel.BitrateKbps);
        await controller.StopAsync();
        await controller.PlayAsync(new PlaybackSessionRequest(Source, channel.Name)
        { CatalogChannel = channel with { Source = "pcp://other.test:7144/channel/531DC8DFC7FB42928AC2C0A626517A87" } });
        Assert.Null(daily.PlayedChannel!.ContactUrl);
    }

    private const string Source =
        "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87";

    [Fact]
    public async Task SelectedChannelPlayPreservesNameIdAndSourceAtDailyBoundary()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var viewModel = new GenesisMainViewModel(controller);
        var channel = new GenesisChannelItem(
            "selected station",
            Source,
            ChannelId: "531DC8DFC7FB42928AC2C0A626517A87");
        viewModel.ReplaceChannels([channel]);
        viewModel.SelectedChannel = channel;

        await viewModel.PlayAsync();

        Assert.Equal(1, daily.PlayCount);
        Assert.NotNull(daily.PlayedChannel);
        Assert.Equal("selected station", daily.PlayedChannel.Name);
        Assert.Equal("531DC8DFC7FB42928AC2C0A626517A87", daily.PlayedChannel.ChannelId);
        Assert.Equal(Source, daily.PlayedChannel.Source);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal(Source, viewModel.CurrentSource);
    }

    [Fact]
    public async Task ManualPcpSourceUsesSameDailyControllerBoundary()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);

        var outcome = await controller.PlayAsync(Source);

        Assert.Equal(PlaybackSessionCommandResult.Completed, outcome.Result);
        Assert.Equal(Source, Assert.IsType<GenesisChannelItem>(daily.PlayedChannel).Source);
        Assert.Equal(Source, daily.PlayedChannel.Name);
    }

    [Fact]
    public async Task StopForwardsOnceAndRepeatedStopIsSafe()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        _ = await controller.PlayAsync(Source);

        var stopped = await controller.StopAsync();
        var repeatedStop = await controller.StopAsync();

        Assert.Equal(PlaybackSessionCommandResult.Completed, stopped.Result);
        Assert.Equal(PlaybackSessionState.Stopped, stopped.Snapshot.State);
        Assert.Equal(PlaybackSessionCommandResult.Completed, repeatedStop.Result);
        Assert.Equal(1, daily.StopCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.test/live")]
    [InlineData("pcp://tracker.example:7144/channel/invalid")]
    public async Task InvalidOrUnsupportedSourceFailsClosed(string source)
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);

        var outcome = await controller.PlayAsync(source);

        Assert.Equal(PlaybackSessionCommandResult.Rejected, outcome.Result);
        Assert.Equal(PlaybackSessionState.Stopped, outcome.Snapshot.State);
        Assert.Equal("This source is not a valid PCP channel URI.", outcome.Snapshot.ErrorMessage);
        Assert.Equal(0, daily.PlayCount);
    }

    [Fact]
    public async Task SourceAndCatalogChannelIdMustIdentifySameChannel()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);

        var outcome = await controller.PlayAsync(new PlaybackSessionRequest(
            Source,
            "different identity",
            "11111111111141118111111111111111"));

        Assert.Equal(PlaybackSessionCommandResult.Rejected, outcome.Result);
        Assert.Equal(0, daily.PlayCount);
    }

    [Fact]
    public async Task InvalidReplacementDoesNotForgetActiveSession()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        _ = await controller.PlayAsync(Source);

        var rejected = await controller.PlayAsync("https://example.test/live");

        Assert.Equal(PlaybackSessionCommandResult.Rejected, rejected.Result);
        Assert.Equal(PlaybackSessionState.Playing, controller.Snapshot.State);
        Assert.Equal(Source, controller.Snapshot.Source);
        Assert.Equal(1, daily.PlayCount);
        _ = await controller.StopAsync();
        Assert.Equal(1, daily.StopCount);
    }

    [Fact]
    public async Task MissingPlayerIsReportedOnPlayWithoutNetworkStartup()
    {
        var missingPlayer = Path.Combine(
            Path.GetTempPath(),
            $"Nova-GuiNext-missing-{Guid.NewGuid():N}",
            PcrPlayerExternalPlayerLauncher.ExecutableFileName);
        var daily = new NovaDailyHttpFlvPlaybackController(
            missingPlayer,
            static _ => { },
            static _ => { });
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);

        var outcome = await controller.PlayAsync(Source);

        Assert.Equal(PlaybackSessionCommandResult.Faulted, outcome.Result);
        Assert.Equal(PlaybackSessionState.Faulted, outcome.Snapshot.State);
        Assert.Equal(
            "The external player is unavailable. Check Playback Settings, --pcrplayer-executable, or NOVA_PCRPLAYER_EXE. " +
            "If no path is configured, check PATH for PCRPlayer64.exe.",
            outcome.Snapshot.ErrorMessage);
    }

    [Fact]
    public async Task DailyStartupExceptionIsMappedWithoutExposingRawException()
    {
        var daily = new RecordingDailyPlaybackController
        {
            PlayAsyncException = new InvalidOperationException("private transport detail"),
        };
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);

        var outcome = await controller.PlayAsync(Source);

        Assert.Equal(PlaybackSessionCommandResult.Faulted, outcome.Result);
        Assert.Equal(
            "The playback source could not be started.",
            outcome.Snapshot.ErrorMessage);
        Assert.DoesNotContain(
            "private transport detail",
            outcome.Snapshot.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypedStartupFailureRetainsSafeHttpAndQuitClassification()
    {
        var upstream = new PeerCast.Nova.Network.Pcp.PcpChannelSessionResult(
            PeerCast.Nova.Network.Pcp.PcpChannelSessionEndReason.HttpRejected, 1, 0, 403, 0, 0, 0)
        { Quit = new(1007) };
        var result = new PeerCast.Nova.Network.Pcp.PcpRelayCoordinatorResult(
            PeerCast.Nova.Network.Pcp.PcpRelayCoordinatorEndReason.UpstreamEnded,
            PeerCast.Nova.Network.Pcp.PcpRelayDrainOutcome.Completed, new(0, 0), upstream, null, true);
        var daily = new RecordingDailyPlaybackController
        { PlayAsyncException = NovaDailyPlaybackStartupException.FromResult(result, null) };
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var outcome = await controller.PlayAsync(Source);
        Assert.Equal(PlaybackSessionCommandResult.Faulted, outcome.Result);
        Assert.Equal("PCP startup failed: HttpRejected; HTTP 403; QUIT 1007.", outcome.Snapshot.ErrorMessage);
        Assert.DoesNotContain("tracker.example", outcome.Snapshot.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingUpstreamResultRetainsConnectionStageAndHeaderTimeoutIsDistinct()
    {
        var result = new PeerCast.Nova.Network.Pcp.PcpRelayCoordinatorResult(
            PeerCast.Nova.Network.Pcp.PcpRelayCoordinatorEndReason.UpstreamFailed,
            PeerCast.Nova.Network.Pcp.PcpRelayDrainOutcome.Completed, new(0, 0), null, null, true);
        var fault = new PeerCast.Nova.Core.Channels.ChannelFaultSnapshot(
            PeerCast.Nova.Core.Channels.ChannelFaultCategory.ConnectionFailed,
            PeerCast.Nova.Core.Channels.ChannelFaultStage.Connecting, 1, 0);
        Assert.Equal("PCP startup failed: UpstreamFailed (ConnectionFailed/Connecting).",
            NovaDailyPlaybackStartupException.FromResult(result, fault).Message);
        Assert.Equal("FLV header was not received within 30 seconds.",
            NovaDailyPlaybackStartupException.HeaderTimeout().Message);
    }

    [Fact]
    public async Task StopFailureIsMappedWithoutExposingRawException()
    {
        var daily = new RecordingDailyPlaybackController
        {
            StopAsyncException = new InvalidOperationException("private cleanup detail"),
        };
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        _ = await controller.PlayAsync(Source);

        var outcome = await controller.StopAsync();

        Assert.Equal(PlaybackSessionCommandResult.Faulted, outcome.Result);
        Assert.Equal(PlaybackSessionState.Faulted, outcome.Snapshot.State);
        Assert.Equal(
            "The playback session could not be stopped.",
            outcome.Snapshot.ErrorMessage);
        Assert.DoesNotContain(
            "private cleanup detail",
            outcome.Snapshot.ErrorMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationReturnsStoppedWithoutLaunchingBoundaryAgain()
    {
        var dailyStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var daily = new RecordingDailyPlaybackController
        {
            PlayAsyncHandler = async (_, cancellationToken) =>
            {
                dailyStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
        };
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        using var cancellation = new CancellationTokenSource();

        var play = controller.PlayAsync(Source, cancellation.Token).AsTask();
        await dailyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var outcome = await play.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PlaybackSessionCommandResult.Cancelled, outcome.Result);
        Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
        Assert.Equal(1, daily.PlayCount);
    }

    [Fact]
    public async Task DifferentChannelPlayUsesDailyStopThenStartReplacementBoundary()
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        _ = await controller.PlayAsync(Source);

        var outcome = await controller.PlayAsync(
            "pcp://other.example:7144/channel/11111111111141118111111111111111");

        Assert.Equal(PlaybackSessionCommandResult.Completed, outcome.Result);
        Assert.Equal(2, daily.PlayCount);
        Assert.Equal(
            "pcp://other.example:7144/channel/11111111111141118111111111111111",
            daily.PlayedChannel?.Source);
        Assert.Equal(daily.PlayedChannel?.Source, controller.Snapshot.Source);
    }

    [Theory]
    [InlineData(false, PlaybackSessionState.Stopped)]
    [InlineData(true, PlaybackSessionState.Faulted)]
    public async Task DailyControllerTerminationProjectsIntoGuiSnapshot(
        bool failed,
        PlaybackSessionState expectedState)
    {
        var daily = new RecordingDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var viewModel = new GenesisMainViewModel(controller);
        var changed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GenesisMainViewModel.PlaybackState) &&
                viewModel.PlaybackState == expectedState)
            {
                changed.TrySetResult();
            }
        };
        viewModel.SourceText = Source;
        await viewModel.PlayAsync();

        daily.CompletePlayback(failed);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expectedState, viewModel.PlaybackState);
        Assert.Equal(expectedState, controller.Snapshot.State);
        if (failed)
        {
            Assert.Equal("The playback session ended unexpectedly.", viewModel.ErrorMessage);
        }
        else
        {
            Assert.Null(viewModel.ErrorMessage);
        }
    }

    [Fact]
    public async Task SystemControllerCompositionDoesNotResolvePlayerUntilPlay()
    {
        using var fixture = new RecordingMetadataFixture();
        await using var controller = GuiNextDailyPlaybackSessionController.CreateSystem(
            Path.Combine(Path.GetTempPath(), "missing", "PCRPlayer64.exe"),
            recordingSettings: fixture.Settings, historyStore: fixture.Store);

        Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
    }

    private sealed class RecordingMetadataFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Nova-gui-playback-metadata-tests", Guid.NewGuid().ToString("N"));
        public GenesisRecordingHistoryStore Store => new(Path.Combine(_root, "history.json"));
        public GenesisRecordingOutputDirectorySettings Settings => new(new(Path.Combine(_root, "settings.json")), Path.Combine(_root, "recordings"));
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
    }

    private sealed class RecordingDailyPlaybackController : INovaDailyPlaybackController
    {
        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded;

        public GenesisChannelItem? PlayedChannel { get; private set; }

        public int PlayCount { get; private set; }

        public int StopCount { get; private set; }

        public Func<GenesisChannelItem, CancellationToken, Task>? PlayAsyncHandler { get; init; }

        public Exception? PlayAsyncException { get; init; }

        public Exception? StopAsyncException { get; init; }

        public async Task PlayAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken)
        {
            PlayCount++;
            PlayedChannel = channel;
            if (PlayAsyncException is not null)
            {
                throw PlayAsyncException;
            }

            if (PlayAsyncHandler is not null)
            {
                await PlayAsyncHandler(channel, cancellationToken);
            }
        }

        public Task StopAsync()
        {
            StopCount++;
            if (StopAsyncException is not null)
            {
                throw StopAsyncException;
            }

            return Task.CompletedTask;
        }

        public void CompletePlayback(bool failed) =>
            PlaybackEnded?.Invoke(new(PlayedChannel?.Source ?? string.Empty, failed));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
