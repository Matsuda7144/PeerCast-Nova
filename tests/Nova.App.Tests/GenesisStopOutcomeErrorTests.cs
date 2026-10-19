namespace PeerCast.Nova.App.Tests;

public sealed class GenesisStopOutcomeErrorTests
{
    [Theory]
    [InlineData(
        PlaybackSessionCommandResult.Rejected,
        PlaybackSessionState.Playing,
        "Stop request was rejected.")]
    [InlineData(
        PlaybackSessionCommandResult.Faulted,
        PlaybackSessionState.Faulted,
        "Stop failed.")]
    public async Task StopOutcomeWithoutDetailUsesStopSpecificFallback(
        PlaybackSessionCommandResult result,
        PlaybackSessionState outcomeState,
        string expectedError)
    {
        var controller = new StopOutcomeController(result, outcomeState);
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/channel",
        };
        await viewModel.PlayAsync();

        await viewModel.StopAsync();

        Assert.Equal(1, controller.StopCount);
        Assert.Equal(outcomeState, viewModel.PlaybackState);
        Assert.Equal(expectedError, viewModel.ErrorMessage);
    }

    [Fact]
    public async Task RejectedStopPreservesPlaybackWindowMode()
    {
        var controller = new StopOutcomeController(
            PlaybackSessionCommandResult.Rejected,
            PlaybackSessionState.Playing);
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/channel",
        };
        await viewModel.PlayAsync();
        viewModel.ShowFullscreen();

        await viewModel.StopAsync();

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.True(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(GenesisPlaybackWindowMode.Fullscreen, viewModel.PlaybackWindowMode);
    }

    private sealed class StopOutcomeController : IPlaybackSessionController
    {
        private readonly PlaybackSessionCommandResult _stopResult;
        private readonly PlaybackSessionState _stopState;

        public StopOutcomeController(
            PlaybackSessionCommandResult stopResult,
            PlaybackSessionState stopState)
        {
            _stopResult = stopResult;
            _stopState = stopState;
        }

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
            Snapshot = new(_stopState, "pcp://example/channel", null);
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                _stopResult,
                Snapshot));
        }
    }
}
