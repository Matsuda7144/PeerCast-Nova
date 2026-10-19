namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackWindowCoordinatorTests
{
    [Fact]
    public async Task PlayProjectsWindowAndStopDisposesIt()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new RecordingTarget();
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

        Assert.Equal(1, target.OpenCount);
        Assert.Equal("pcp://example", target.States[0].Source);
        Assert.Equal(GenesisPlaybackWindowMode.Normal, target.States[0].Mode);
        Assert.False(target.Lifetimes[0].Disposed);

        await viewModel.StopAsync();
        await coordinator.SynchronizeAsync();

        Assert.True(target.Lifetimes[0].Disposed);
        Assert.Equal(1, target.OpenCount);
    }

    [Fact]
    public async Task InitialCancellationDoesNotReturnCoordinator()
    {
        using var cancellation = new CancellationTokenSource();
        var dispatcher = new RecordingDispatcher();
        var target = new CancellingOpenTarget(cancellation);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example",
        };
        await viewModel.PlayAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => GenesisPlaybackWindowCoordinator.OpenAsync(
                    dispatcher,
                    target,
                    viewModel,
                    cancellation.Token)
                .AsTask());

        Assert.Single(target.Lifetimes);
        Assert.True(target.Lifetimes[0].Disposed);
    }

    [Fact]
    public async Task UserWindowCloseStopsSessionAndDisposesSurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new RecordingTarget();
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

        target.Lifetimes[0].RequestUserClose();
        await coordinator.WaitForCloseNotificationAsync();
        await coordinator.SynchronizeAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.True(target.Lifetimes[0].Disposed);
    }

    [Fact]
    public async Task PresentationModeChangeReprojectsPlaybackSurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new RecordingTarget();
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
        await coordinator.SynchronizeAsync();

        Assert.Equal(2, target.OpenCount);
        Assert.True(target.Lifetimes[0].Disposed);
        Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, target.States[1].Mode);
        Assert.False(target.Lifetimes[1].Disposed);
    }

    [Fact]
    public async Task DisposalFailureDoesNotBlockPresentationModeReplacement()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new ThrowingReplacementTarget();
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
        await coordinator.SynchronizeAsync();

        Assert.True(target.FirstLifetime.DisposeAttempted);
        Assert.Equal(2, target.OpenCount);
        Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, target.States[1].Mode);
        Assert.NotNull(target.ReplacementLifetime);
        Assert.False(target.ReplacementLifetime.Disposed);
    }

    [Fact]
    public async Task RetiringSurfaceCloseDoesNotStopReplacementSession()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new BlockingReplacementTarget();
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
        var replacement = coordinator.SynchronizeAsync().AsTask();
        await target.FirstLifetime.DisposeStarted.WaitAsync(TimeSpan.FromSeconds(1));

        target.FirstLifetime.RequestUserClose();
        await Task.Yield();
        var stateAfterRetiringClose = viewModel.PlaybackState;
        target.FirstLifetime.ReleaseDispose();

        await replacement;
        await coordinator.SynchronizeAsync();

        Assert.Equal(PlaybackSessionState.Playing, stateAfterRetiringClose);
        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.True(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(2, target.OpenCount);
        Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, target.States[1].Mode);
        Assert.True(target.FirstLifetime.Disposed);
        Assert.NotNull(target.ReplacementLifetime);
        Assert.False(target.ReplacementLifetime.Disposed);
    }

    [Fact]
    public async Task CoordinatorDisposeClosesCurrentSurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new RecordingTarget();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example",
        };
        var coordinator = await GenesisPlaybackWindowCoordinator.OpenAsync(
            dispatcher,
            target,
            viewModel);
        await viewModel.PlayAsync();
        await coordinator.SynchronizeAsync();

        await coordinator.DisposeAsync();

        Assert.Single(target.Lifetimes);
        Assert.True(target.Lifetimes[0].Disposed);
    }

    [Fact]
    public async Task CoordinatorDisposeDoesNotWaitForUnfinishedCloseObserver()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new NonCompletingCloseTarget();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController())
        {
            SourceText = "pcp://example",
        };
        var coordinator = await GenesisPlaybackWindowCoordinator.OpenAsync(
            dispatcher,
            target,
            viewModel);
        await viewModel.PlayAsync();
        await coordinator.SynchronizeAsync();

        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        await coordinator.WaitForCloseNotificationAsync();

        Assert.True(target.Lifetime.Disposed);
        Assert.False(target.Lifetime.ClosedByUser.IsCompleted);
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

    private sealed class RecordingTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public List<GenesisPlaybackWindowState> States { get; } = [];

        public List<RecordingLifetime> Lifetimes { get; } = [];

        public int OpenCount => States.Count;

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            var lifetime = new RecordingLifetime();
            States.Add(state);
            Lifetimes.Add(lifetime);
            return ValueTask.FromResult<IAsyncDisposable?>(lifetime);
        }
    }

    private sealed class CancellingOpenTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        private readonly CancellationTokenSource _cancellation;

        public CancellingOpenTarget(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public List<RecordingLifetime> Lifetimes { get; } = [];

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            var lifetime = new RecordingLifetime();
            Lifetimes.Add(lifetime);
            _cancellation.Cancel();
            return ValueTask.FromResult<IAsyncDisposable?>(lifetime);
        }
    }

    private sealed class ThrowingReplacementTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public List<GenesisPlaybackWindowState> States { get; } = [];

        public ThrowingDisposeLifetime FirstLifetime { get; } = new();

        public RecordingLifetime? ReplacementLifetime { get; private set; }

        public int OpenCount => States.Count;

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            if (States.Count == 1)
            {
                return ValueTask.FromResult<IAsyncDisposable?>(FirstLifetime);
            }

            ReplacementLifetime = new RecordingLifetime();
            return ValueTask.FromResult<IAsyncDisposable?>(ReplacementLifetime);
        }
    }

    private sealed class BlockingReplacementTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public List<GenesisPlaybackWindowState> States { get; } = [];

        public BlockingDisposeLifetime FirstLifetime { get; } = new();

        public RecordingLifetime? ReplacementLifetime { get; private set; }

        public int OpenCount => States.Count;

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            States.Add(state);
            if (States.Count == 1)
            {
                return ValueTask.FromResult<IAsyncDisposable?>(FirstLifetime);
            }

            ReplacementLifetime = new RecordingLifetime();
            return ValueTask.FromResult<IAsyncDisposable?>(ReplacementLifetime);
        }
    }

    private sealed class NonCompletingCloseTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public NonCompletingCloseLifetime Lifetime { get; } = new();

        public ValueTask<IAsyncDisposable?> OpenAsync(
            long ownerWindowToken,
            GenesisPlaybackWindowState state,
            CancellationToken cancellationToken)
        {
            Assert.Equal(7144, ownerWindowToken);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IAsyncDisposable?>(Lifetime);
        }
    }

    private sealed class ThrowingDisposeLifetime : IAsyncDisposable
    {
        public bool DisposeAttempted { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            throw new InvalidOperationException("Surface disposal failed.");
        }
    }

    private sealed class NonCompletingCloseLifetime : IGenesisPlaybackWindowCloseAwareLifetime
    {
        private readonly TaskCompletionSource<bool> _closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public Task<bool> ClosedByUser => _closed.Task;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingDisposeLifetime : IGenesisPlaybackWindowCloseAwareLifetime
    {
        private readonly TaskCompletionSource<bool> _closed = new();
        private readonly TaskCompletionSource<bool> _disposeStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposeRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public Task<bool> ClosedByUser => _closed.Task;

        public Task DisposeStarted => _disposeStarted.Task;

        public void RequestUserClose() =>
            _closed.TrySetResult(true);

        public void ReleaseDispose() =>
            _disposeRelease.TrySetResult(true);

        public async ValueTask DisposeAsync()
        {
            Disposed = true;
            _disposeStarted.TrySetResult(true);
            await _disposeRelease.Task.ConfigureAwait(false);
            _closed.TrySetResult(false);
        }
    }

    private sealed class RecordingLifetime : IGenesisPlaybackWindowCloseAwareLifetime
    {
        private readonly TaskCompletionSource<bool> _closed =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public Task<bool> ClosedByUser => _closed.Task;

        public void RequestUserClose() =>
            _closed.TrySetResult(true);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _closed.TrySetResult(false);
            return ValueTask.CompletedTask;
        }
    }
}
