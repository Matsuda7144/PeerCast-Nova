using System.Diagnostics.CodeAnalysis;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Copies each upstream payload once, then offers the shared owned payload to
/// independently bounded relay-child queues. A slow or failing child is
/// stopped without failing the upstream packet consumer or another child.
/// </summary>
public sealed class PcpRelayFanout : IPcpChannelPacketConsumer, IAsyncDisposable
{
    private const uint HeadPacketType = 0x64616568;
    private const int MaximumRetainedHeadBytes = 1024 * 1024;

    private readonly object _gate = new();
    private readonly HashSet<PcpRelayChildWorker> _activeChildren = [];
    private readonly HashSet<PcpRelayChildWorker> _knownChildren = [];
    private readonly PcpRelayFanoutOptions _options;
    private readonly bool _replayHeadForNewChildren;
    private Task<PcpRelayFanoutCleanupResult>? _stopTask;
    private Task<PcpRelayFanoutCompletionResult>? _completionTask;
    private PcpRelayFanoutEvidence _retired = new();
    private long _registeredChildren;
    private bool _completing;
    private bool _forcedCleanup;
    private RetainedHead? _retainedHead;
    private int _consumeActive;
    private bool _stopping;

    public PcpRelayFanout(
        PcpRelayFanoutOptions? options = null,
        bool replayHeadForNewChildren = false)
    {
        _options = options is null
            ? new PcpRelayFanoutOptions()
            : options with { };
        _options.Validate();
        _replayHeadForNewChildren = replayHeadForNewChildren;
    }

    internal bool CanAddChild
    {
        get
        {
            lock (_gate) return !_stopping && !_completing &&
            _knownChildren.Count(child => !child.Completion.IsCompleted) < _options.MaxChildren;
        }
    }
    public int ActiveChildCount
    {
        get
        {
            lock (_gate)
            {
                RetireCompletedChildrenLocked();
                return _activeChildren.Count;
            }
        }
    }

    internal bool HasRetainedHead { get { lock (_gate) { return _retainedHead is not null; } } }

    public PcpRelayFanoutEvidence Evidence
    {
        get
        {
            lock (_gate)
            {
                RetireCompletedChildrenLocked();
                var evidence = _retired with
                {
                    AdmissionClosed = _completing || _stopping,
                    RegisteredChildCount = _registeredChildren,
                    RemainingChildCount = _knownChildren.Count,
                };
                foreach (var child in _knownChildren) evidence = evidence.Add(child.Snapshot);
                return evidence;
            }
        }
    }

    public Task<PcpRelayFanoutCleanupResult>? CleanupCompletion
    {
        get { lock (_gate) { return _stopTask; } }
    }

    public PcpRelayChildRegistration AddChild(IPcpRelayChildSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (!TryAddChild(sink, out var registration) ||
            registration is null)
        {
            throw new InvalidOperationException(
                "The bounded relay-child limit has been reached.");
        }

        return registration;
    }

    /// <summary>
    /// Atomically reserves a bounded child slot. A false result means only
    /// that the finite child limit is currently full; a stopping fan-out still
    /// throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    public bool TryAddChild(
        IPcpRelayChildSink sink,
        [NotNullWhen(true)] out PcpRelayChildRegistration? registration)
    {
        ArgumentNullException.ThrowIfNull(sink);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping || _completing, this);
            RetireCompletedChildrenLocked();

            if (_knownChildren.Count >= _options.MaxChildren)
            {
                registration = null;
                return false;
            }

            var child = new PcpRelayChildWorker(
                sink,
                _options,
                OnChildStoppedAccepting);

            if (_replayHeadForNewChildren &&
                _retainedHead is { } retainedHead &&
                !child.TryEnqueue(
                    retainedHead.Packet,
                    retainedHead.Payload,
                    isRetainedHeadReplay: true))
            {
                child.RequestStop(PcpRelayChildEndReason.OwnerRemoved);
                registration = null;
                return false;
            }

            _activeChildren.Add(child);
            _knownChildren.Add(child);
            _registeredChildren = checked(_registeredChildren + 1);
            registration = new PcpRelayChildRegistration(child);
            // Retire only after the actual task ends, including abnormal cleanup.
            _ = child.Completion.ContinueWith(_ => OnChildCompleted(child),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return true;
        }
    }

    public ValueTask ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packet);
        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.Exchange(ref _consumeActive, 1) != 0)
        {
            throw new InvalidOperationException(
                "Relay fan-out consumption must remain sequential.");
        }

        PcpRelaySharedPayload? payload = null;
        RetainedHead? replacedHead = null;

        try
        {
            if (_replayHeadForNewChildren && packet.Type == HeadPacketType)
            {
                var headByteLimit = (int)Math.Min(
                    MaximumRetainedHeadBytes,
                    _options.MaxPendingBytesPerChild);
                if (packet.Payload.Length > headByteLimit)
                {
                    throw new InvalidDataException(
                        $"The PCP HEAD packet exceeds the bounded {headByteLimit}-byte relay bootstrap limit.");
                }

                payload = PcpRelaySharedPayload.CopyFrom(packet.Payload);
            }

            PcpRelayChildWorker[] children;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_stopping || _completing, this);

                if (payload is not null)
                {
                    var retainedPacket = new PcpChannelPacket(
                        packet.ChannelId,
                        packet.SessionId,
                        packet.Sequence,
                        packet.Type,
                        packet.WirePosition,
                        packet.StreamPosition,
                        payload.Memory,
                        packet.Discontinuity,
                        packet.Continuation);
                    payload.AddReference();
                    replacedHead = _retainedHead;
                    _retainedHead = new RetainedHead(retainedPacket, payload);
                }

                children = [.. _activeChildren];
            }

            replacedHead?.Payload.Dispose();

            if (children.Length == 0)
            {
                return ValueTask.CompletedTask;
            }

            payload ??= PcpRelaySharedPayload.CopyFrom(packet.Payload);

            foreach (var child in children)
            {
                child.TryEnqueue(packet, payload);
            }

            return ValueTask.CompletedTask;
        }
        finally
        {
            payload?.Dispose();
            Volatile.Write(ref _consumeActive, 0);
        }
    }

    /// <summary>
    /// Waits until every currently known child has completed its in-flight
    /// packet and emptied its bounded queue. The caller must stop upstream
    /// consumption before calling this method so no new packets can race the
    /// idle observation.
    /// </summary>
    public Task<bool> WaitForIdleAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
        => WaitForIdleCoreAsync(() =>
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
                return _knownChildren.Select(child => child.Snapshot).ToArray();
            }
        }, timeout, cancellationToken);

    // A closed server admission scope excludes unrelated HTTP/recording children.
    internal static Task<bool> WaitForIdleAsync(
        IReadOnlyList<PcpRelayChildRegistration> children, TimeSpan timeout, CancellationToken cancellationToken)
        => WaitForIdleCoreAsync(() => children.Select(child => child.Snapshot).ToArray(), timeout, cancellationToken);

    private static async Task<bool> WaitForIdleCoreAsync(
        Func<PcpRelayChildStatusSnapshot[]> read, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The relay drain timeout must be positive and no greater than one minute.");
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        try
        {
            while (true)
            {
                if (read().All(child => child.PendingPacketCount == 0 && !child.StartupPending))
                {
                    return true;
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(10),
                    linkedSource.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested &&
                !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>
    /// Caller must first stop upstream consumption. Closes child admission,
    /// reuses the bounded idle wait, then awaits every owned child cleanup.
    /// The first request owns one deadline/token; duplicate calls share its result.
    /// Timeout/cancellation while waiting does not abort a pending send.
    /// </summary>
    public Task<PcpRelayFanoutCompletionResult> CompleteAfterUpstreamEndAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            if (_completionTask is not null) return _completionTask;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(IncompleteResult(PcpRelayCompletionOutcome.Cancelled));
            _completing = true;
            _completionTask = CompleteCoreAsync(new CancellationTokenSource(timeout), cancellationToken);
            return _completionTask;
        }
    }

    private async Task<PcpRelayFanoutCompletionResult> CompleteCoreAsync(
        CancellationTokenSource deadline, CancellationToken cancellationToken)
    {
        await Task.Yield();
        using (deadline)
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken))
        {
            try
            {
                var idle = await WaitForIdleCoreAsync(ReadChildren, TimeSpan.FromMinutes(1), wait.Token).ConfigureAwait(false);
                if (!idle) return IncompleteResult(PcpRelayCompletionOutcome.TimedOut);
                wait.Token.ThrowIfCancellationRequested();
                Task<PcpRelayFanoutCleanupResult> cleanup;
                lock (_gate)
                {
                    if (_forcedCleanup) return IncompleteResult(PcpRelayCompletionOutcome.Cancelled);
                    cleanup = BeginCleanupLocked(forced: false);
                }
                var final = await cleanup.WaitAsync(wait.Token).ConfigureAwait(false);
                var evidence = final.Evidence;
                var outcome = final.Outcome switch
                {
                    PcpRelayCleanupOutcome.Incomplete => PcpRelayCompletionOutcome.CleanupIncomplete,
                    PcpRelayCleanupOutcome.Failed => PcpRelayCompletionOutcome.Failed,
                    _ when IsForcedCleanup => PcpRelayCompletionOutcome.Cancelled,
                    _ when evidence.DeliveryFailed => PcpRelayCompletionOutcome.DeliveryFailed,
                    _ => PcpRelayCompletionOutcome.Completed,
                };
                return new(outcome, evidence.SendsSettled && !evidence.DeliveryFailed,
                    final.ChildrenStopped, final.CleanupCompleted, evidence);
            }
            catch (OperationCanceledException)
            {
                return IncompleteResult(IsForcedCleanup || cancellationToken.IsCancellationRequested
                    ? PcpRelayCompletionOutcome.Cancelled : PcpRelayCompletionOutcome.TimedOut);
            }
            catch (Exception) { return IncompleteResult(PcpRelayCompletionOutcome.Failed); }
        }
    }

    private PcpRelayChildStatusSnapshot[] ReadChildren()
    {
        lock (_gate)
        {
            RetireCompletedChildrenLocked();
            return _knownChildren.Select(child => child.Snapshot).ToArray();
        }
    }

    private bool IsForcedCleanup { get { lock (_gate) { return _forcedCleanup; } } }

    internal PcpRelayFanoutCompletionResult IncompleteResult(PcpRelayCompletionOutcome outcome)
    {
        var evidence = Evidence;
        var cleanup = CleanupCompletion;
        var final = cleanup?.IsCompletedSuccessfully == true ? cleanup.Result : null;
        return new(outcome, evidence.SendsSettled && !evidence.DeliveryFailed,
            final?.ChildrenStopped ?? false, final?.CleanupCompleted ?? false, evidence);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            return new ValueTask(AwaitLegacyCleanupAsync(BeginCleanupLocked(forced: true)));
        }
    }

    private Task<PcpRelayFanoutCleanupResult> BeginCleanupLocked(bool forced)
    {
        if (forced && (_stopTask is null || !_stopTask.IsCompleted)) _forcedCleanup = true;
        if (_stopTask is not null) return _stopTask;
        _stopping = true;
        var children = _knownChildren.ToArray();
        _activeChildren.Clear();
        var retainedHead = _retainedHead;
        _retainedHead = null;
        return _stopTask = StopChildrenAsync(children, retainedHead);
    }

    private static async Task AwaitLegacyCleanupAsync(Task<PcpRelayFanoutCleanupResult> cleanup)
    {
        if ((await cleanup.ConfigureAwait(false)).Outcome == PcpRelayCleanupOutcome.Failed)
            throw new InvalidOperationException("Relay child cleanup failed.");
    }

    private async Task<PcpRelayFanoutCleanupResult> StopChildrenAsync(
        PcpRelayChildWorker[] children,
        RetainedHead? retainedHead)
    {
        await Task.Yield();
        var stopped = false;
        var failed = false;
        try
        {
            foreach (var child in children)
            {
                child.RequestStop(PcpRelayChildEndReason.FanoutStopped);
            }

            var waits = children.Select(async child =>
            {
                try { return await child.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { return false; }
                finally { if (child.Completion.IsCompleted) OnChildCompleted(child); }
            });
            var results = await Task.WhenAll(waits).ConfigureAwait(false);
            stopped = results.All(result => result);
            failed = children.Any(child => child.Completion.IsFaulted || child.Completion.IsCanceled);
        }
        catch (Exception) { failed = true; }
        finally
        {
            try { retainedHead?.Payload.Dispose(); }
            catch (Exception) { failed = true; }
        }
        failed |= Evidence.CleanupFailedChildCount != 0;
        var clean = stopped && !failed;
        return new(failed ? PcpRelayCleanupOutcome.Failed : clean ? PcpRelayCleanupOutcome.Completed :
            PcpRelayCleanupOutcome.Incomplete, stopped, clean, Evidence);
    }

    private void OnChildStoppedAccepting(PcpRelayChildWorker child)
    {
        lock (_gate)
        {
            _activeChildren.Remove(child);
        }
    }

    private void OnChildCompleted(PcpRelayChildWorker child)
    {
        lock (_gate)
        {
            _activeChildren.Remove(child);
            if (_knownChildren.Remove(child))
            {
                var snapshot = child.Snapshot;
                if (child.Completion.IsFaulted || child.Completion.IsCanceled)
                {
                    _ = child.Completion.Exception;
                    snapshot = snapshot with { CleanupFailed = true };
                }
                _retired = _retired.Add(snapshot) with
                { EndedChildCount = checked(_retired.EndedChildCount + 1) };
            }
        }
    }

    private void RetireCompletedChildrenLocked()
    {
        foreach (var child in _knownChildren.Where(child => child.Completion.IsCompleted).ToArray())
            OnChildCompleted(child);
    }

    private sealed record RetainedHead(
        PcpChannelPacket Packet,
        PcpRelaySharedPayload Payload);
}
