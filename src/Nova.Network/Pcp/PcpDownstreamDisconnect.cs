using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>Local process command identity. Never populated from HOST or display text.</summary>
public sealed record PcpDownstreamDisconnectTarget(ChannelId ChannelId, ChannelSessionId OwnerId,
    long ConnectionId, Guid ChildInstanceId);

public enum PcpDownstreamDisconnectOutcome
{
    Accepted, Completed, AlreadyDisconnected, StaleSelection, OwnerChanged, ChannelMismatch,
    NotOwnedDownstream, AlreadyStopping, CleanupUnconfirmed, Failed,
}

/// <summary>Accepted is not cleanup proof. Completion is bounded and never transfers ownership.</summary>
public sealed record PcpDownstreamDisconnectRequest(PcpDownstreamDisconnectOutcome Outcome,
    Task<PcpDownstreamDisconnectOutcome>? Completion = null);

/// <summary>In-process owner command, not a remote management endpoint.</summary>
public interface IPcpDownstreamRelayControl
{
    PcpDownstreamDisconnectRequest RequestDisconnect(PcpDownstreamDisconnectTarget target);
}

/// <summary>
/// Serializes current application Owner validation with bounded Network acceptance.
/// The callback only validates/marks a child; it must not cancel, dispose, await,
/// invoke external code or acquire application locks. Lock order: Owner, server;
/// the child closing flag is claimed atomically without taking a lifecycle lock.
/// </summary>
public interface IPcpDownstreamRelayCommandGate
{
    PcpDownstreamDisconnectRequest AcceptDisconnect(PcpDownstreamDisconnectTarget target,
        Func<PcpDownstreamDisconnectRequest> accept);
}

public sealed partial class PcpDownstreamRelayServer : IPcpDownstreamRelayControl
{
    private bool _manualDisconnectAllowed = true;
    internal void ObserveRecoveryForDisconnect(PcpSmartRelayState state)
    {
        lock (_gate) _manualDisconnectAllowed = state is PcpSmartRelayState.Disabled or PcpSmartRelayState.Receiving;
    }
    internal TimeSpan ManualDisconnectTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public PcpDownstreamDisconnectRequest RequestDisconnect(PcpDownstreamDisconnectTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ChannelId != _channelId) return new(PcpDownstreamDisconnectOutcome.ChannelMismatch);
        if (target.OwnerId.Value != _localSessionId) return new(PcpDownstreamDisconnectOutcome.OwnerChanged);
        PendingDisconnect? pending = null;
        // Never enter application admission code while holding the server gate.
        var receipt = _admission is IPcpDownstreamRelayCommandGate authority
            ? authority.AcceptDisconnect(target, () => AcceptDisconnect(target, out pending))
            : _admission is null ? AcceptDisconnect(target, out pending)
            : new(PcpDownstreamDisconnectOutcome.NotOwnedDownstream);
        // Cancellation callbacks and all cleanup run after ALL acceptance locks.
        pending?.Start();
        return receipt;
    }

    private PcpDownstreamDisconnectRequest AcceptDisconnect(PcpDownstreamDisconnectTarget target,
        out PendingDisconnect? pending)
    {
        pending = null;
        lock (_gate)
        {
            if (_state != PcpDownstreamRelayServerState.Running || Volatile.Read(ref _disposed) != 0 ||
                ForcedStopRequested || _stopSource.IsCancellationRequested || _childAdmission.IsClosed ||
                !_manualDisconnectAllowed)
                return new(PcpDownstreamDisconnectOutcome.AlreadyStopping);
            if (_cleanupIncompleteCount != 0 || _admissionCleanupFailedCount != 0 ||
                _pendingAdmissionCleanup.Any(t => !t.IsCompletedSuccessfully || !t.Result))
                return new(PcpDownstreamDisconnectOutcome.CleanupUnconfirmed);
            var entry = _sessions.FirstOrDefault(e => e.Connection.ConnectionId == target.ConnectionId);
            if (entry is null) return new(PcpDownstreamDisconnectOutcome.AlreadyDisconnected);
            if (target.ChildInstanceId == Guid.Empty || entry.ChildInstanceId != target.ChildInstanceId)
                return new(PcpDownstreamDisconnectOutcome.StaleSelection);
            if (entry.Disconnect is { } existing)
            {
                pending = existing;
                return new(PcpDownstreamDisconnectOutcome.AlreadyStopping, existing.Completion);
            }
            if (entry.Session is not { } session || !session.TryAcceptManualStop())
                return new(PcpDownstreamDisconnectOutcome.NotOwnedDownstream);
            pending = entry.Disconnect = new(entry, session, ManualDisconnectTimeout);
            return new(PcpDownstreamDisconnectOutcome.Accepted, pending.Completion);
        }
    }

    private sealed class PendingDisconnect(SessionEntry entry, PcpDownstreamChildSession session, TimeSpan timeout)
    {
        private readonly TaskCompletionSource<PcpDownstreamDisconnectOutcome> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _started;
        internal Task<PcpDownstreamDisconnectOutcome> Completion => _completion.Task;
        internal void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 0) _ = Task.Run(FinishAsync);
        }
        private async Task FinishAsync()
        {
            using var deadline = new CancellationTokenSource(timeout);
            try
            {
                var cancellation = session.CancelAcceptedManualStopAsync();
                _ = cancellation.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                await cancellation.WaitAsync(deadline.Token).ConfigureAwait(false);
                await entry.Task!.WaitAsync(deadline.Token).ConfigureAwait(false);
                await entry.Connection.CleanupCompletion.WaitAsync(deadline.Token).ConfigureAwait(false);
                var clean = await session.AdmissionCleanupCompletion.WaitAsync(deadline.Token).ConfigureAwait(false);
                _completion.TrySetResult(clean && entry.CleanupConfirmed
                    ? PcpDownstreamDisconnectOutcome.Completed : PcpDownstreamDisconnectOutcome.CleanupUnconfirmed);
            }
            catch (Exception)
            {
                // Existing server/child cleanup retains late workers and reservations.
                // Timing out this observation never releases or disposes them again.
                _completion.TrySetResult(PcpDownstreamDisconnectOutcome.CleanupUnconfirmed);
            }
        }
    }
}

public sealed partial class PcpDownstreamChildSession
{
    internal bool TryAcceptManualStop()
    {
        var run = Volatile.Read(ref _runTask);
        if (Volatile.Read(ref _disposed) || run?.IsCompleted != false || !IsEstablishedRelay ||
            _operationToken.IsCancellationRequested) return false;
        // Never wait for Dispose's lifecycle lock (which may be running cancellation
        // callbacks) while holding Owner/server gates. Natural termination uses the
        // same flag; a failed post-check simply leaves the already-closing child alone.
        return Interlocked.CompareExchange(ref _observationClosing, 1, 0) == 0 &&
            !Volatile.Read(ref _disposed) && !run.IsCompleted && !_operationToken.IsCancellationRequested;
    }

    internal Task CancelAcceptedManualStopAsync()
    {
        try { return _lifetimeSource.CancelAsync(); }
        catch (ObjectDisposedException) { return Task.CompletedTask; }
    }
}

public sealed partial class PcpRelayCoordinator : IPcpDownstreamRelayControl
{
    public PcpDownstreamDisconnectRequest RequestDisconnect(PcpDownstreamDisconnectTarget target) =>
        _downstreamServer.RequestDisconnect(target);
}
