using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Publishing;

public sealed partial class ObsPublishChannelHost
{
    private readonly TaskCompletionSource _localFinishRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private LocalFinish? _localFinish;
    private ObsPublishLocalTerminationResult? _localFinishResult;
    private Task<ObsPublishOwnerCleanupEvidence>? _localCleanupTask;
    private bool _ending;
    private bool _forceRequested;
    private bool _localSuccessCommitted;
    private Task<bool>? _forcedCancellationTask;
    private Task<bool>? _forcedNotificationTask;
    private readonly Dictionary<ObsPublishTerminationPoint, Task<bool>> _ownedReleases = [];
    private Task<PcpDownstreamRelayServerResult>? _relayRunTask;
    internal Func<ObsPublishTerminationPoint, Task>? TerminationObserver { get; init; }
    internal TimeProvider TerminationTimeProvider { get; init; } = TimeProvider.System;
    internal CancellationToken OwnerStopToken => _stop.Token;
    internal Task<ObsRtmpIngestResult> IngressCompletion => _ingest.Completion;
    internal bool DeadlineTimerSignaled => _localFinish?.Deadline.IsCancellationRequested == true;
    internal PcpRelayFanoutRecordingStreamSourceLeaseFactory RecordingSource => _recording;
    internal PcpHttpFlvPlaybackSession? HttpSource => _httpSource;

    private Task ObserveTerminationAsync(ObsPublishTerminationPoint point)
    {
        var task = TerminationObserver?.Invoke(point) ?? Task.CompletedTask;
        _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    /// <summary>
    /// Explicit one-generation request. First admitted call owns a shared deadline/token.
    /// Startup, observation and Dispose never invoke this normal termination path.
    /// </summary>
    public Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        lock (_gate)
        {
            if (_localFinish is not null) return _localFinish.Completion.Task;
            if (cancellationToken.IsCancellationRequested || _disposeTask is not null)
                return Task.FromResult(Unadmitted(ObsPublishLocalTerminationOutcome.Cancelled));
            if (_completion is null || _ending || _completion.IsCompleted)
                return Task.FromResult(Unadmitted(ObsPublishLocalTerminationOutcome.Failed));
            // Completed ingress/child facts win even if RunAsync has not entered finally.
            if (_ingest.Completion.IsCompleted || _relayRunTask?.IsCompleted == true || _httpChild?.Completion.IsCompleted == true)
                return Task.FromResult(Unadmitted(ObsPublishLocalTerminationOutcome.Failed));
            var request = new LocalFinish(timeout, TerminationTimeProvider, cancellationToken);
            _localFinish = request;
            _localFinishRequested.TrySetResult();
            return request.Completion.Task;
        }
    }

    private ObsPublishLocalTerminationResult Unadmitted(ObsPublishLocalTerminationOutcome outcome) =>
        new(ChannelId, SessionId, outcome, ObsPublishLocalTerminationPhase.Admission, false,
            false, false, false, null, null, null, null, null,
            ObsPublishWriterEvidence.NotObserved, null);

    private async Task<ObsPublishLocalTerminationResult> FinishLocallyAsync(
        LocalFinish request, bool started, CancellationToken lifetime)
    {
        using (var wait = CancellationTokenSource.CreateLinkedTokenSource(request.Deadline.Token, request.Cancellation, lifetime))
        {
            var phase = ObsPublishLocalTerminationPhase.Admission;
            var outcome = ObsPublishLocalTerminationOutcome.Failed;
            var admissionClosed = false;
            var upstreamEnded = false;
            var countersMatched = false;
            ObsRtmpQuiescenceResult? ingress = null;
            PcpRelayQuiescenceResult? relay = null;
            PcpHttpFlvCompletionResult? http = null;
            PcpRecordingSourceCompletionResult? recording = null;
            PcpRelayFanoutCompletionResult? fanout = null;
            ObsPublishOwnerCleanupEvidence? cleanup = null;
            try
            {
                if (!started) throw new InvalidOperationException("Owner startup incomplete.");
                await _relay.StopAcceptingAsync().WaitAsync(wait.Token).ConfigureAwait(false);
                admissionClosed = true;
                phase = ObsPublishLocalTerminationPhase.Ingress;
                ingress = await _ingest.RequestQuiescenceAsync(request.Remaining, wait.Token).ConfigureAwait(false);
                upstreamEnded = ingress.FinalResult is { EndReason: ObsRtmpIngestEndReason.Quiesced or ObsRtmpIngestEndReason.PublisherStopped };
                countersMatched = upstreamEnded && ingress.FinalResult!.AcceptedPacketCount == _acceptedPackets &&
                    ingress.FinalResult.AcceptedByteCount == _acceptedBytes;
                Require(ingress.Outcome is ObsRtmpQuiescenceOutcome.Completed or ObsRtmpQuiescenceOutcome.AlreadyEnded,
                    ingress.Outcome == ObsRtmpQuiescenceOutcome.TimedOut ? ObsPublishLocalTerminationOutcome.TimedOut :
                    ingress.Outcome == ObsRtmpQuiescenceOutcome.Cancelled ? ObsPublishLocalTerminationOutcome.Cancelled :
                    ingress.FinalResult?.EndReason == ObsRtmpIngestEndReason.ConsumerRejected ? ObsPublishLocalTerminationOutcome.DeliveryFailed :
                    ObsPublishLocalTerminationOutcome.Failed);
                Require(upstreamEnded && countersMatched, ObsPublishLocalTerminationOutcome.Failed);
                await ObserveTerminationAsync(ObsPublishTerminationPoint.IngressEnded).WaitAsync(wait.Token).ConfigureAwait(false);
                _tracker.TransitionTo(ChannelSessionState.Stopping);
                _headAvailable.TrySetResult(false);
                phase = ObsPublishLocalTerminationPhase.PacketDrain;
                if (!await _fanout.WaitForIdleAsync(request.Remaining, wait.Token).ConfigureAwait(false))
                {
                    // The nested timer can round down its remaining duration.
                    // Do not retry delivery or invent deadline evidence: consume
                    // only the original remaining budget before recording expiry.
                    await request.WaitUntilExpiredAsync(wait.Token).ConfigureAwait(false);
                    throw new LocalFinishFailure(ObsPublishLocalTerminationOutcome.TimedOut);
                }
                await ObserveTerminationAsync(ObsPublishTerminationPoint.SharedIdle).WaitAsync(wait.Token).ConfigureAwait(false);
                phase = ObsPublishLocalTerminationPhase.Relay;
                relay = await _relay.DrainAfterUpstreamEndAsync(request.Remaining, wait.Token).ConfigureAwait(false);
                RequireRelay(relay.Outcome);
                await ObserveTerminationAsync(ObsPublishTerminationPoint.RelayDrained).WaitAsync(wait.Token).ConfigureAwait(false);
                phase = ObsPublishLocalTerminationPhase.Http;
                if (_httpSource is not null)
                {
                    http = await _httpSource.CompleteWithEvidenceAsync(request.Remaining, wait.Token).ConfigureAwait(false);
                    Require(http.Fanout.Evidence.CleanupFailedChildCount == 0, ObsPublishLocalTerminationOutcome.CleanupIncomplete);
                    RequireRelayCompletion(http.Outcome);
                }
                await ObserveTerminationAsync(ObsPublishTerminationPoint.HttpCompleted).WaitAsync(wait.Token).ConfigureAwait(false);
                phase = ObsPublishLocalTerminationPhase.RecordingSource;
                recording = await _recording.CompleteWithEvidenceAsync(request.Remaining, cancellationToken: wait.Token).ConfigureAwait(false);
                var recordingFailure = recording.Outcome switch
                {
                    PcpRecordingSourceCompletionOutcome.TimedOut => ObsPublishLocalTerminationOutcome.TimedOut,
                    PcpRecordingSourceCompletionOutcome.Cancelled when recording.Phase == PcpRecordingSourceCompletionPhase.LeaseCleanup &&
                        request.Expired && !request.Cancellation.IsCancellationRequested && !lifetime.IsCancellationRequested
                        => ObsPublishLocalTerminationOutcome.CleanupIncomplete,
                    PcpRecordingSourceCompletionOutcome.Cancelled => ObsPublishLocalTerminationOutcome.Cancelled,
                    PcpRecordingSourceCompletionOutcome.DeliveryFailed => ObsPublishLocalTerminationOutcome.DeliveryFailed,
                    PcpRecordingSourceCompletionOutcome.CleanupIncomplete => ObsPublishLocalTerminationOutcome.CleanupIncomplete,
                    _ => ObsPublishLocalTerminationOutcome.Failed,
                };
                Require(recording.Outcome == PcpRecordingSourceCompletionOutcome.NormalEof, recordingFailure);
                await ObserveTerminationAsync(ObsPublishTerminationPoint.RecordingCompleted).WaitAsync(wait.Token).ConfigureAwait(false);
                phase = ObsPublishLocalTerminationPhase.Fanout;
                fanout = await _fanout.CompleteAfterUpstreamEndAsync(request.Remaining, wait.Token).ConfigureAwait(false);
                Require(fanout.Evidence.CleanupFailedChildCount == 0, ObsPublishLocalTerminationOutcome.CleanupIncomplete);
                RequireRelayCompletion(fanout.Outcome);
                await ObserveTerminationAsync(ObsPublishTerminationPoint.FanoutCompleted).WaitAsync(wait.Token).ConfigureAwait(false);
                phase = ObsPublishLocalTerminationPhase.Cleanup;
                _ = BeginLocalCleanup(force: false);
                cleanup = await ObserveLocalCleanupAsync(wait.Token).ConfigureAwait(false);
                Require(cleanup.Completed, ObsPublishLocalTerminationOutcome.CleanupIncomplete);
                wait.Token.ThrowIfCancellationRequested();
                phase = ObsPublishLocalTerminationPhase.Finished;
                outcome = ObsPublishLocalTerminationOutcome.LocalCompleted;
            }
            catch (LocalFinishFailure failure)
            {
                outcome = failure.Outcome == ObsPublishLocalTerminationOutcome.Cancelled &&
                    request.Expired && !request.Cancellation.IsCancellationRequested && !lifetime.IsCancellationRequested
                    ? ObsPublishLocalTerminationOutcome.TimedOut : failure.Outcome;
            }
            catch (OperationCanceledException)
            {
                outcome = request.Cancellation.IsCancellationRequested || lifetime.IsCancellationRequested
                    ? ObsPublishLocalTerminationOutcome.Cancelled : ObsPublishLocalTerminationOutcome.TimedOut;
            }
            catch (TimeoutException) { outcome = ObsPublishLocalTerminationOutcome.TimedOut; }
            catch (Exception) { outcome = ObsPublishLocalTerminationOutcome.Failed; }

            if (outcome != ObsPublishLocalTerminationOutcome.LocalCompleted)
            {
                _ = BeginLocalCleanup(force: true);
                cleanup = null;
                // Forced cleanup cancels the owner lifetime. Keep observing it only within
                // the original deadline, not with that now-cancelled processing token.
                try { if (!request.Expired) cleanup = await ObserveLocalCleanupAsync(request.Deadline.Token).ConfigureAwait(false); }
                catch (Exception) { /* Pending cleanup remains owned; no success is fabricated. */ }
            }
            if (_tracker.Snapshot.State != ChannelSessionState.Stopping)
                _tracker.TransitionTo(ChannelSessionState.Stopping);
            _headAvailable.TrySetResult(false);
            _tracker.TransitionTo(ChannelSessionState.Stopped);
            var sourceEvidence = _recording.Evidence;
            var writer = WriterEvidenceFor(sourceEvidence);
            var result = new ObsPublishLocalTerminationResult(ChannelId, SessionId,
                outcome, phase, request.Expired, admissionClosed, upstreamEnded, countersMatched,
                ingress, relay, fanout, http, recording, writer, cleanup);
            return result;
        }
    }

    internal static ObsPublishWriterEvidence WriterEvidenceFor(PcpRecordingSourceEvidence evidence) =>
        evidence.AcquiredLeaseCount == 0 && evidence.PendingAcquisitionCount == 0 &&
        evidence.AcquisitionFailureCount == 0 && evidence.RejectedLeaseCleanupPendingCount == 0 &&
        evidence.RejectedLeaseCleanupFailureCount == 0 && evidence.CleanupPendingLeaseCount == 0 &&
        evidence.CleanupFailedLeaseCount == 0 && !evidence.AcceptedTerminalFault && evidence.CleanupCompleted
            ? ObsPublishWriterEvidence.NotRequired : ObsPublishWriterEvidence.NotObserved;

    private Task<bool> ReleaseOwned(ObsPublishTerminationPoint point, Func<ValueTask> release)
    {
        lock (_gate)
        {
            if (_ownedReleases.TryGetValue(point, out var existing)) return existing;
            // Independent release entry points run outside owner locks. A blocking
            // callback before its first await cannot prevent other releases starting.
            var task = Task.Run(async () =>
            {
                try
                {
                    if (point != ObsPublishTerminationPoint.Cancellation)
                        await ObserveTerminationAsync(ObsPublishTerminationPoint.BeforeResourceRelease).ConfigureAwait(false);
                    await release().ConfigureAwait(false);
                    await ObserveTerminationAsync(point).ConfigureAwait(false);
                    return true;
                }
                catch { return false; }
            });
            _ownedReleases.Add(point, task);
            return task;
        }
    }

    private void RequestForceLocked()
    {
        if (_localSuccessCommitted) return;
        // This transition, under _gate, is the force-admission linearization point.
        _forceRequested = true;
        _forcedCancellationTask ??= ReleaseOwned(ObsPublishTerminationPoint.Cancellation,
            () => new ValueTask(_stop.CancelAsync()));
        _forcedNotificationTask ??= ReleaseOwned(ObsPublishTerminationPoint.RecordingFault,
            () => _recording.CompleteAsync(new IOException("Local termination was not completed.")));
    }

    private Task<ObsPublishOwnerCleanupEvidence> BeginLocalCleanup(bool force)
    {
        lock (_gate)
        {
            if (force) RequestForceLocked();
            if (_localCleanupTask is null)
            {
                _localCleanupTask = LocalCleanupAsync();
                _ = _localCleanupTask.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return _localCleanupTask;
        }
    }

    private async Task<ObsPublishOwnerCleanupEvidence> LocalCleanupAsync()
    {
        await Task.Yield();
        // Normal path reaches this only after upstream, scoped/nested drains and
        // global fanout completion. Forced path may cancel pending work; these
        // independent owners retain and join their own workers and payloads.
        var ingress = ReleaseOwned(ObsPublishTerminationPoint.IngressCleanup, _ingest.DisposeAsync);
        var relay = ReleaseOwned(ObsPublishTerminationPoint.RelayCleanup, _relay.DisposeAsync);
        var fanout = ReleaseOwned(ObsPublishTerminationPoint.FanoutCleanup, _fanout.DisposeAsync);
        var source = _httpSource is null ? Task.FromResult(true) :
            ReleaseOwned(ObsPublishTerminationPoint.HttpSourceCleanup, _httpSource.DisposeAsync);
        var listener = _ownsHttp ? ReleaseOwned(ObsPublishTerminationPoint.HttpListenerCleanup, _http.DisposeAsync) : Task.FromResult(true);
        var network = StopWanNetworkAsync();
        await Task.WhenAll(ingress, relay, fanout, source, listener, network).ConfigureAwait(false);
        var fanoutProof = _fanout.CleanupCompletion;
        var relaySnapshot = _relay.Snapshot;
        return new(await ingress, await relay && relaySnapshot.ActiveConnectionCount == 0 &&
            relaySnapshot.CleanupIncompleteCount == 0 && relaySnapshot.InternalFailureCount == 0 &&
            relaySnapshot.AdmissionCleanupPendingCount == 0 && relaySnapshot.AdmissionCleanupFailedCount == 0 && RelayReservationsReleased,
            await fanout && fanoutProof?.IsCompletedSuccessfully == true && fanoutProof.Result.CleanupCompleted,
            await source && (_httpSource is null || _httpSource.CleanupCompleted), _ownsHttp,
            await listener, _recording.Evidence.CleanupCompleted)
        { WanNetwork = await network };
    }

    private async Task<ObsPublishOwnerCleanupEvidence> ObserveLocalCleanupAsync(CancellationToken token)
    {
        await _localCleanupTask!.WaitAsync(token).ConfigureAwait(false);
        Task<bool>? cancellation;
        Task<bool>? notification;
        lock (_gate) { cancellation = _forcedCancellationTask; notification = _forcedNotificationTask; }
        if (cancellation is not null) await cancellation.WaitAsync(token).ConfigureAwait(false);
        if (notification is not null) await notification.WaitAsync(token).ConfigureAwait(false);
        return CleanupSnapshot()!;
    }

    private ObsPublishOwnerCleanupEvidence? CleanupSnapshot()
    {
        lock (_gate)
        {
            if (_localCleanupTask?.IsCompletedSuccessfully != true) return null;
            return _localCleanupTask.Result with
            {
                RecordingLeases = _recording.Evidence.CleanupCompleted,
                ForcedStopRequested = _forceRequested,
                ForcedCancellationCompleted = _forcedCancellationTask?.IsCompletedSuccessfully == true && _forcedCancellationTask.Result,
                RecordingFaultNotificationCompleted = _forcedNotificationTask?.IsCompletedSuccessfully == true && _forcedNotificationTask.Result,
            };
        }
    }

    private async Task<ObsPublishLocalTerminationResult> FinalizeLocalResultAsync(
        ObsPublishLocalTerminationResult result, bool ownerClean, CancellationToken lifetime)
    {
        var request = _localFinish!;
        try { await ObserveTerminationAsync(ObsPublishTerminationPoint.BeforeCommit).WaitAsync(request.Deadline.Token).ConfigureAwait(false); }
        catch (Exception)
        {
            result = result with { Outcome = request.Expired ? ObsPublishLocalTerminationOutcome.TimedOut : ObsPublishLocalTerminationOutcome.Failed };
        }
        lock (_gate)
        {
            if (result.Outcome == ObsPublishLocalTerminationOutcome.LocalCompleted)
            {
                var outcome = _forceRequested || _disposeTask is not null || request.Cancellation.IsCancellationRequested || lifetime.IsCancellationRequested
                    ? ObsPublishLocalTerminationOutcome.Cancelled : request.Expired ? ObsPublishLocalTerminationOutcome.TimedOut :
                    !ownerClean ? ObsPublishLocalTerminationOutcome.CleanupIncomplete : ObsPublishLocalTerminationOutcome.LocalCompleted;
                result = result with { Outcome = outcome, DeadlineExpired = request.Expired };
                if (outcome == ObsPublishLocalTerminationOutcome.LocalCompleted) _localSuccessCommitted = true;
            }
        }
        if (result.Outcome != ObsPublishLocalTerminationOutcome.LocalCompleted)
        {
            _ = BeginLocalCleanup(force: true);
            ObsPublishOwnerCleanupEvidence? cleanup = null;
            try { if (!request.Expired) cleanup = await ObserveLocalCleanupAsync(request.Deadline.Token).ConfigureAwait(false); } catch { }
            result = result with { Cleanup = cleanup, DeadlineExpired = request.Expired };
        }
        var recordingEvidence = _recording.Evidence;
        return result with
        {
            RecordingFinalEvidence = recordingEvidence,
            WriterEvidence = WriterEvidenceFor(recordingEvidence),
            HttpSourceCleanupFailed = _httpSource?.SourceCleanupFailed == true,
        };
    }

    private static void Require(bool succeeded, ObsPublishLocalTerminationOutcome failure)
    {
        if (!succeeded) throw new LocalFinishFailure(failure);
    }

    private static void RequireRelay(PcpRelayQuiescenceOutcome outcome) => Require(outcome == PcpRelayQuiescenceOutcome.Drained,
        outcome switch
        {
            PcpRelayQuiescenceOutcome.TimedOut => ObsPublishLocalTerminationOutcome.TimedOut,
            PcpRelayQuiescenceOutcome.Cancelled => ObsPublishLocalTerminationOutcome.Cancelled,
            PcpRelayQuiescenceOutcome.DeliveryFailed => ObsPublishLocalTerminationOutcome.DeliveryFailed,
            PcpRelayQuiescenceOutcome.CleanupFailed => ObsPublishLocalTerminationOutcome.CleanupIncomplete,
            _ => ObsPublishLocalTerminationOutcome.Failed,
        });

    private static void RequireRelayCompletion(PcpRelayCompletionOutcome outcome) => Require(outcome == PcpRelayCompletionOutcome.Completed,
        outcome switch
        {
            PcpRelayCompletionOutcome.TimedOut => ObsPublishLocalTerminationOutcome.TimedOut,
            PcpRelayCompletionOutcome.Cancelled => ObsPublishLocalTerminationOutcome.Cancelled,
            PcpRelayCompletionOutcome.DeliveryFailed => ObsPublishLocalTerminationOutcome.DeliveryFailed,
            PcpRelayCompletionOutcome.CleanupIncomplete => ObsPublishLocalTerminationOutcome.CleanupIncomplete,
            _ => ObsPublishLocalTerminationOutcome.Failed,
        });

    private sealed class LocalFinishFailure(ObsPublishLocalTerminationOutcome outcome) : Exception
    {
        public ObsPublishLocalTerminationOutcome Outcome { get; } = outcome;
    }

    private sealed class LocalFinish(TimeSpan timeout, TimeProvider clock, CancellationToken cancellation)
    {
        private readonly long _started = clock.GetTimestamp();
        public CancellationTokenSource Deadline { get; } = new(timeout, clock);
        public CancellationToken Cancellation { get; } = cancellation;
        public TaskCompletionSource<ObsPublishLocalTerminationResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Expired => Deadline.IsCancellationRequested || clock.GetElapsedTime(_started) >= timeout;
        public async Task WaitUntilExpiredAsync(CancellationToken cancellationToken)
        {
            while (!Expired)
            {
                try { await Task.Delay(Remaining, clock, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (Expired) { return; }
            }
        }
        public TimeSpan Remaining
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

internal enum ObsPublishTerminationPoint
{
    BeforeEndSelection, BeforeCommit, BeforeResourceRelease, Cancellation, LifetimeCancellation, RecordingFault, RecordingNotification,
    IngressCleanup, RelayCleanup, FanoutCleanup, HttpSourceCleanup, HttpListenerCleanup,
    RecordingAdmissionClosed, IngressEnded, SharedIdle, RelayDrained, HttpCompleted, RecordingCompleted, FanoutCompleted,
}
