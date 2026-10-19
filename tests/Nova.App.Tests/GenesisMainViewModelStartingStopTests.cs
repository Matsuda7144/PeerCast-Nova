namespace PeerCast.Nova.App.Tests;

public sealed class GenesisMainViewModelStartingStopTests
{
    [Fact]
    public async Task StopDuringStartingCancelsStartupAndIsIdempotent()
    {
        var daily = new CancellableDailyPlaybackController();
        await using var controller = new GuiNextDailyPlaybackSessionController(daily);
        var viewModel = new GenesisMainViewModel(controller)
        {
            SourceText = "pcp://tracker.example:7144/channel/531DC8DFC7FB42928AC2C0A626517A87",
        };
        var interaction = new GenesisMainWindowInteraction(viewModel);

        var play = viewModel.PlayAsync().AsTask();
        await daily.PlayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PlaybackSessionState.Starting, viewModel.PlaybackState);
        Assert.True(viewModel.CanStop);
        Assert.False(viewModel.CanPlay);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        var presentation = GenesisGuiNextPlaybackCommandPresentation.Resolve(
            viewModel,
            GenesisGuiNextTab.Channels,
            selectedRow: null);
        Assert.False(presentation.IsPlayEnabled);
        Assert.True(presentation.IsStopEnabled);

        await viewModel.PlayAsync();
        Assert.Equal(1, daily.PlayCount);

        var stop = GenesisGuiNextPlaybackCommandDispatch
            .StopAsync(viewModel, interaction)
            .AsTask();
        await daily.StopRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await GenesisGuiNextPlaybackCommandDispatch
            .StopAsync(viewModel, interaction)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(play, stop).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, daily.PlayCount);
        Assert.Equal(1, daily.StopCount);
        Assert.True(daily.StartupCancellationObserved);
        Assert.Equal(1, daily.CleanupCount);
        Assert.Equal(PlaybackSessionState.Stopped, controller.Snapshot.State);
        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Null(viewModel.ErrorMessage);
        Assert.True(viewModel.CanPlay);
        Assert.False(viewModel.CanStop);
    }

    private sealed class CancellableDailyPlaybackController : INovaDailyPlaybackController
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _playbackCleaned = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _startupCancellation;

        public event Action<NovaDailyPlaybackTermination>? PlaybackEnded
        {
            add { }
            remove { }
        }

        public TaskCompletionSource PlayStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StopRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int PlayCount { get; private set; }

        public int StopCount { get; private set; }

        public int CleanupCount { get; private set; }

        public bool StartupCancellationObserved { get; private set; }

        public async Task PlayAsync(
            GenesisChannelItem channel,
            CancellationToken cancellationToken)
        {
            _ = channel;
            PlayCount++;
            var startupCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_gate)
            {
                _startupCancellation = startupCancellation;
            }

            PlayStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, startupCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                StartupCancellationObserved = true;
                throw;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_startupCancellation, startupCancellation))
                    {
                        _startupCancellation = null;
                    }
                }

                startupCancellation.Dispose();
                CleanupCount++;
                _playbackCleaned.TrySetResult();
            }
        }

        public async Task StopAsync()
        {
            StopCount++;
            StopRequested.TrySetResult();
            lock (_gate)
            {
                try
                {
                    _startupCancellation?.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Startup completed while stop was waiting for ownership.
                }
            }

            await _playbackCleaned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
