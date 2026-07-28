using PeerCast.Nova.Playback;

namespace PeerCast.Nova.Windows;

public enum PlaybackWindowsPhysicalRunnerState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
    Disposed,
}

public enum PlaybackWindowsPhysicalRunnerResult
{
    None,
    Started,
    PlatformUnsupported,
    InvalidConfiguration,
    AlreadyStarted,
    Cancelled,
    Stopped,
    Faulted,
    Disposed,
}

public sealed record PlaybackWindowsPhysicalRunnerOptions
{
    public int ClientWidth { get; init; } = 640;

    public int ClientHeight { get; init; } = 360;

    public string Title { get; init; } = "PeerCast Nova Controlled Windows Output";

    public int MaximumPendingOperations { get; init; } = 8;
}

public sealed record PlaybackWindowsPhysicalRunnerSnapshot(
    PlaybackWindowsPhysicalRunnerState State,
    PlaybackWindowsPhysicalRunnerResult Result,
    long WindowToken,
    int OwnerManagedThreadId,
    int PendingOperationCount,
    long AcceptedOperationCount,
    long CompletedOperationCount,
    long CancelledOperationCount,
    long RejectedOperationCount,
    bool HasMessageLoop,
    bool AdmissionClosed);

/// <summary>
/// Application-owned finite Windows window, message loop, and dispatcher host
/// used only by explicit controlled physical-output runs. Nova.Playback borrows
/// the dispatcher and window token; it never owns this thread or window.
/// </summary>
public sealed class PlaybackWindowsPhysicalRunnerHost :
    IPlaybackWindowsOutputDispatcher,
    IAsyncDisposable
{
    private const int MaximumAllowedPendingOperations = 64;
    private readonly object _sync = new();
    private readonly LinkedList<PlaybackWindowsPhysicalRunnerWorkItem> _pending = new();
    private readonly PlaybackWindowsPhysicalRunnerOptions _options;
    private readonly IPlaybackWindowsPhysicalRunnerNative _native;
    private readonly Func<bool> _isSupportedPlatform;
    private readonly TaskCompletionSource<PlaybackWindowsPhysicalRunnerResult> _started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private PlaybackWindowsPhysicalRunnerState _state =
        PlaybackWindowsPhysicalRunnerState.Created;
    private PlaybackWindowsPhysicalRunnerResult _result =
        PlaybackWindowsPhysicalRunnerResult.None;
    private long _windowToken;
    private int _ownerManagedThreadId;
    private long _acceptedOperationCount;
    private long _completedOperationCount;
    private long _cancelledOperationCount;
    private long _rejectedOperationCount;
    private bool _hasMessageLoop;
    private bool _admissionClosed;
    private bool _isDisposed;

    public PlaybackWindowsPhysicalRunnerHost(
        PlaybackWindowsPhysicalRunnerOptions? options = null)
        : this(
            CloneOptions(options),
            new SystemPlaybackWindowsPhysicalRunnerNative(),
            static () => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
    {
    }

    internal PlaybackWindowsPhysicalRunnerHost(
        PlaybackWindowsPhysicalRunnerOptions options,
        IPlaybackWindowsPhysicalRunnerNative native,
        Func<bool> isSupportedPlatform)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(native);
        ArgumentNullException.ThrowIfNull(isSupportedPlatform);
        _options = CloneOptions(options);
        _native = native;
        _isSupportedPlatform = isSupportedPlatform;
    }

    public bool HasAccess =>
        Environment.CurrentManagedThreadId == Volatile.Read(ref _ownerManagedThreadId) &&
        Volatile.Read(ref _ownerManagedThreadId) != 0;

    public PlaybackWindowsPhysicalRunnerSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new(
                    _state,
                    _result,
                    _windowToken,
                    _ownerManagedThreadId,
                    _pending.Count,
                    _acceptedOperationCount,
                    _completedOperationCount,
                    _cancelledOperationCount,
                    _rejectedOperationCount,
                    _hasMessageLoop,
                    _admissionClosed);
            }
        }
    }

    public async ValueTask<PlaybackWindowsPhysicalRunnerResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        Thread thread;
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackWindowsPhysicalRunnerResult.Disposed);
            }

            if (_state != PlaybackWindowsPhysicalRunnerState.Created)
            {
                return SetResult(PlaybackWindowsPhysicalRunnerResult.AlreadyStarted);
            }

            if (!ValidateOptions(_options))
            {
                _state = PlaybackWindowsPhysicalRunnerState.Faulted;
                _admissionClosed = true;
                return SetResult(PlaybackWindowsPhysicalRunnerResult.InvalidConfiguration);
            }

            if (!_isSupportedPlatform())
            {
                _state = PlaybackWindowsPhysicalRunnerState.Stopped;
                _admissionClosed = true;
                _stopped.TrySetResult();
                return SetResult(PlaybackWindowsPhysicalRunnerResult.PlatformUnsupported);
            }

            _state = PlaybackWindowsPhysicalRunnerState.Starting;
            _result = PlaybackWindowsPhysicalRunnerResult.None;
            thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "PeerCast Nova controlled Windows output owner",
            };
            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.MTA);
            }

            _thread = thread;
        }

        thread.Start();
        try
        {
            return await _started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _native.RequestStop();
            await _stopped.Task.ConfigureAwait(false);
            lock (_sync)
            {
                return SetResult(PlaybackWindowsPhysicalRunnerResult.Cancelled);
            }
        }
    }

    public ValueTask<PlaybackWindowsOutputDispatcherInvocation<T>> InvokeAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (HasAccess)
        {
            return ExecuteInlineAsync(operation, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                AddCounter(ref _cancelledOperationCount);
            }

            return ValueTask.FromResult(new PlaybackWindowsOutputDispatcherInvocation<T>(
                PlaybackWindowsOutputDispatcherInvocationResult.Cancelled,
                default!));
        }

        var work = new PlaybackWindowsPhysicalRunnerWorkItem<T>(operation, cancellationToken);
        LinkedListNode<PlaybackWindowsPhysicalRunnerWorkItem> node;
        lock (_sync)
        {
            if (_state != PlaybackWindowsPhysicalRunnerState.Running || _admissionClosed)
            {
                AddCounter(ref _rejectedOperationCount);
                return ValueTask.FromResult(new PlaybackWindowsOutputDispatcherInvocation<T>(
                    PlaybackWindowsOutputDispatcherInvocationResult.Shutdown,
                    default!));
            }

            if (_pending.Count >= _options.MaximumPendingOperations)
            {
                AddCounter(ref _rejectedOperationCount);
                return ValueTask.FromResult(new PlaybackWindowsOutputDispatcherInvocation<T>(
                    PlaybackWindowsOutputDispatcherInvocationResult.Faulted,
                    default!));
            }

            node = _pending.AddLast(work);
            AddCounter(ref _acceptedOperationCount);
        }

        if (!_native.SignalWork())
        {
            lock (_sync)
            {
                if (node.List is not null)
                {
                    _pending.Remove(node);
                    AddCounter(ref _rejectedOperationCount);
                }

                _admissionClosed = true;
                if (_state is PlaybackWindowsPhysicalRunnerState.Running or
                    PlaybackWindowsPhysicalRunnerState.Starting)
                {
                    _state = PlaybackWindowsPhysicalRunnerState.Faulted;
                    _result = PlaybackWindowsPhysicalRunnerResult.Faulted;
                }
            }

            work.CompleteShutdown();
            _native.RequestStop();
        }

        return new(work.Task);
    }

    public async ValueTask<PlaybackWindowsPhysicalRunnerResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        bool wait;
        lock (_sync)
        {
            if (_isDisposed)
            {
                return SetResult(PlaybackWindowsPhysicalRunnerResult.Disposed);
            }

            if (_state == PlaybackWindowsPhysicalRunnerState.Created)
            {
                _state = PlaybackWindowsPhysicalRunnerState.Stopped;
                _admissionClosed = true;
                _stopped.TrySetResult();
                return SetResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
            }

            if (_state is PlaybackWindowsPhysicalRunnerState.Stopped or
                PlaybackWindowsPhysicalRunnerState.Faulted)
            {
                return _state == PlaybackWindowsPhysicalRunnerState.Faulted
                    ? SetResult(PlaybackWindowsPhysicalRunnerResult.Faulted)
                    : SetResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
            }

            _state = PlaybackWindowsPhysicalRunnerState.Stopping;
            _admissionClosed = true;
            CompletePendingShutdownLocked();
            wait = !HasAccess;
        }

        _native.RequestStop();
        if (!wait)
        {
            return PlaybackWindowsPhysicalRunnerResult.Stopped;
        }

        try
        {
            await _stopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                return _state == PlaybackWindowsPhysicalRunnerState.Faulted
                    ? SetResult(PlaybackWindowsPhysicalRunnerResult.Faulted)
                    : SetResult(PlaybackWindowsPhysicalRunnerResult.Stopped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                return SetResult(PlaybackWindowsPhysicalRunnerResult.Cancelled);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }
        }

        await StopAsync().ConfigureAwait(false);
        _native.Dispose();
        lock (_sync)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _state = PlaybackWindowsPhysicalRunnerState.Disposed;
            _result = PlaybackWindowsPhysicalRunnerResult.Disposed;
            _admissionClosed = true;
            _windowToken = 0;
            _ownerManagedThreadId = 0;
            _hasMessageLoop = false;
            CompletePendingShutdownLocked();
        }
    }

    private void ThreadMain()
    {
        Volatile.Write(ref _ownerManagedThreadId, Environment.CurrentManagedThreadId);
        PlaybackWindowsPhysicalRunnerNativeRunResult nativeResult;
        try
        {
            nativeResult = _native.Run(
                _options,
                new(OnNativeStarted, DrainPendingOnOwnerThread));
        }
        catch
        {
            nativeResult = PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted;
        }

        lock (_sync)
        {
            _admissionClosed = true;
            _hasMessageLoop = false;
            _windowToken = 0;
            CompletePendingShutdownLocked();
            if (_state != PlaybackWindowsPhysicalRunnerState.Disposed)
            {
                var faulted = nativeResult == PlaybackWindowsPhysicalRunnerNativeRunResult.Faulted ||
                    _state == PlaybackWindowsPhysicalRunnerState.Faulted;
                _state = faulted
                    ? PlaybackWindowsPhysicalRunnerState.Faulted
                    : PlaybackWindowsPhysicalRunnerState.Stopped;
                _result = faulted
                    ? PlaybackWindowsPhysicalRunnerResult.Faulted
                    : PlaybackWindowsPhysicalRunnerResult.Stopped;
            }

            _started.TrySetResult(
                _state == PlaybackWindowsPhysicalRunnerState.Faulted
                    ? PlaybackWindowsPhysicalRunnerResult.Faulted
                    : PlaybackWindowsPhysicalRunnerResult.Stopped);
            _stopped.TrySetResult();
        }

        Volatile.Write(ref _ownerManagedThreadId, 0);
    }

    private void OnNativeStarted(long windowToken)
    {
        var requestStop = false;
        lock (_sync)
        {
            if (windowToken == 0 || _state != PlaybackWindowsPhysicalRunnerState.Starting)
            {
                _state = PlaybackWindowsPhysicalRunnerState.Faulted;
                _result = PlaybackWindowsPhysicalRunnerResult.Faulted;
                _admissionClosed = true;
                requestStop = true;
            }
            else
            {
                _windowToken = windowToken;
                _hasMessageLoop = true;
                _state = PlaybackWindowsPhysicalRunnerState.Running;
                _result = PlaybackWindowsPhysicalRunnerResult.Started;
                _started.TrySetResult(PlaybackWindowsPhysicalRunnerResult.Started);
            }
        }

        if (requestStop)
        {
            _native.RequestStop();
        }
    }

    private void DrainPendingOnOwnerThread()
    {
        while (true)
        {
            PlaybackWindowsPhysicalRunnerWorkItem? work;
            lock (_sync)
            {
                if (_state != PlaybackWindowsPhysicalRunnerState.Running ||
                    _admissionClosed || _pending.First is null)
                {
                    return;
                }

                work = _pending.First.Value;
                _pending.RemoveFirst();
            }

            var outcome = work.Execute();
            lock (_sync)
            {
                switch (outcome)
                {
                    case PlaybackWindowsPhysicalRunnerWorkItemOutcome.Completed:
                        AddCounter(ref _completedOperationCount);
                        break;
                    case PlaybackWindowsPhysicalRunnerWorkItemOutcome.Cancelled:
                        AddCounter(ref _cancelledOperationCount);
                        break;
                    default:
                        AddCounter(ref _rejectedOperationCount);
                        break;
                }
            }
            work.PublishCompletion();
        }
    }

    private async ValueTask<PlaybackWindowsOutputDispatcherInvocation<T>> ExecuteInlineAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_state != PlaybackWindowsPhysicalRunnerState.Running || _admissionClosed)
            {
                AddCounter(ref _rejectedOperationCount);
                return new(
                    PlaybackWindowsOutputDispatcherInvocationResult.Shutdown,
                    default!);
            }

            AddCounter(ref _acceptedOperationCount);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await operation(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                AddCounter(ref _completedOperationCount);
            }

            return new(PlaybackWindowsOutputDispatcherInvocationResult.Completed, value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                AddCounter(ref _cancelledOperationCount);
            }

            return new(
                PlaybackWindowsOutputDispatcherInvocationResult.Cancelled,
                default!);
        }
        catch
        {
            lock (_sync)
            {
                AddCounter(ref _rejectedOperationCount);
            }

            return new(
                PlaybackWindowsOutputDispatcherInvocationResult.Faulted,
                default!);
        }
    }

    private void CompletePendingShutdownLocked()
    {
        while (_pending.First is not null)
        {
            var work = _pending.First.Value;
            _pending.RemoveFirst();
            AddCounter(ref _rejectedOperationCount);
            work.CompleteShutdown();
        }
    }

    private PlaybackWindowsPhysicalRunnerResult SetResult(
        PlaybackWindowsPhysicalRunnerResult result)
    {
        _result = result;
        return result;
    }

    private static PlaybackWindowsPhysicalRunnerOptions CloneOptions(
        PlaybackWindowsPhysicalRunnerOptions? options) => options is null
        ? new()
        : options with { Title = string.Concat(options.Title) };

    private static bool ValidateOptions(PlaybackWindowsPhysicalRunnerOptions options) =>
        options.ClientWidth is >= 16 and <= 16_384 &&
        options.ClientHeight is >= 16 and <= 16_384 &&
        options.MaximumPendingOperations is >= 1 and <= MaximumAllowedPendingOperations &&
        !string.IsNullOrWhiteSpace(options.Title) &&
        options.Title.Length <= 256;

    private static void AddCounter(ref long counter)
    {
        if (counter < long.MaxValue)
        {
            counter++;
        }
    }
}

internal enum PlaybackWindowsPhysicalRunnerNativeRunResult
{
    Stopped,
    Faulted,
}

internal readonly record struct PlaybackWindowsPhysicalRunnerNativeCallbacks(
    Action<long> Started,
    Action DrainWork);

internal interface IPlaybackWindowsPhysicalRunnerNative : IDisposable
{
    PlaybackWindowsPhysicalRunnerNativeRunResult Run(
        PlaybackWindowsPhysicalRunnerOptions options,
        PlaybackWindowsPhysicalRunnerNativeCallbacks callbacks);

    bool SignalWork();

    bool RequestStop();
}

internal enum PlaybackWindowsPhysicalRunnerWorkItemOutcome
{
    Completed,
    Cancelled,
    Faulted,
    Shutdown,
}

internal abstract class PlaybackWindowsPhysicalRunnerWorkItem
{
    public abstract PlaybackWindowsPhysicalRunnerWorkItemOutcome Execute();

    public abstract void PublishCompletion();

    public abstract void CompleteShutdown();
}

internal sealed class PlaybackWindowsPhysicalRunnerWorkItem<T>(
    Func<CancellationToken, ValueTask<T>> operation,
    CancellationToken cancellationToken) : PlaybackWindowsPhysicalRunnerWorkItem
{
    private readonly TaskCompletionSource<PlaybackWindowsOutputDispatcherInvocation<T>> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PlaybackWindowsOutputDispatcherInvocation<T> _invocation;

    public Task<PlaybackWindowsOutputDispatcherInvocation<T>> Task => _completion.Task;

    public override PlaybackWindowsPhysicalRunnerWorkItemOutcome Execute()
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = operation(cancellationToken);
            var value = pending.IsCompletedSuccessfully
                ? pending.Result
                : pending.AsTask().GetAwaiter().GetResult();
            _invocation = new(
                PlaybackWindowsOutputDispatcherInvocationResult.Completed,
                value);
            return PlaybackWindowsPhysicalRunnerWorkItemOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _invocation = new(
                PlaybackWindowsOutputDispatcherInvocationResult.Cancelled,
                default!);
            return PlaybackWindowsPhysicalRunnerWorkItemOutcome.Cancelled;
        }
        catch
        {
            _invocation = new(
                PlaybackWindowsOutputDispatcherInvocationResult.Faulted,
                default!);
            return PlaybackWindowsPhysicalRunnerWorkItemOutcome.Faulted;
        }
    }

    public override void PublishCompletion() => _completion.TrySetResult(_invocation);

    public override void CompleteShutdown() => _completion.TrySetResult(new(
        PlaybackWindowsOutputDispatcherInvocationResult.Shutdown,
        default!));
}
