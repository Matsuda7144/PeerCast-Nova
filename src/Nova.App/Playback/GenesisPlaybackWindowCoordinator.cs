using System.ComponentModel;

namespace PeerCast.Nova.App;

/// <summary>
/// Keeps the visible playback window synchronized with the framework-neutral
/// playback-window state. All native creation still flows through the shared
/// application dispatcher and therefore remains on the existing owner thread.
/// </summary>
internal sealed class GenesisPlaybackWindowCoordinator : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IGenesisMainWindowDispatcher _dispatcher;
    private readonly IGenesisPlaybackWindowSurfaceTarget _target;
    private readonly GenesisMainViewModel _viewModel;
    private Task _tail = Task.CompletedTask;
    private Task _closeObserver = Task.CompletedTask;
    private IAsyncDisposable? _surfaceLifetime;
    private GenesisPlaybackWindowState _projectedState = GenesisPlaybackWindowState.Closed;
    private int _surfaceVersion;
    private int _disposed;

    private GenesisPlaybackWindowCoordinator(
        IGenesisMainWindowDispatcher dispatcher,
        IGenesisPlaybackWindowSurfaceTarget target,
        GenesisMainViewModel viewModel)
    {
        _dispatcher = dispatcher;
        _target = target;
        _viewModel = viewModel;
    }

    public static async ValueTask<GenesisPlaybackWindowCoordinator> OpenAsync(
        IGenesisMainWindowDispatcher dispatcher,
        IGenesisPlaybackWindowSurfaceTarget target,
        GenesisMainViewModel viewModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(viewModel);
        cancellationToken.ThrowIfCancellationRequested();

        var coordinator = new GenesisPlaybackWindowCoordinator(
            dispatcher,
            target,
            viewModel);
        coordinator._viewModel.PropertyChanged += coordinator.OnViewModelPropertyChanged;
        await coordinator.SynchronizeAsync(cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await coordinator.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return coordinator;
    }

    internal ValueTask SynchronizeAsync(
        CancellationToken cancellationToken = default) =>
        new(QueueSynchronization(cancellationToken));

    internal ValueTask WaitForCloseNotificationAsync()
    {
        lock (_sync)
        {
            return new(_closeObserver);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Task cleanup;
        lock (_sync)
        {
            _closeObserver = Task.CompletedTask;
            _tail = CleanupAfterAsync(_tail);
            cleanup = _tail;
        }

        await cleanup.ConfigureAwait(false);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(GenesisMainViewModel.PlaybackWindow) or
            nameof(GenesisMainViewModel.IsPlaybackWindowOpen) or
            nameof(GenesisMainViewModel.PlaybackWindowMode) or
            nameof(GenesisMainViewModel.PlaybackWindowSource))
        {
            _ = QueueSynchronization(CancellationToken.None);
        }
    }

    private Task QueueSynchronization(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return Task.CompletedTask;
            }

            _tail = SynchronizeAfterAsync(_tail, cancellationToken);
            return _tail;
        }
    }

    private async Task SynchronizeAfterAsync(
        Task predecessor,
        CancellationToken cancellationToken)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested ||
                Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            await SynchronizeCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // GUI synchronization is best-effort. A failed auxiliary surface must
            // not tear down the shared application host or production boundary.
        }
    }

    private async Task SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        var requestedState = _viewModel.PlaybackWindow;
        if (Equals(requestedState, _projectedState))
        {
            return;
        }

        var previousState = _projectedState;
        var replacingPresentationMode =
            _surfaceLifetime is not null &&
            previousState.IsOpen &&
            requestedState.IsOpen &&
            previousState.Mode != requestedState.Mode &&
            string.Equals(previousState.Source, requestedState.Source, StringComparison.Ordinal);
        var retiredSurface = true;
        if (_surfaceLifetime is not null)
        {
            _ = Interlocked.Increment(ref _surfaceVersion);
            try
            {
                await _surfaceLifetime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                retiredSurface = false;
                // A retiring auxiliary surface must not block projection of the
                // replacement requested by the current playback-window state.
            }
            finally
            {
                _surfaceLifetime = null;
            }
        }

        _projectedState = GenesisPlaybackWindowState.Closed;
        if (!Equals(_viewModel.PlaybackWindow, requestedState) ||
            cancellationToken.IsCancellationRequested ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (!requestedState.IsOpen)
        {
            return;
        }

        var projection = await GenesisPlaybackWindowProjection.OpenAsync(
            _dispatcher,
            _target,
            requestedState,
            cancellationToken).ConfigureAwait(false);
        if (projection.Result == GenesisPlaybackWindowProjectionResult.Projected &&
            projection.Lifetime is not null)
        {
            if (!Equals(_viewModel.PlaybackWindow, requestedState) ||
                cancellationToken.IsCancellationRequested ||
                Volatile.Read(ref _disposed) != 0)
            {
                try
                {
                    await projection.Lifetime.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // A stale projected surface must not replace the latest requested state.
                }

                return;
            }

            var surfaceVersion = Interlocked.Increment(ref _surfaceVersion);
            _surfaceLifetime = projection.Lifetime;
            _projectedState = requestedState;
            _closeObserver = ObserveCloseAsync(projection.Lifetime, surfaceVersion);
            return;
        }

        var replacementFailed =
            projection.Result is GenesisPlaybackWindowProjectionResult.TargetRejected or
                GenesisPlaybackWindowProjectionResult.Faulted;
        if (!replacingPresentationMode ||
            !retiredSurface ||
            !replacementFailed ||
            !Equals(_viewModel.PlaybackWindow, requestedState) ||
            cancellationToken.IsCancellationRequested ||
            Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var fallback = await GenesisPlaybackWindowProjection.OpenAsync(
            _dispatcher,
            _target,
            previousState,
            cancellationToken).ConfigureAwait(false);
        if (fallback.Result != GenesisPlaybackWindowProjectionResult.Projected ||
            fallback.Lifetime is null)
        {
            return;
        }

        if (!Equals(_viewModel.PlaybackWindow, requestedState) ||
            cancellationToken.IsCancellationRequested ||
            Volatile.Read(ref _disposed) != 0)
        {
            try
            {
                await fallback.Lifetime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // A stale recovery surface must not replace the latest requested state.
            }

            return;
        }

        var fallbackVersion = Interlocked.Increment(ref _surfaceVersion);
        _surfaceLifetime = fallback.Lifetime;
        _projectedState = previousState;
        _closeObserver = ObserveCloseAsync(fallback.Lifetime, fallbackVersion);
        RestorePlaybackWindowMode(previousState.Mode);
    }

    private void RestorePlaybackWindowMode(GenesisPlaybackWindowMode mode)
    {
        switch (mode)
        {
            case GenesisPlaybackWindowMode.Normal:
                _viewModel.RestorePlaybackWindow();
                break;
            case GenesisPlaybackWindowMode.VideoOnly:
                _viewModel.ShowVideoOnly();
                break;
            case GenesisPlaybackWindowMode.Fullscreen:
                _viewModel.ShowFullscreen();
                break;
        }
    }

    private async Task ObserveCloseAsync(
        IAsyncDisposable lifetime,
        int surfaceVersion)
    {
        if (lifetime is not IGenesisPlaybackWindowCloseAwareLifetime closeAware)
        {
            return;
        }

        try
        {
            var closedByUser = await closeAware.ClosedByUser.ConfigureAwait(false);
            if (closedByUser &&
                Volatile.Read(ref _disposed) == 0 &&
                Volatile.Read(ref _surfaceVersion) == surfaceVersion)
            {
                await _viewModel.ClosePlaybackWindowAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Auxiliary window-close notification must not tear down the app host.
        }
    }

    private async Task CleanupAfterAsync(Task predecessor)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
        }
        catch
        {
            // Cleanup still owns the current surface after a prior failed sync.
        }

        if (_surfaceLifetime is not null)
        {
            try
            {
                await _surfaceLifetime.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Disposal is finite and must not escape application shutdown.
            }
            finally
            {
                _surfaceLifetime = null;
                _projectedState = GenesisPlaybackWindowState.Closed;
            }
        }
    }
}
