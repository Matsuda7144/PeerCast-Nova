namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainViewModelTests
{
    [Fact]
    public void InitialStateIsReadyForManualSource()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
        Assert.Empty(viewModel.Channels);
        Assert.Equal(0, viewModel.ChannelCount);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(GenesisPlaybackWindowMode.Normal, viewModel.PlaybackWindowMode);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void ChannelColumnsFollowCompactPecaRecorderOrder()
    {
        var actual = GenesisChannelListPresentation.Columns
            .Select(column => (column.Column, column.Header))
            .ToArray();

        Assert.Equal(
            new[]
            {
                (GenesisChannelColumn.ChannelName, "チャンネル名"),
                (
                    GenesisChannelColumn.Details,
                    "[ジャンル - 詳細] コメント アーティスト/タイトル/アルバム"),
                (GenesisChannelColumn.ListenerRelay, "リスナー/リレー"),
                (GenesisChannelColumn.Bitrate, "ビットレート"),
                (GenesisChannelColumn.Uptime, "配信時間"),
                (GenesisChannelColumn.Type, "タイプ"),
                (GenesisChannelColumn.YellowPage, "YP"),
            },
            actual);
    }

    [Fact]
    public void ChannelItemFormatsCompactOperationalColumns()
    {
        var channel = new GenesisChannelItem(
            "たつひと",
            "pcp://example/channel",
            "[game - がんばれ] コメント",
            Listeners: 133,
            Relays: 137,
            BitrateKbps: 2008,
            Uptime: TimeSpan.FromHours(2) + TimeSpan.FromMinutes(15),
            Type: "FLV");

        Assert.Equal("[game - がんばれ] コメント", channel.DetailsText);
        Assert.Equal("133 / 137", channel.ListenerRelayText);
        Assert.Equal("2008", channel.BitrateText);
        Assert.Equal("02:15", channel.UptimeText);
        Assert.Equal("FLV", channel.TypeText);
    }

    [Fact]
    public void ReplacingChannelsKeepsListPresentationSeparateFromPlayback()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem(
            "Example channel",
            "pcp://example/channel",
            "[game - example]");

        viewModel.ReplaceChannels([channel]);

        Assert.Equal([channel], viewModel.Channels);
        Assert.Equal(1, viewModel.ChannelCount);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    [Fact]
    public async Task BlankSourceIsRejectedBeforeControllerInvocation()
    {
        var controller = new RecordingPlaybackSessionController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "   ",
        };

        await viewModel.PlayAsync();

        Assert.Equal(0, controller.PlayCount);
        Assert.Equal("Enter a channel or source URL.", viewModel.ErrorMessage);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    [Fact]
    public async Task FakeControllerProvidesPlayPopupStopGuiFlowWithoutPlayback()
    {
        var controller = new FakePlaybackSessionController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "  pcp://example/channel  ",
        };

        await viewModel.PlayAsync();

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal("pcp://example/channel", viewModel.CurrentSource);
        Assert.Equal("Playing", viewModel.StatusText);
        Assert.False(viewModel.CanPlay);
        Assert.True(viewModel.CanStop);
        Assert.True(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(GenesisPlaybackWindowMode.Normal, viewModel.PlaybackWindowMode);
        Assert.Equal("pcp://example/channel", viewModel.PlaybackWindowSource);
        Assert.Null(viewModel.ErrorMessage);

        await viewModel.StopAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.Null(viewModel.CurrentSource);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Null(viewModel.PlaybackWindowSource);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task PlayIsIgnoredWhilePlaybackStateDisablesPlay()
    {
        var controller = new RecordingPlaybackSessionController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/channel",
        };
        await viewModel.PlayAsync();

        viewModel.SourceText = "pcp://example/second";
        await viewModel.PlayAsync();

        Assert.Equal(1, controller.PlayCount);
        Assert.Equal("pcp://example/channel", controller.LastSource);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.Equal("pcp://example/channel", viewModel.CurrentSource);
        Assert.Equal("pcp://example/second", viewModel.SourceText);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.CanPlay);
        Assert.True(viewModel.CanStop);
    }

    [Fact]
    public async Task PlaybackWindowCanSwitchBetweenNormalVideoOnlyAndFullscreen()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example/channel",
        };
        await viewModel.PlayAsync();

        viewModel.ShowVideoOnly();
        Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, viewModel.PlaybackWindowMode);

        viewModel.ShowFullscreen();
        Assert.Equal(GenesisPlaybackWindowMode.Fullscreen, viewModel.PlaybackWindowMode);

        viewModel.RestorePlaybackWindow();
        Assert.Equal(GenesisPlaybackWindowMode.Normal, viewModel.PlaybackWindowMode);
    }

    [Fact]
    public async Task ClosingPlaybackPopupRequestsSessionStop()
    {
        var controller = new RecordingPlaybackSessionController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/channel",
        };
        await viewModel.PlayAsync();

        await viewModel.ClosePlaybackWindowAsync();

        Assert.Equal(1, controller.StopCount);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    [Fact]
    public async Task RejectedPlaySurfacesBoundedGuiError()
    {
        var controller = new RecordingPlaybackSessionController
        {
            PlayOutcome = new(
                PlaybackSessionCommandResult.Rejected,
                new(
                    PlaybackSessionState.Stopped,
                    null,
                    "Source is unavailable.")),
        };
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://missing/channel",
        };

        await viewModel.PlayAsync();

        Assert.Equal(1, controller.PlayCount);
        Assert.Equal("pcp://missing/channel", controller.LastSource);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.Equal("Source is unavailable.", viewModel.ErrorMessage);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
        Assert.False(viewModel.IsPlaybackWindowOpen);
    }

    [Fact]
    public void SelectingChannelCopiesItsSourceIntoManualSourceField()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var channel = new GenesisChannelItem(
            "Example channel",
            "pcp://example/channel",
            "Fake GUI item");

        viewModel.SelectedChannel = channel;

        Assert.Same(channel, viewModel.SelectedChannel);
        Assert.Equal(channel.Source, viewModel.SourceText);
    }

    private sealed class RecordingPlaybackSessionController : IPlaybackSessionController
    {
        public PlaybackSessionSnapshot Snapshot { get; private set; } =
            PlaybackSessionSnapshot.Stopped;

        public PlaybackSessionCommandOutcome PlayOutcome { get; init; } = new(
            PlaybackSessionCommandResult.Completed,
            new(PlaybackSessionState.Playing, "pcp://example/channel", null));

        public int PlayCount { get; private set; }

        public int StopCount { get; private set; }

        public string? LastSource { get; private set; }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            PlayCount++;
            LastSource = source;
            Snapshot = PlayOutcome.Snapshot;
            return ValueTask.FromResult(PlayOutcome);
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            StopCount++;
            Snapshot = PlaybackSessionSnapshot.Stopped;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }
    }
}
