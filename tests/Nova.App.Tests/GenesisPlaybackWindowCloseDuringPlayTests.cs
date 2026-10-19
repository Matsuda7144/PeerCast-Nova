namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackWindowCloseDuringPlayTests
{
    [Fact]
    public async Task CloseRequestedWhilePlayOutcomeOpensWindowStopsSession()
    {
        var controller = new RecordingPlaybackSessionController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/channel",
        };
        Task? closeRequest = null;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(GenesisMainViewModel.IsPlaybackWindowOpen) &&
                viewModel.IsPlaybackWindowOpen)
            {
                closeRequest = viewModel.ClosePlaybackWindowAsync().AsTask();
            }
        };

        await viewModel.PlayAsync();
        if (closeRequest is not null)
        {
            await closeRequest;
        }

        Assert.NotNull(closeRequest);
        Assert.Equal(1, controller.StopCount);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Null(viewModel.CurrentSource);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
    }

    private sealed class RecordingPlaybackSessionController : IPlaybackSessionController
    {
        public PlaybackSessionSnapshot Snapshot { get; private set; } =
            PlaybackSessionSnapshot.Stopped;

        public int StopCount { get; private set; }

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Snapshot = new(PlaybackSessionState.Playing, source, null);
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            Snapshot = PlaybackSessionSnapshot.Stopped;
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Completed,
                Snapshot));
        }
    }
}
