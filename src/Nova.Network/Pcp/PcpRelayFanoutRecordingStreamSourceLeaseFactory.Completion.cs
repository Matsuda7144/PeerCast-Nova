namespace PeerCast.Nova.Network.Pcp;

public sealed partial class PcpRelayFanoutRecordingStreamSourceLeaseFactory
{
    private PcpRecordingSourceEvidence _retiredEvidence = new();
    private long _acquiredLeases;
    private long _acquisitionFailures;
    private bool _ownerFaultRequested;
    private bool _acceptedTerminalFault;
    private int _rejectedCleanupPending;
    private long _rejectedCleanupFailures;
    private int _pendingAcquisitions;
    private TaskCompletionSource? _acquisitionsSettled;
    private Task<PcpRecordingSourceCompletionResult>? _evidenceCompletionTask;

    public PcpRecordingSourceEvidence Evidence
    {
        get
        {
            lock (_gate)
            {
                foreach (var lease in _leases.Where(lease => lease.CleanupCompletion.IsCompleted).ToArray()) Remove(lease);
                var evidence = _retiredEvidence with
                {
                    AdmissionClosed = !_accepting,
                    OwnerFaultRequested = _ownerFaultRequested,
                    AcceptedTerminalFault = _acceptedTerminalFault,
                    TerminalNotificationSelected = _completionTask is not null,
                    TerminalNotificationCompleted = _completionTask?.IsCompletedSuccessfully == true,
                    RejectedLeaseCleanupPendingCount = _rejectedCleanupPending,
                    RejectedLeaseCleanupFailureCount = _rejectedCleanupFailures,
                    PendingAcquisitionCount = _pendingAcquisitions,
                    AcquiredLeaseCount = _acquiredLeases,
                    AcquisitionFailureCount = _acquisitionFailures,
                };
                foreach (var lease in _leases) evidence = evidence.Add(lease.Evidence);
                return evidence;
            }
        }
    }

    /// <summary>
    /// Caller must stop upstream first. Drains only this factory's children,
    /// joins existing EOF/fault notification and waits for natural lease release.
    /// Never disposes an active reader to manufacture completion or writer proof.
    /// First admitted call owns the deadline/token/result; duplicates are not retries.
    /// </summary>
    public Task<PcpRecordingSourceCompletionResult> CompleteWithEvidenceAsync(
        TimeSpan timeout, Exception? terminalFault = null, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            if (_evidenceCompletionTask is not null) return _evidenceCompletionTask;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult(new PcpRecordingSourceCompletionResult(
                    PcpRecordingSourceCompletionOutcome.Cancelled, PcpRecordingSourceCompletionPhase.Acquisition, false, Evidence));
            _accepting = false;
            _completed = true;
            _acceptedTerminalFault = terminalFault is not null;
            return _evidenceCompletionTask = CompleteWithEvidenceCoreAsync(
                new CancellationTokenSource(timeout), terminalFault, cancellationToken);
        }
    }

    private async Task<PcpRecordingSourceCompletionResult> CompleteWithEvidenceCoreAsync(
        CancellationTokenSource deadline, Exception? terminalFault, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var phase = PcpRecordingSourceCompletionPhase.Acquisition;
        using (deadline)
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken))
        {
            try
            {
                Task acquisitions;
                lock (_gate) { acquisitions = _pendingAcquisitions == 0 ? Task.CompletedTask : _acquisitionsSettled!.Task; }
                await acquisitions.WaitAsync(wait.Token).ConfigureAwait(false);
                RecordingLease[] leases;
                lock (_gate)
                {
                    foreach (var lease in _leases.Where(lease => lease.CleanupCompletion.IsCompleted).ToArray()) Remove(lease);
                    leases = [.. _leases];
                }
                phase = PcpRecordingSourceCompletionPhase.PacketDrain;
                if (terminalFault is null)
                {
                    var children = leases.Select(lease => lease.Registration).OfType<PcpRelayChildRegistration>().ToArray();
                    var idle = await PcpRelayFanout.WaitForIdleAsync(children, TimeSpan.FromMinutes(1), wait.Token).ConfigureAwait(false);
                    if (!idle) return Result(PcpRecordingSourceCompletionOutcome.TimedOut, phase, true);
                }
                wait.Token.ThrowIfCancellationRequested();
                phase = PcpRecordingSourceCompletionPhase.TerminalNotification;
                await CompleteAsync(terminalFault).AsTask().WaitAsync(wait.Token).ConfigureAwait(false);
                phase = PcpRecordingSourceCompletionPhase.ChildStop;
                var stopped = Task.WhenAll(leases.Select(lease => lease.StopChildAsync(wait.Token)));
                ObserveLateFailure(stopped);
                await stopped.WaitAsync(wait.Token).ConfigureAwait(false);
                phase = PcpRecordingSourceCompletionPhase.LeaseCleanup;
                var cleanup = Task.WhenAll(leases.Select(lease => lease.CleanupCompletion));
                ObserveLateFailure(cleanup);
                await cleanup.WaitAsync(wait.Token).ConfigureAwait(false);
                wait.Token.ThrowIfCancellationRequested();
                var evidence = Evidence;
                var outcome = !evidence.CleanupCompleted ? PcpRecordingSourceCompletionOutcome.CleanupIncomplete :
                    evidence.DeliveryFailed ? PcpRecordingSourceCompletionOutcome.DeliveryFailed :
                    evidence.AcceptedTerminalFault || evidence.OwnerFaultRequested || evidence.SourceFaultLeaseCount != 0 ? PcpRecordingSourceCompletionOutcome.SourceFault :
                    evidence.AbortedLeaseCount != 0 ? PcpRecordingSourceCompletionOutcome.Cancelled :
                    evidence.EofNotified && evidence.ChildrenStopped ? PcpRecordingSourceCompletionOutcome.NormalEof :
                    PcpRecordingSourceCompletionOutcome.Failed;
                return new(outcome, PcpRecordingSourceCompletionPhase.Finished, false, evidence);
            }
            catch (OperationCanceledException) when (wait.IsCancellationRequested)
            {
                return Result(cancellationToken.IsCancellationRequested ? PcpRecordingSourceCompletionOutcome.Cancelled :
                    phase == PcpRecordingSourceCompletionPhase.LeaseCleanup ? PcpRecordingSourceCompletionOutcome.CleanupIncomplete :
                    PcpRecordingSourceCompletionOutcome.TimedOut, phase, deadline.IsCancellationRequested);
            }
            catch (Exception)
            {
                var evidence = Evidence;
                return new(
                    phase == PcpRecordingSourceCompletionPhase.LeaseCleanup || !evidence.CleanupCompleted
                        ? PcpRecordingSourceCompletionOutcome.CleanupIncomplete
                        : PcpRecordingSourceCompletionOutcome.Failed,
                    phase,
                    deadline.IsCancellationRequested,
                    evidence);
            }
        }
    }

    private PcpRecordingSourceCompletionResult Result(
        PcpRecordingSourceCompletionOutcome outcome, PcpRecordingSourceCompletionPhase phase, bool expired) =>
        new(outcome, phase, expired, Evidence);

    private static void ObserveLateFailure(Task task) =>
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}
