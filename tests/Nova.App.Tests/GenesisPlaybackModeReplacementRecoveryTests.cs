namespace PeerCast.Nova.App.Tests;

public sealed class GenesisPlaybackModeReplacementRecoveryTests
{
    [Fact]
    public async Task FailedModeReplacementRestoresPreviousPlaybackSurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new RejectingModeReplacementTarget();
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

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.True(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(GenesisPlaybackWindowMode.Normal, viewModel.PlaybackWindowMode);
        Assert.True(target.FirstLifetime.Disposed);
        Assert.False(target.RestoredLifetime.Disposed);
        Assert.Collection(
            target.States,
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode));
    }

    [Fact]
    public async Task StaleModeReplacementDoesNotRestorePreviousPlaybackSurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new BlockingRejectedModeReplacementTarget();
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
        await target.ReplacementStarted.WaitAsync(TimeSpan.FromSeconds(1));

        await viewModel.StopAsync();
        target.RejectReplacement();
        await replacement;
        await coordinator.SynchronizeAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.True(target.FirstLifetime.Disposed);
        Assert.Collection(
            target.States,
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, state.Mode));
    }

    [Fact]
    public async Task StateChangeDuringFallbackDisposesStaleRecoverySurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new BlockingFallbackTarget();
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
        await target.FallbackStarted.WaitAsync(TimeSpan.FromSeconds(1));

        await viewModel.StopAsync();
        target.ReleaseFallback();
        await replacement;
        await coordinator.SynchronizeAsync();

        Assert.Equal(PlaybackSessionState.Stopped, viewModel.PlaybackState);
        Assert.False(viewModel.IsPlaybackWindowOpen);
        Assert.True(target.FirstLifetime.Disposed);
        Assert.True(target.RecoveryLifetime.Disposed);
        Assert.Collection(
            target.States,
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode));
    }

    [Fact]
    public async Task DisposalDuringFallbackDoesNotAdoptRecoverySurface()
    {
        var dispatcher = new RecordingDispatcher();
        var target = new BlockingFallbackTarget();
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

        viewModel.ShowVideoOnly();
        var replacement = coordinator.SynchronizeAsync().AsTask();
        await target.FallbackStarted.WaitAsync(TimeSpan.FromSeconds(1));

        var disposal = coordinator.DisposeAsync().AsTask();
        target.ReleaseFallback();
        await Task.WhenAll(replacement, disposal);

        Assert.Equal(PlaybackSessionState.Playing, viewModel.PlaybackState);
        Assert.True(viewModel.IsPlaybackWindowOpen);
        Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, viewModel.PlaybackWindowMode);
        Assert.True(target.FirstLifetime.Disposed);
        Assert.True(target.RecoveryLifetime.Disposed);
        Assert.Collection(
            target.States,
            state => Assert.Equal(GenesisPlaybackWindowMode.Normal, state.Mode),
            state => Assert.Equal(GenesisPlaybackWindowMode.VideoOnly, state.Mode),
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

    private sealed class RejectingModeReplacementTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        public List<GenesisPlaybackWindowState> States { get; } = [];

        public RecordingLifetime FirstLifetime { get; } = new();

        public RecordingLifetime RestoredLifetime { get; } = new();

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
                2 => ValueTask.FromResult<IAsyncDisposable?>(null),
                3 => ValueTask.FromResult<IAsyncDisposable?>(RestoredLifetime),
                _ => throw new InvalidOperationException("Unexpected playback surface request."),
            };
        }
    }

    private sealed class BlockingRejectedModeReplacementTarget :
        IGenesisPlaybackWindowSurfaceTarget
    {
        private readonly TaskCompletionSource<bool> _replacementStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _replacementRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<GenesisPlaybackWindowState> States { get; } = [];

        public RecordingLifetime FirstLifetime { get; } = new();

        public Task ReplacementStarted => _replacementStarted.Task;

        public void RejectReplacement() =>
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
                throw new InvalidOperationException("Stale fallback must not be opened.");
            }

            _replacementStarted.TrySetResult(true);
            await _replacementRelease.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private sealed class BlockingFallbackTarget : IGenesisPlaybackWindowSurfaceTarget
    {
        private readonly TaskCompletionSource<bool> _fallbackStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _fallbackRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<GenesisPlaybackWindowState> States { get; } = [];

        public RecordingLifetime FirstLifetime { get; } = new();

        public RecordingLifetime RecoveryLifetime { get; } = new();

        public Task FallbackStarted => _fallbackStarted.Task;

        public void ReleaseFallback() =>
            _fallbackRelease.TrySetResult(true);

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

            if (States.Count == 2)
            {
                return null;
            }

            if (States.Count != 3)
            {
                throw new InvalidOperationException("Unexpected playback surface request.");
            }

            _fallbackStarted.TrySetResult(true);
            await _fallbackRelease.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return RecoveryLifetime;
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
}
