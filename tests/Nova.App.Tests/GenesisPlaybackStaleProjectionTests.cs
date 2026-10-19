namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackStaleProjectionTests
{
    [Fact]
    public async Task StateChangeDuringSuccessfulReplacementDisposesStaleProjectedSurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new BlockingSuccessfulReplacementTarget();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example",
        };
        await using var coordinator = await GenesisPlaybackWindowCoordinator.OpenAsync(
            dispatcher,
            target,
            viewModel);
        await viewModel.PlayAsync();
        await coordinator.SynchronizeAsync();

        viewModel.ShowVideoOnly();
        await target.ReplacementStarted.WaitAsync(TimeSpan.FromSeconds(1));

        await viewModel.StopAsync();
        target.ReleaseReplacement();
        await target.ReplacementLifetime.Disposed.WaitAsync(TimeSpan.FromSeconds(1));
        await coordinator.SynchronizeAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.True(target.FirstLifetime.Disposed);
        Assert.Equal(0, target.ReplacementLifetime.CloseNotificationReadCount);
        Assert.Collection(
            target.States,
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, state.Mode));
    }

    [Fact]
    public async Task StateChangeDuringRetirementDoesNotOpenStaleReplacement()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new BlockingRetirementTarget();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example",
        };
        await using var coordinator = await GenesisPlaybackWindowCoordinator.OpenAsync(
            dispatcher,
            target,
            viewModel);
        await viewModel.PlayAsync();
        await coordinator.SynchronizeAsync();

        viewModel.ShowVideoOnly();
        await target.FirstLifetime.RetirementStarted.WaitAsync(TimeSpan.FromSeconds(1));

        await viewModel.StopAsync();
        target.FirstLifetime.ReleaseRetirement();
        await target.FirstLifetime.Disposed.WaitAsync(TimeSpan.FromSeconds(1));
        await coordinator.SynchronizeAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.Collection(
            target.States,
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode));
    }

    private sealed class RecordingDispatcher : IGenesisMainWindowDispatcher
    {
        public long WindowToken => 7144;

        public async ValueTask<GenesisMainWindowDispatcherInvocation> InvokeGenesisAsync(
            Func<long, CancellationToken, ValueTask<IAsyncDisposable?>> operation,
            CancellationToken cancellationToken)
        {
            var lifetime = await operation(WindowToken, cancellationToken);
            return new(GenesisMainWindowDispatcherResult.Completed, lifetime);
        }
    }

    private sealed class BlockingSuccessfulReplacementTarget :
        IGenesisPlaybackWindowSurfaceTarget
    {
        private readonly TaskCompletionSource<bool> _replacementStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _replacementRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<GenesisPlaybackWindowState> States { get; } = [];

        public RecordingLifetime FirstLifetime { get; } = new();

        public CloseAwareRecordingLifetime ReplacementLifetime { get; } = new();

        public Task ReplacementStarted => _replacementStarted.Task;

        public void ReleaseReplacement() =>
            _replacementRelease.TrySetResult(true);

        public async ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            if (States.Count == 1)
            {
                return FirstLifetime;
            }

            if (States.Count != 2)
            {
                throw new InvalidOperationException("Unexpected playback surface request.");
            }

            _replacementStarted.TrySetResult(true);
            await _replacementRelease.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return ReplacementLifetime;
        }
    }

    private sealed class BlockingRetirementTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public List<GenesisPlaybackWindowState> States { get; } = [];

        public BlockingRetirementLifetime FirstLifetime { get; } = new();

        public RecordingLifetime UnexpectedReplacementLifetime { get; } = new();

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            return States.Count switch
            {
                1 => ValueTask.FromResult<IAsyncDisposable?>(FirstLifetime),
                2 => ValueTask.FromResult<IAsyncDisposable?>(UnexpectedReplacementLifetime),
                _ => throw new InvalidOperationException("Unexpected playback surface request."),
            };
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRetirementLifetime : IAsyncDisposable
    {
        private readonly TaskCompletionSource<bool> _retirementStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _retirementRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RetirementStarted => _retirementStarted.Task;

        public Task Disposed => _disposed.Task;

        public void ReleaseRetirement() =>
            _retirementRelease.TrySetResult(true);

        public async ValueTask DisposeAsync()
        {
            _retirementStarted.TrySetResult(true);
            await _retirementRelease.Task.ConfigureAwait(false);
            _disposed.TrySetResult(true);
        }
    }

    private sealed class CloseAwareRecordingLifetime :
        IGenesisPlaybackWindowCloseAwareLifetime
    {
        private readonly TaskCompletionSource<bool> _closedByUser =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _closeNotificationReadCount;

        public Task<bool> ClosedByUser
        {
            get
            {
                _ = Interlocked.Increment(ref _closeNotificationReadCount);
                return _closedByUser.Task;
            }
        }

        public int CloseNotificationReadCount =>
            Volatile.Read(ref _closeNotificationReadCount);

        public Task Disposed => _disposed.Task;

        public ValueTask DisposeAsync()
        {
            _closedByUser.TrySetResult(false);
            _disposed.TrySetResult(true);
            return ValueTask.CompletedTask;
        }
    }
}
