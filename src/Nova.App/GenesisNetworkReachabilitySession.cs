using System.Diagnostics;

namespace PeerCast.Nova.App;

/// <summary>
/// Owns one listener/family reachability check lane. Concurrent GUI requests are
/// coalesced onto the same finite check so repeated button activation cannot start
/// overlapping external probes.
/// </summary>
internal sealed class GenesisNetworkReachabilitySession : IAsyncDisposable
{
    private const string CheckerFaultDiagnosticCode = "checker-fault";

    private readonly object _sync = new();
    private readonly IGenesisNetworkReachabilityChecker _checker;
    private readonly GenesisNetworkReachabilityRequest _request;
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private Task<GenesisNetworkReachabilityResult>? _activeCheck;
    private GenesisNetworkReachabilityResult? _lastResult;
    private GenesisNetworkReachabilityState _state =
        GenesisNetworkReachabilityState.Unknown;
    private int _disposed;

    public GenesisNetworkReachabilitySession(
        IGenesisNetworkReachabilityChecker checker,
        GenesisNetworkReachabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(request);
        _checker = checker;
        _request = request;
    }

    public GenesisNetworkReachabilityState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public GenesisNetworkReachabilityResult? LastResult
    {
        get
        {
            lock (_sync)
            {
                return _lastResult;
            }
        }
    }

    public Task<GenesisNetworkReachabilityResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<GenesisNetworkReachabilityResult> completion;

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);

            if (_activeCheck is { IsCompleted: false })
            {
                return _activeCheck;
            }

            _state = GenesisNetworkReachabilityState.Checking;
            completion = new TaskCompletionSource<GenesisNetworkReachabilityResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _activeCheck = completion.Task;
        }

        _ = CompleteCheckAsync(completion, cancellationToken);
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        CancelBestEffort(_lifetimeCancellation);

        Task<GenesisNetworkReachabilityResult>? activeCheck;
        lock (_sync)
        {
            activeCheck = _activeCheck;
        }

        try
        {
            if (activeCheck is not null)
            {
                _ = await activeCheck.ConfigureAwait(false);
            }
        }
        finally
        {
            _lifetimeCancellation.Dispose();
        }
    }

    private async Task CompleteCheckAsync(
        TaskCompletionSource<GenesisNetworkReachabilityResult> completion,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteCheckAsync(cancellationToken)
            .ConfigureAwait(false);

        lock (_sync)
        {
            if (result.Outcome == GenesisNetworkReachabilityOutcome.Cancelled)
            {
                _state = MapPreviousState(_lastResult);
            }
            else
            {
                _lastResult = result;
                _state = MapState(result.Outcome);
            }
        }

        completion.TrySetResult(result);
    }

    private async Task<GenesisNetworkReachabilityResult> ExecuteCheckAsync(
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token,
                cancellationToken);
        var operationToken = operationCancellation.Token;

        try
        {
            operationToken.ThrowIfCancellationRequested();
            var result = await _checker.CheckAsync(_request, operationToken)
                .ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();

            return ReferenceEquals(result.Request, _request)
                ? result
                : CreateFailedResult(startedAt);
        }
        catch (OperationCanceledException)
            when (operationToken.IsCancellationRequested)
        {
            return GenesisNetworkReachabilityResult.Cancelled(
                _request,
                Stopwatch.GetElapsedTime(startedAt),
                DateTimeOffset.UtcNow);
        }
        catch
        {
            return CreateFailedResult(startedAt);
        }
    }

    private GenesisNetworkReachabilityResult CreateFailedResult(long startedAt)
    {
        return GenesisNetworkReachabilityResult.Failed(
            _request,
            Stopwatch.GetElapsedTime(startedAt),
            DateTimeOffset.UtcNow,
            CheckerFaultDiagnosticCode);
    }

    private static GenesisNetworkReachabilityState MapPreviousState(
        GenesisNetworkReachabilityResult? result)
    {
        return result is null
            ? GenesisNetworkReachabilityState.Unknown
            : MapState(result.Outcome);
    }

    private static GenesisNetworkReachabilityState MapState(
        GenesisNetworkReachabilityOutcome outcome)
    {
        return outcome switch
        {
            GenesisNetworkReachabilityOutcome.Open =>
                GenesisNetworkReachabilityState.Open,
            GenesisNetworkReachabilityOutcome.Closed =>
                GenesisNetworkReachabilityState.Closed,
            GenesisNetworkReachabilityOutcome.Failed =>
                GenesisNetworkReachabilityState.Failed,
            GenesisNetworkReachabilityOutcome.Cancelled =>
                GenesisNetworkReachabilityState.Unknown,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "Unknown network reachability outcome."),
        };
    }

    private static void CancelBestEffort(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (AggregateException)
        {
            // Provider cancellation callbacks cannot change session ownership.
        }
    }
}
