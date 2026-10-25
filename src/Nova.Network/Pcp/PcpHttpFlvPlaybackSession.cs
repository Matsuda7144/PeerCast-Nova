using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

internal sealed class PcpHttpFlvPlaybackSession :
    IOwnedPcpChannelPacketConsumer
{
    private const uint HeadPacketType = 0x64616568;
    private const uint DataPacketType = 0x61746164;

    private readonly object _gate = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _sessionId;
    private readonly PcpRelayFanout _fanout;
    private readonly PcpFlvBootstrapRetention _bootstrap;
    private readonly Action<PcpHttpFlvPlaybackSession> _onDisposed;
    private readonly Action _onHeaderReady;
    private TaskCompletionSource<bool> _headerReady = NewHeaderReadySource();
    private Task? _disposeTask;
    private Task<PcpHttpFlvCompletionResult>? _completionTask;
    private Task<bool>? _legacyCompletionTask;
    private CancellationToken _completionCancellationToken;
    private bool _completing;
    private bool _disposed;
    private bool _headerReadyReported;
    private bool _sourceRemoved;
    private int _sourceCleanupFailed;
    internal bool SourceCleanupFailed => Volatile.Read(ref _sourceCleanupFailed) != 0;

    public Guid ChannelIdValue => _channelId.Value;

    public int ActiveClientCount => _fanout.ActiveChildCount;

    public PcpRelayFanoutEvidence Evidence => _fanout.Evidence;

    public PcpHttpFlvPlaybackSession(
        ChannelId channelId,
        ChannelSessionId sessionId,
        PcpHttpFlvPlaybackOptions options,
        Action<PcpHttpFlvPlaybackSession> onDisposed,
        Action onHeaderReady)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(onDisposed);
        ArgumentNullException.ThrowIfNull(onHeaderReady);

        _channelId = channelId;
        _sessionId = sessionId;
        _fanout = new PcpRelayFanout(
            options.Fanout with
            {
                MaxChildren = options.MaximumConcurrentStreamClients,
            });
        _bootstrap = new PcpFlvBootstrapRetention(options.Bootstrap);
        _onDisposed = onDisposed;
        _onHeaderReady = onHeaderReady;
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (packet.ChannelId != _channelId || packet.SessionId != _sessionId)
        {
            throw new InvalidDataException(
                "The FLV packet does not belong to this channel session.");
        }

        if (packet.Type is not HeadPacketType and not DataPacketType)
        {
            return ValueTask.CompletedTask;
        }

        ValueTask fanoutResult;
        var notifyHeaderReady = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _completing, this);

            if (packet.Discontinuity is not null)
            {
                _bootstrap.Reset();
                _headerReady = NewHeaderReadySource();
            }

            // Observe synchronously while callback-scoped packet memory is valid.
            _bootstrap.Observe(packet.Payload);
            if (_bootstrap.StatusSnapshot.HeaderRetained)
            {
                _headerReady.TrySetResult(true);
                if (!_headerReadyReported)
                {
                    _headerReadyReported = true;
                    notifyHeaderReady = true;
                }
            }

            // The fan-out copies packet.Payload into bounded owned child queues.
            // Its completion is synchronous; child I/O never backpressures PCP.
            fanoutResult = _fanout.ConsumeAsync(packet, cancellationToken);
        }

        if (notifyHeaderReady)
        {
            try
            {
                _onHeaderReady();
            }
            catch
            {
                // Optional readiness reporting must not affect PCP consumption.
            }
        }

        return fanoutResult;
    }

    public async Task<bool> WaitForHeaderAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Task<bool> readyTask;
        lock (_gate)
        {
            if (_disposed || _completing)
            {
                return false;
            }

            if (_bootstrap.StatusSnapshot.HeaderRetained)
            {
                return true;
            }

            readyTask = _headerReady.Task;
        }

        try
        {
            return await readyTask.WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public bool TryAddClient(
        IPcpRelayChildSink sink,
        out PcpRelayChildRegistration? registration,
        out PcpFlvBootstrapPayloadSnapshot? bootstrap)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed || _completing, this);
            if (!_bootstrap.StatusSnapshot.HeaderRetained ||
                !_fanout.TryAddChild(sink, out registration))
            {
                registration = null;
                bootstrap = null;
                return false;
            }

            // Snapshot and child registration share this lock with ConsumeAsync,
            // so no packet can fall between bootstrap and live delivery.
            bootstrap = _bootstrap.Snapshot();
            return true;
        }
    }

    /// <summary>
    /// After upstream has stopped, close admission and drain existing bounded
    /// client queues before removing this source. Cancellation never reports
    /// a successful drain. Fault/abort callers use DisposeAsync directly.
    /// </summary>
    internal Task<bool> CompleteAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed && _completionTask is null) return Task.FromResult(false);
            var completion = CompleteWithEvidenceAsync(timeout, cancellationToken);
            if (_completionTask is null) return LegacyCompleteAsync(completion, cancellationToken);
            return _legacyCompletionTask ??= LegacyCompleteAsync(completion, _completionCancellationToken);
        }
    }

    private static async Task<bool> LegacyCompleteAsync(
        Task<PcpHttpFlvCompletionResult> completion, CancellationToken cancellationToken)
    {
        var result = await completion.ConfigureAwait(false);
        if (result.Outcome == PcpRelayCompletionOutcome.Cancelled)
            throw new OperationCanceledException(cancellationToken);
        return result.Outcome == PcpRelayCompletionOutcome.Completed;
    }

    /// <summary>
    /// One overall deadline includes local send drain and source cleanup. Caller
    /// must first stop upstream. HTTP retains its existing forced cleanup on a
    /// failed/cancelled wait, but never calls that successful delivery or cleanup.
    /// </summary>
    internal Task<PcpHttpFlvCompletionResult> CompleteWithEvidenceAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            if (_completionTask is not null) return _completionTask;
            if (cancellationToken.IsCancellationRequested || _disposed)
                return Task.FromResult(CreateCompletionResult(PcpRelayCompletionOutcome.Cancelled,
                    _fanout.IncompleteResult(PcpRelayCompletionOutcome.Cancelled)));

            _completing = true;
            _completionCancellationToken = cancellationToken;
            _headerReady.TrySetResult(false);
            return _completionTask = CompleteCoreAsync(new CancellationTokenSource(timeout), timeout, cancellationToken);
        }
    }

    private async Task<PcpHttpFlvCompletionResult> CompleteCoreAsync(
        CancellationTokenSource deadline, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Task.Yield();
        using (deadline)
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken))
        {
            PcpRelayFanoutCompletionResult fanout;
            try
            {
                fanout = await _fanout.CompleteAfterUpstreamEndAsync(timeout, wait.Token)
                    .WaitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                fanout = _fanout.IncompleteResult(cancellationToken.IsCancellationRequested
                    ? PcpRelayCompletionOutcome.Cancelled : PcpRelayCompletionOutcome.TimedOut);
            }
            catch (Exception) { fanout = _fanout.IncompleteResult(PcpRelayCompletionOutcome.Failed); }

            // Preserve the existing HTTP abort/cleanup path, but bound this wait.
            var cleanup = DisposeAsync().AsTask();
            var outcome = fanout.Outcome;
            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                outcome = PcpRelayCompletionOutcome.TimedOut;
            if (outcome is PcpRelayCompletionOutcome.TimedOut or PcpRelayCompletionOutcome.Cancelled)
                return CreateCompletionResult(outcome, _fanout.IncompleteResult(outcome));
            try { await cleanup.WaitAsync(wait.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                outcome = cancellationToken.IsCancellationRequested
                    ? PcpRelayCompletionOutcome.Cancelled : PcpRelayCompletionOutcome.TimedOut;
                return CreateCompletionResult(outcome, _fanout.IncompleteResult(outcome));
            }
            catch (Exception) { outcome = PcpRelayCompletionOutcome.CleanupIncomplete; }
            var result = CreateCompletionResult(outcome, fanout);
            return !result.CleanupCompleted && outcome == PcpRelayCompletionOutcome.Completed
                ? result with { Outcome = PcpRelayCompletionOutcome.CleanupIncomplete } : result;
        }
    }

    internal Func<Task>? AfterSourceCleanup { get; set; }

    internal bool CleanupCompleted
    {
        get
        {
            lock (_gate)
            {
                var cleanup = _fanout.CleanupCompletion;
                return !SourceCleanupFailed && _disposeTask?.IsCompletedSuccessfully == true &&
                    cleanup?.IsCompletedSuccessfully == true && cleanup.Result.CleanupCompleted && _sourceRemoved;
            }
        }
    }

    private PcpHttpFlvCompletionResult CreateCompletionResult(
        PcpRelayCompletionOutcome outcome, PcpRelayFanoutCompletionResult fanout)
    {
        lock (_gate)
        {
            var cleanup = _fanout.CleanupCompletion;
            var clean = _disposeTask?.IsCompletedSuccessfully == true &&
                cleanup?.IsCompletedSuccessfully == true && cleanup.Result.CleanupCompleted && _sourceRemoved;
            return new(outcome, _sourceRemoved, clean, fanout) { SourceCleanupFailed = SourceCleanupFailed };
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new(_disposeTask);
            }

            _disposed = true;
            _headerReady.TrySetResult(false);
            _disposeTask = DisposeCoreAsync();
            // Preserve the original task/exception for Dispose callers while
            // retaining ownership of faults after an outer bounded wait ends.
            _ = _disposeTask.ContinueWith(task =>
            {
                _ = task.Exception;
                Interlocked.Exchange(ref _sourceCleanupFailed, 1);
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Even a synchronous source-removal callback is covered by the caller's bounded wait.
        await Task.Yield();
        try
        {
            try { await _fanout.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                _bootstrap.Reset();
                _onDisposed(this);
                lock (_gate) { _sourceRemoved = true; }
                if (AfterSourceCleanup is { } observe) await observe().ConfigureAwait(false);
            }
        }
        catch
        {
            Interlocked.Exchange(ref _sourceCleanupFailed, 1);
            throw;
        }
    }

    private static TaskCompletionSource<bool> NewHeaderReadySource() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
