using PeerCast.Nova.Foundation.Logging;

namespace PeerCast.Nova.Core;

/// <summary>
/// Coordinates module lifecycle and control flow.
/// Stream payload bytes bypass this class and travel through the data plane.
/// </summary>
public sealed class NovaApplication : IAsyncDisposable
{
    private readonly IReadOnlyList<INovaModule> _modules;
    private readonly INovaLogger _logger;
    private readonly NovaApplicationOptions _options;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly List<INovaModule> _startedModules = [];
    private bool _isRunning;
    private int _disposeState;

    public NovaApplication(
        IEnumerable<INovaModule> modules,
        INovaLogger logger,
        NovaApplicationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);

        _modules = modules.ToArray();
        _logger = logger;
        _options = options is null
            ? new NovaApplicationOptions()
            : options with { };
        _options.Validate();
    }

    public bool IsRunning => Volatile.Read(ref _isRunning);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            if (_isRunning)
            {
                return;
            }

            try
            {
                foreach (var module in _modules)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    _logger.Log(LogLevel.Information, $"Starting module: {module.Name}");
                    await module.StartAsync(cancellationToken).ConfigureAwait(false);
                    _startedModules.Add(module);
                }

                Volatile.Write(ref _isRunning, true);
                _logger.Log(LogLevel.Information, "PeerCast Nova core started.");
            }
            catch
            {
                await StopStartedModulesBestEffortAsync(
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await StopCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        try
        {
            await StopCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_isRunning && _startedModules.Count == 0)
            {
                return;
            }

            var cancellationObserved =
                await StopStartedModulesBestEffortAsync(
                    cancellationToken).ConfigureAwait(false);

            Volatile.Write(ref _isRunning, false);
            _logger.Log(LogLevel.Information, "PeerCast Nova core stopped.");

            if (cancellationObserved)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task<bool> StopStartedModulesBestEffortAsync(
        CancellationToken cancellationToken)
    {
        var cancellationObserved = false;

        for (var index = _startedModules.Count - 1; index >= 0; index--)
        {
            var module = _startedModules[index];
            Task? stopTask = null;

            using var timeoutSource = new CancellationTokenSource(
                _options.ModuleStopTimeout);

            using var linkedSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);

            try
            {
                _logger.Log(LogLevel.Information, $"Stopping module: {module.Name}");
                stopTask = module.StopAsync(linkedSource.Token).AsTask();
                await stopTask.WaitAsync(linkedSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                cancellationObserved = true;
                ObserveLateFailure(stopTask);
                _logger.Log(
                    LogLevel.Warning,
                    $"Module '{module.Name}' stop was canceled; continuing best-effort shutdown.");
            }
            catch (OperationCanceledException)
                when (timeoutSource.IsCancellationRequested)
            {
                ObserveLateFailure(stopTask);
                _logger.Log(
                    LogLevel.Warning,
                    $"Module '{module.Name}' did not stop within {_options.ModuleStopTimeout}; continuing shutdown.");
            }
            catch (Exception exception)
            {
                _logger.Log(
                    LogLevel.Error,
                    $"Module '{module.Name}' failed while stopping.",
                    exception);
            }
        }

        _startedModules.Clear();
        Volatile.Write(ref _isRunning, false);
        return cancellationObserved;
    }

    private static void ObserveLateFailure(Task? task)
    {
        if (task is null)
        {
            return;
        }

        if (task.IsFaulted)
        {
            _ = task.Exception;
            return;
        }

        if (task.IsCompleted)
        {
            return;
        }

        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeState) != 0,
            this);
    }
}
