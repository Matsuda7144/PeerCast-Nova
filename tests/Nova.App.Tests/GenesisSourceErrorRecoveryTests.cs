namespace PeerCast.Nova.App.Tests;

public sealed class GenesisSourceErrorRecoveryTests
{
    [Fact]
    public async Task CorrectedSourceClearsStaleValidationError()
    {
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "   ",
        };

        await viewModel.PlayAsync();

        Assert.Equal("Enter a channel or source URL.", viewModel.ErrorMessage);
        Assert.Equal(
            "Ready: Enter a channel or source URL.",
            GenesisStatusPresentation.Format(viewModel));

        viewModel.SourceText = "pcp://example/retry";

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Ready", GenesisStatusPresentation.Format(viewModel));
    }

    [Fact]
    public async Task CorrectedSourceClearsRejectedPlayError()
    {
        var controller = new RejectedPlayController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/missing",
        };

        await viewModel.PlayAsync();

        Assert.Equal("Source is unavailable.", viewModel.ErrorMessage);

        viewModel.SourceText = "pcp://example/retry";

        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Ready", GenesisStatusPresentation.Format(viewModel));
    }

    [Fact]
    public async Task SourceEditPreservesStopFailure()
    {
        var controller = new StopFailingController();
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://example/channel",
        };
        await viewModel.PlayAsync();

        await viewModel.StopAsync();

        Assert.Equal(PlaybackSessionState.Faulted, viewModel.PlaybackState);
        Assert.Equal("Stop failed.", viewModel.ErrorMessage);

        viewModel.SourceText = "pcp://example/retry";

        Assert.Equal("Stop failed.", viewModel.ErrorMessage);
        Assert.Equal(
            "Playback error: Stop failed.",
            GenesisStatusPresentation.Format(viewModel));
    }

    private sealed class RejectedPlayController : IPlaybackSessionController
    {
        public PlaybackSessionSnapshot Snapshot { get; private set; } =
            PlaybackSessionSnapshot.Stopped;

        public ValueTask<PlaybackSessionCommandOutcome> PlayAsync(
            string source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Snapshot = new(
                PlaybackSessionState.Stopped,
                null,
                "Source is unavailable.");
            return ValueTask.FromResult(new PlaybackSessionCommandOutcome(
                PlaybackSessionCommandResult.Rejected,
                Snapshot));
        }

        public ValueTask<PlaybackSessionCommandOutcome> StopAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StopFailingController : IPlaybackSessionController
    {
        public PlaybackSessionSnapshot Snapshot { get; private set; } =
            PlaybackSessionSnapshot.Stopped;

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
            throw new InvalidOperationException("Stop failed.");
        }
    }
}
