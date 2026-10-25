using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal sealed partial class NovaObsPublishingLifecycleController
{
    private FinishRequest? _finishRequest;
    private Task? _finishWorker;
    private NovaObsPublishingGenerationProof? _generationProof;
    private Task<NovaObsPublishingGenerationProof?>? _generationProofRead;
    internal bool HasCurrentGenerationProof
    {
        get { lock (_gate) return _generationProof?.IsValid == true; }
    }
    private Task<ObsPublishLocalTerminationResult>? _networkFinish;
    internal TimeProvider FinishTimeProvider { get; init; } = TimeProvider.System;
    internal Func<PublishingFinishPoint, Task>? FinishObserver { get; init; }
    internal Task? FinishWorker => _finishWorker;
    internal Task<bool>? CleanupTask => _cleanup;

    public Task<NovaObsPublishingLifecycleResult> RequestFinishAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            if (_finishRequest is not null || _terminal.Task.IsCompleted) return _terminal.Task;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(RejectedFinish(NovaObsPublishingCompletionKind.Cancelled));
            if (!_started || _closing || _disposal is not null || _owner is not INovaObsPublishingFinishRuntime)
                return Task.FromResult(RejectedFinish(NovaObsPublishingCompletionKind.Failed));
            var request = new FinishRequest(timeout, FinishTimeProvider, cancellationToken);
            _finishRequest = request;
            _closing = true;
            _state = NovaObsPublishingLifecycleState.Stopping;
            _finishWorker = Task.Run(() => FinishCoreAsync(request), CancellationToken.None);
            ObserveLateFault(_finishWorker);
            return _terminal.Task;
        }
    }

    private NovaObsPublishingLifecycleResult RejectedFinish(NovaObsPublishingCompletionKind kind) =>
        new(_state, kind, NovaObsPublishingLifecycleFailure.FinishUnavailable, null, false, false);

    private void EnsureStopWorkerLocked()
    {
        if (_stopWorker is not null) return;
        _stopWorker = Task.Run(StopCoreAsync);
        ObserveLateFault(_stopWorker);
    }

    private void ObserveLateFault(Task task) =>
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            Interlocked.Increment(ref _lateFaultCount);
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private void RecordCleanupFailure(NovaObsPublishingLifecycleFailure failure)
    {
        lock (_gate)
        {
            _cleanupFailure = failure;
            if (!_terminal.Task.IsCompleted)
            {
                if (_failure == NovaObsPublishingLifecycleFailure.None || failure == NovaObsPublishingLifecycleFailure.CleanupFailed)
                    _failure = failure;
            }
            else Interlocked.Increment(ref _lateFaultCount);
        }
    }

    private async Task ObserveFinishAsync(PublishingFinishPoint point, FinishRequest request, CancellationToken token)
    {
        if (FinishObserver is not { } observer) return;
        var task = Task.Run(() => observer(point), CancellationToken.None);
        ObserveLateFault(task);
        await task.WaitAsync(token).ConfigureAwait(false);
        request.Check(token);
    }

    // Status is an external borrowed callback too: never call it while holding the
    // lifecycle gate or let its synchronous prefix consume the Finish worker.
    private async Task<NovaObsPublishingReadOnlySnapshot> ReadFinishIdentityAsync(CancellationToken token)
    {
        var read = Task.Run(_status.Read, CancellationToken.None);
        ObserveLateFault(read);
        var snapshot = await read.WaitAsync(token).ConfigureAwait(false);
        Volatile.Read(ref _generationProof)?.Observe(snapshot);
        return snapshot;
    }

    private async Task FinishCoreAsync(FinishRequest request)
    {
        using (request.Deadline)
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(request.Deadline.Token, request.Cancellation))
        {
            ObsPublishLocalTerminationResult? local = null;
            ObsPublishChannelResult? ownerResult = null;
            var cleaned = false;
            var identityMatched = false;
            var eosWon = false;
            var kind = NovaObsPublishingCompletionKind.Failed;
            var failure = NovaObsPublishingLifecycleFailure.FinishFailed;
            NovaObsPublishingReadOnlySnapshot? finalIdentity = null;
            try
            {
                if (_startFinished is not null) await _startFinished.Task.WaitAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                if (_ownerCompletion is null) throw new InvalidOperationException("Owner startup incomplete.");
                _generationProofRead = Task.Run(() => (_owner as INovaObsPublishingGenerationRuntime)?.GenerationProof);
                ObserveLateFault(_generationProofRead);
                request.Proof = await _generationProofRead.WaitAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                if (request.Proof is null || !request.Proof.IsValid || request.Proof.ChannelId == Guid.Empty || request.Proof.SessionId == Guid.Empty)
                    throw new FinishFailure(NovaObsPublishingLifecycleFailure.IdentityUnproven);
                lock (_gate) { _generationProof = request.Proof; }
                var identity = await ReadFinishIdentityAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                if (identity.Availability == NovaObsPublishingAvailability.Unavailable || identity.ChannelId is null || identity.SessionId is null)
                    throw new FinishFailure(NovaObsPublishingLifecycleFailure.IdentityUnproven);
                request.ChannelId = request.Proof.ChannelId;
                request.SessionId = request.Proof.SessionId;
                if (!Matches(request, identity)) throw new FinishFailure(NovaObsPublishingLifecycleFailure.IdentityUnproven);
                await ObserveFinishAsync(PublishingFinishPoint.BeforeNetwork, request, wait.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_stopRequested || _disposal is not null) throw new OperationCanceledException();
                    request.Check(wait.Token);
                    // Publish ownership before any runtime code can execute. A synchronous
                    // blocked callback cannot prevent bounded reconciliation or forced Stop.
                    _networkFinish = Task.Run(() => ((INovaObsPublishingFinishRuntime)_owner!).RequestLocalTerminationAsync(
                        request.Remaining, wait.Token));
                    ObserveLateFault(_networkFinish);
                }
                local = await _networkFinish.WaitAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                ownerResult = await _ownerCompletion.WaitAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                if (local is null || ownerResult is null) throw new InvalidOperationException("Missing termination evidence.");
                eosWon = ownerResult.EndReason == ObsPublishChannelEndReason.PublisherStopped && ownerResult.LocalTermination is null &&
                    local.Outcome == ObsPublishLocalTerminationOutcome.Failed && local.Phase == ObsPublishLocalTerminationPhase.Admission;
                identityMatched = local.ChannelId.Value == request.ChannelId && local.SessionId.Value == request.SessionId &&
                    (ReferenceEquals(ownerResult.LocalTermination, local) || eosWon);
                if (!identityMatched) throw new FinishFailure(NovaObsPublishingLifecycleFailure.IdentityUnproven);
                kind = local.Outcome switch
                {
                    ObsPublishLocalTerminationOutcome.LocalCompleted => NovaObsPublishingCompletionKind.LocalCompleted,
                    ObsPublishLocalTerminationOutcome.TimedOut => NovaObsPublishingCompletionKind.TimedOut,
                    ObsPublishLocalTerminationOutcome.Cancelled => NovaObsPublishingCompletionKind.Cancelled,
                    ObsPublishLocalTerminationOutcome.DeliveryFailed => NovaObsPublishingCompletionKind.DeliveryFailed,
                    ObsPublishLocalTerminationOutcome.CleanupIncomplete => NovaObsPublishingCompletionKind.CleanupIncomplete,
                    _ => NovaObsPublishingCompletionKind.Failed,
                };
                if (eosWon) kind = IsNormalEosProven(ownerResult) ? NovaObsPublishingCompletionKind.ObsNormalEos : NovaObsPublishingCompletionKind.IncompleteEos;
                await ObserveFinishAsync(PublishingFinishPoint.OwnerObserved, request, wait.Token).ConfigureAwait(false);
                cleaned = await CleanupOnceAsync().WaitAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                cleaned &= (local.CleanupCompleted || eosWon) && ownerResult.ShutdownCompleted;
                await ObserveFinishAsync(PublishingFinishPoint.CleanupObserved, request, wait.Token).ConfigureAwait(false);
                finalIdentity = await ReadFinishIdentityAsync(wait.Token).ConfigureAwait(false);
                request.Check(wait.Token);
                identityMatched &= Matches(request, finalIdentity);
                if (!identityMatched) throw new FinishFailure(NovaObsPublishingLifecycleFailure.IdentityUnproven);
                if (!cleaned) { kind = NovaObsPublishingCompletionKind.CleanupIncomplete; failure = NovaObsPublishingLifecycleFailure.CleanupFailed; }
                else if (kind == NovaObsPublishingCompletionKind.LocalCompleted)
                    failure = LocalProof(local, ownerResult) ? NovaObsPublishingLifecycleFailure.None : NovaObsPublishingLifecycleFailure.FinishFailed;
                else if (kind == NovaObsPublishingCompletionKind.ObsNormalEos) failure = NovaObsPublishingLifecycleFailure.None;
                else if (kind == NovaObsPublishingCompletionKind.IncompleteEos) failure = NovaObsPublishingLifecycleFailure.NormalEosUnproven;
                else if (kind == NovaObsPublishingCompletionKind.Cancelled && ownerResult.EndReason == ObsPublishChannelEndReason.HostCancelled)
                    failure = NovaObsPublishingLifecycleFailure.None;
                else failure = kind == NovaObsPublishingCompletionKind.TimedOut ? NovaObsPublishingLifecycleFailure.FinishTimedOut : NovaObsPublishingLifecycleFailure.FinishFailed;
                await ObserveFinishAsync(PublishingFinishPoint.BeforeCommit, request, wait.Token).ConfigureAwait(false);
            }
            catch (FinishFailure exception) { failure = exception.Failure; kind = NovaObsPublishingCompletionKind.Failed; identityMatched = false; cleaned = false; }
            catch (OperationCanceledException)
            {
                bool stopped; lock (_gate) stopped = _stopRequested;
                kind = request.Cancellation.IsCancellationRequested || stopped ? NovaObsPublishingCompletionKind.Cancelled : NovaObsPublishingCompletionKind.TimedOut;
                failure = kind == NovaObsPublishingCompletionKind.TimedOut ? NovaObsPublishingLifecycleFailure.FinishTimedOut : NovaObsPublishingLifecycleFailure.FinishFailed;
                cleaned = false;
            }
            catch (TimeoutException) { kind = NovaObsPublishingCompletionKind.TimedOut; failure = NovaObsPublishingLifecycleFailure.FinishTimedOut; cleaned = false; }
            catch (Exception) { kind = NovaObsPublishingCompletionKind.Failed; failure = NovaObsPublishingLifecycleFailure.FinishFailed; cleaned = false; }

            if (!cleaned)
            {
                lock (_gate) EnsureStopWorkerLocked(); // Owned deferred cleanup; never join Start without the original budget.
                try
                {
                    request.Check(request.Deadline.Token);
                    if (_startFinished is not null) await _startFinished.Task.WaitAsync(request.Deadline.Token).ConfigureAwait(false);
                    var disposed = await CleanupOnceAsync().WaitAsync(request.Deadline.Token).ConfigureAwait(false);
                    request.Check(request.Deadline.Token);
                    finalIdentity = await ReadFinishIdentityAsync(request.Deadline.Token).ConfigureAwait(false);
                    cleaned = disposed && identityMatched && Matches(request, finalIdentity) &&
                        (local?.CleanupCompleted == true || eosWon) && ownerResult?.ShutdownCompleted == true;
                }
                catch (Exception) { /* Task ownership survives the bounded wait; no terminal upgrade. */ }
            }
            lock (_gate)
                lock (request.Proof?.Gate ?? _gate)
                {
                    if (_terminal.Task.IsCompleted) return;
                    // The sealed proof is an owner capability with immutable identity or
                    // synchronized invalidate-before-mutation. This lock linearizes its
                    // invalidation with publication without invoking external callbacks.
                    var generationUnproven = request.Proof is null || !request.Proof.IsValid ||
                        !ReferenceEquals(request.Proof, _generationProof);
                    // Final commit linearizes with Stop/Dispose admission. No external code
                    // is invoked here; all snapshots belong to this immutable owner generation.
                    var forceWon = _stopRequested || _disposal is not null;
                    if (request.Cancellation.IsCancellationRequested)
                    { kind = NovaObsPublishingCompletionKind.Cancelled; failure = NovaObsPublishingLifecycleFailure.FinishFailed; }
                    else if (request.Expired)
                    { kind = NovaObsPublishingCompletionKind.TimedOut; failure = NovaObsPublishingLifecycleFailure.FinishTimedOut; }
                    else if (forceWon && (kind != NovaObsPublishingCompletionKind.Cancelled || ownerResult?.EndReason != ObsPublishChannelEndReason.HostCancelled))
                    { kind = NovaObsPublishingCompletionKind.Cancelled; failure = NovaObsPublishingLifecycleFailure.FinishFailed; }
                    if ((generationUnproven && failure == NovaObsPublishingLifecycleFailure.None) ||
                        request.Proof is { IsValid: false } || (failure == NovaObsPublishingLifecycleFailure.None &&
                        (!identityMatched || !Matches(request, finalIdentity) || !ReferenceEquals(_cleanupResult, ownerResult))))
                    { failure = NovaObsPublishingLifecycleFailure.IdentityUnproven; kind = NovaObsPublishingCompletionKind.Failed; identityMatched = false; cleaned = false; }
                    if (_cleanupFailure != NovaObsPublishingLifecycleFailure.None && failure == NovaObsPublishingLifecycleFailure.None)
                    { failure = _cleanupFailure; kind = NovaObsPublishingCompletionKind.CleanupIncomplete; cleaned = false; }
                    if (failure == NovaObsPublishingLifecycleFailure.FinishFailed && kind == NovaObsPublishingCompletionKind.LocalCompleted)
                        kind = NovaObsPublishingCompletionKind.Failed;
                    // Actual resource disposal remains in _cleanup/_cleanupResult. The
                    // public cleanup proof additionally belongs to this exact generation.
                    cleaned &= !generationUnproven && identityMatched && Matches(request, finalIdentity) && ownerResult?.ShutdownCompleted == true &&
                        ReferenceEquals(_cleanupResult, ownerResult) &&
                        (ReferenceEquals(ownerResult?.LocalTermination, local) || eosWon);
                    if (!identityMatched && failure == NovaObsPublishingLifecycleFailure.IdentityUnproven)
                    { cleaned = false; kind = NovaObsPublishingCompletionKind.Failed; }
                    _failure = failure;
                    _state = failure == NovaObsPublishingLifecycleFailure.None ? NovaObsPublishingLifecycleState.Stopped : NovaObsPublishingLifecycleState.Failed;
                    _terminal.TrySetResult(new(_state, kind, failure, ownerResult?.EndReason, cleaned,
                        kind == NovaObsPublishingCompletionKind.ObsNormalEos && failure == NovaObsPublishingLifecycleFailure.None && cleaned)
                    {
                        FinishRequested = true,
                        OwnerCompletionObserved = ownerResult is not null,
                        IdentityMatched = identityMatched,
                        ChannelId = request.ChannelId,
                        SessionId = request.SessionId,
                        LocalTermination = local,
                        FinishDeadlineExpired = request.Expired,
                        LocalCompletedProven = kind == NovaObsPublishingCompletionKind.LocalCompleted && failure == NovaObsPublishingLifecycleFailure.None && cleaned,
                    });
                }
        }
    }

    private static bool Matches(FinishRequest request, NovaObsPublishingReadOnlySnapshot? snapshot) =>
        snapshot is { Availability: not NovaObsPublishingAvailability.Unavailable } &&
        snapshot.ChannelId == request.ChannelId && snapshot.SessionId == request.SessionId;

    private static bool LocalProof(ObsPublishLocalTerminationResult local, ObsPublishChannelResult owner) =>
        ReferenceEquals(owner.LocalTermination, local) && owner.EndReason == ObsPublishChannelEndReason.LocalTerminated &&
        local.Phase == ObsPublishLocalTerminationPhase.Finished && !local.DeadlineExpired && local.CleanupCompleted &&
        local.UpstreamEnded && local.AcceptedCountersMatched && local.RelayAdmissionClosed && !local.HttpSourceCleanupFailed &&
        local.Ingress?.FinalResult is { } ingress && ingress.AcceptedPacketCount == owner.AcceptedPacketCount &&
        ingress.AcceptedByteCount == owner.AcceptedByteCount &&
        (local.RecordingFinalEvidence is null || (local.RecordingFinalEvidence.CleanupCompleted && !local.RecordingFinalEvidence.AcceptedTerminalFault && !local.RecordingFinalEvidence.DeliveryFailed)) &&
        (local.Http is null || (local.Http.Outcome == PcpRelayCompletionOutcome.Completed && !local.Http.SourceCleanupFailed && local.Http.CleanupCompleted)) &&
        (local.RecordingSource is null || local.RecordingSource.Outcome == PcpRecordingSourceCompletionOutcome.NormalEof);

    private sealed class FinishFailure(NovaObsPublishingLifecycleFailure failure) : Exception
    {
        internal NovaObsPublishingLifecycleFailure Failure { get; } = failure;
    }

    private sealed class FinishRequest(TimeSpan timeout, TimeProvider clock, CancellationToken cancellation)
    {
        private readonly long _started = clock.GetTimestamp();
        internal CancellationTokenSource Deadline { get; } = new(timeout, clock);
        internal CancellationToken Cancellation { get; } = cancellation;
        internal NovaObsPublishingGenerationProof? Proof { get; set; }
        internal Guid? ChannelId { get; set; }
        internal Guid? SessionId { get; set; }
        internal bool Expired => clock.GetElapsedTime(_started) >= timeout || Deadline.IsCancellationRequested;
        internal void Check(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Expired) throw new TimeoutException();
        }
        internal TimeSpan Remaining
        {
            get
            {
                Deadline.Token.ThrowIfCancellationRequested();
                var remaining = timeout - clock.GetElapsedTime(_started);
                if (remaining <= TimeSpan.Zero) throw new TimeoutException();
                return remaining;
            }
        }
    }
}

internal enum PublishingFinishPoint { BeforeNetwork, OwnerObserved, CleanupObserved, BeforeCommit }
