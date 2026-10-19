using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Concrete recorder backend that writes injected readable streams into
/// injected file paths. It has no PCP, playback, GUI, or default-path policy.
/// </summary>
public sealed class RecordingFileSink :
    IRecordingStopAdmissionSink,
    IRecordingSinkSessionMonitor
{
    public const int DefaultMaximumActiveSessionCount = 4096;

    private readonly object gate = new();
    private readonly IRecordingStreamSourceProvider sourceProvider;
    private readonly IRecordingFilePathProvider pathProvider;
    private readonly int maximumActiveSessionCount;
    private readonly IRecordingSessionTerminalObserver? terminalObserver;
    private readonly TimeProvider observationTimeProvider;
    private readonly IRecordingDiagnosticObserver? diagnosticObserver;
    private readonly Dictionary<ChannelId, ActiveRecording> activeByChannel = [];
    // Canonical session references keep recovery available even when immediate
    // completion removes the active dictionary entry. No permanent history.
    private readonly ConditionalWeakTable<RecordingSinkSession, RecordingSinkStopAdmission>
        stopAdmissions = new();

    public RecordingSinkStopAdmissionResult RequestStopAdmission(
        RecordingSinkStopRequest request,
        CancellationToken admissionToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ActiveRecording? active;
        RecordingSinkStopAdmissionResult result;
        lock (gate)
        {
            if (admissionToken.IsCancellationRequested)
            {
                return RecordingSinkStopAdmissionResult.NotAdmitted(
                    RecordingSinkStopNotAdmittedReason.CancelledBeforeAdmission);
            }

            if (!activeByChannel.TryGetValue(request.Session.ChannelId, out active) ||
                active.Session.SessionId != request.Session.SessionId)
            {
                return RecordingSinkStopAdmissionResult.NotAdmitted(
                    RecordingSinkStopNotAdmittedReason.Unavailable);
            }

            result = active.RequestStopAdmission(admissionToken);
            if (result.Admission is { } receipt)
            {
                stopAdmissions.Add(active.Session, receipt);
                if (!ReferenceEquals(active.Session, request.Session))
                {
                    stopAdmissions.Add(request.Session, receipt);
                }
            }
        }

        if (result.Admission is not null)
        {
            active.StartOwnedStop(() => CompleteAdmittedStopAsync(active));
        }

        return result;
    }

    public bool TryGetStopAdmission(
        RecordingSinkSession session,
        out RecordingSinkStopAdmission? admission)
    {
        ArgumentNullException.ThrowIfNull(session);
        lock (gate)
        {
            if (stopAdmissions.TryGetValue(session, out admission))
            {
                return true;
            }

            foreach (var pair in stopAdmissions)
            {
                if (pair.Key.ChannelId == session.ChannelId &&
                    pair.Key.SessionId == session.SessionId)
                {
                    admission = pair.Value;
                    return true;
                }
            }

            if (activeByChannel.TryGetValue(session.ChannelId, out var active) &&
                active.Session.SessionId == session.SessionId)
            {
                admission = active.Admission;
                return admission is not null;
            }

            admission = null;
            return false;
        }
    }

    private async Task CompleteAdmittedStopAsync(ActiveRecording active)
    {
        RecordingFaultDescriptor? failure = null;
        var notification = active.NotifyStopAsync();
        try
        {
            await active.WaitForCompletionAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = active.GetFault() ?? RecordingFaultDescriptor.FromException(exception);
        }

        // Join notification even if the writer itself failed.
        var notificationFailure = await notification.ConfigureAwait(false);
        failure ??= notificationFailure;
        var result = failure is not null
            ? RecordingSinkStopResult.FromFault(failure)
            : active.NoDecodableVideoStart
                ? RecordingSinkStopResult.NoDecodableVideoStart
                : RecordingSinkStopResult.Stopped;
        RemoveIfCurrent(active);
        active.Dispose();
        active.PublishStopCompletion(result);
    }

    public RecordingFileSink(
        IRecordingStreamSourceProvider sourceProvider,
        IRecordingFilePathProvider pathProvider,
        int maximumActiveSessionCount = DefaultMaximumActiveSessionCount,
        IRecordingSessionTerminalObserver? terminalObserver = null,
        TimeProvider? observationTimeProvider = null,
        IRecordingDiagnosticObserver? diagnosticObserver = null)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);
        ArgumentNullException.ThrowIfNull(pathProvider);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumActiveSessionCount);

        this.sourceProvider = sourceProvider;
        this.pathProvider = pathProvider;
        this.maximumActiveSessionCount = maximumActiveSessionCount;
        this.terminalObserver = terminalObserver;
        this.observationTimeProvider = observationTimeProvider ?? TimeProvider.System;
        this.diagnosticObserver = diagnosticObserver;
    }

    public async ValueTask<RecordingSinkStartResult> StartAsync(
        RecordingSinkStartRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ActiveRecording active;

        lock (gate)
        {
            if (activeByChannel.TryGetValue(
                    request.Candidate.ChannelId,
                    out var existing))
            {
                return RecordingSinkStartResult.AlreadyRunning(
                    existing.Session);
            }

            if (activeByChannel.Count >= maximumActiveSessionCount)
            {
                throw new InvalidOperationException(
                    "The recording file sink active session limit has been reached.");
            }

            var sessionId = new RecordingSessionId(Guid.NewGuid());
            var session = request.Origin switch
            {
                RecordingSessionOrigin.Manual =>
                    RecordingSinkSession.Manual(
                        sessionId,
                        request.Candidate.ChannelId),

                RecordingSessionOrigin.AutomaticRule =>
                    new RecordingSinkSession(
                        sessionId,
                        request.Candidate.ChannelId,
                        request.RuleId ?? throw new InvalidOperationException(
                            "An automatic recording request must select a rule.")),

                _ => throw new InvalidOperationException(
                    $"Unsupported recording session origin: {request.Origin}."),
            };

            active = new ActiveRecording(
                session,
                request,
                terminalObserver,
                observationTimeProvider,
                diagnosticObserver);
            activeByChannel.Add(
                request.Candidate.ChannelId,
                active);
        }

        Stream? source = null;

        try
        {
            var path = pathProvider.GetPath(
                request,
                active.Session.SessionId);

            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            using (var sourceOpenLifetime =
                   CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    active.StoppingToken))
            {
                source = await sourceProvider.OpenReadAsync(
                    request,
                    sourceOpenLifetime.Token).ConfigureAwait(false);
            }

            ArgumentNullException.ThrowIfNull(source);

            if (!source.CanRead)
            {
                var unreadableSource = source;
                source = null;
                await unreadableSource.DisposeAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The recording source provider returned an unreadable stream.");
            }

            if (string.Equals(
                    System.IO.Path.GetExtension(path),
                    ".flv",
                    StringComparison.OrdinalIgnoreCase))
            {
                source = new FlvRecordingTimestampNormalizingStream(
                    new FlvRecordingDecodableStartStream(source));
            }

            if (!active.TryStart(
                source,
                path))
            {
                var stoppedSource = source;
                source = null;
                await stoppedSource.DisposeAsync().ConfigureAwait(false);

                active.CompleteStartWithoutWrite();
                CleanupUnadmittedStart(active);

                return RecordingSinkStartResult.Rejected;
            }

            source = null;

            return RecordingSinkStartResult.Started(
                active.Session);
        }
        catch (Exception exception)
        {
            var disposeFailure = source is null
                ? null
                : await TryDisposeSourceAsync(source).ConfigureAwait(false);
            source = null;

            if (exception is OperationCanceledException &&
                disposeFailure is null &&
                !cancellationToken.IsCancellationRequested &&
                active.StopRequested)
            {
                active.CompleteStartWithoutWrite();
                CleanupUnadmittedStart(active);

                return RecordingSinkStartResult.Rejected;
            }

            var failure = disposeFailure is null
                ? exception
                : new AggregateException(
                    "Recording source acquisition and disposal both failed.",
                    exception,
                    disposeFailure);

            active.CompleteStartFailure(failure);
            CleanupUnadmittedStart(active);

            if (disposeFailure is not null)
            {
                throw failure;
            }

            throw;
        }
    }

    public async ValueTask<RecordingSinkStopResult> StopAsync(
        RecordingSinkStopRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        ActiveRecording? active;

        lock (gate)
        {
            _ = activeByChannel.TryGetValue(
                request.Session.ChannelId,
                out active);

            if (active is null ||
                active.Session.SessionId != request.Session.SessionId)
            {
                return RecordingSinkStopResult.AlreadyStopped;
            }
        }

        if (active.Admission is { } admission)
        {
            return await admission.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        active.RequestStop();

        if (active.Admission is { } concurrentAdmission)
        {
            return await concurrentAdmission.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await active.WaitForCompletionAsync(
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var fault = active.GetFault() ??
                RecordingFaultDescriptor.FromException(exception);
            RemoveIfCurrent(active);
            active.Dispose();
            return RecordingSinkStopResult.FromFault(fault);
        }

        RemoveIfCurrent(active);
        var noDecodableVideoStart = active.NoDecodableVideoStart;
        active.Dispose();

        return noDecodableVideoStart
            ? RecordingSinkStopResult.NoDecodableVideoStart
            : RecordingSinkStopResult.Stopped;
    }

    public bool TryGetStatus(
        RecordingSinkSession session,
        out RecordingFileSinkSessionStatus? status)
    {
        ArgumentNullException.ThrowIfNull(session);

        ActiveRecording? active;

        lock (gate)
        {
            _ = activeByChannel.TryGetValue(
                session.ChannelId,
                out active);

            if (active is null ||
                active.Session.SessionId != session.SessionId)
            {
                status = null;
                return false;
            }
        }

        status = active.GetStatus();
        return true;
    }

    public bool TryGetSessionStatus(
        RecordingSinkSession session,
        out RecordingSinkSessionLifecycleStatus? status)
    {
        if (!TryGetStatus(
                session,
                out var fileStatus))
        {
            status = null;
            return false;
        }

        var kind = fileStatus!.Kind switch
        {
            RecordingFileSinkSessionStatusKind.Starting =>
                RecordingSinkSessionLifecycleKind.Active,

            RecordingFileSinkSessionStatusKind.Writing =>
                RecordingSinkSessionLifecycleKind.Active,

            RecordingFileSinkSessionStatusKind.Completed =>
                RecordingSinkSessionLifecycleKind.Completed,

            RecordingFileSinkSessionStatusKind.NoDecodableVideoStart =>
                RecordingSinkSessionLifecycleKind.NoDecodableVideoStart,

            RecordingFileSinkSessionStatusKind.Faulted =>
                RecordingSinkSessionLifecycleKind.Faulted,

            _ => throw new InvalidOperationException(
                $"Unsupported recording file session status: {fileStatus.Kind}."),
        };

        status = new RecordingSinkSessionLifecycleStatus(
            session,
            kind,
            fileStatus.Fault);
        return true;
    }

    private void CleanupUnadmittedStart(ActiveRecording active)
    {
        if (active.Admission is null)
        {
            RemoveIfCurrent(active);
            active.Dispose();
        }
    }

    private void RemoveIfCurrent(ActiveRecording active)
    {
        lock (gate)
        {
            if (activeByChannel.TryGetValue(
                    active.Session.ChannelId,
                    out var current) &&
                ReferenceEquals(current, active))
            {
                activeByChannel.Remove(
                    active.Session.ChannelId);
            }
        }
    }

    private static async ValueTask<Exception?> TryDisposeSourceAsync(
        Stream source)
    {
        try
        {
            await source.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class ActiveRecording : IDisposable
    {
        private readonly RecordingSinkStartRequest startRequest;
        private readonly IRecordingSessionTerminalObserver? terminalObserver;
        private readonly TimeProvider observationTimeProvider;
        private readonly IRecordingDiagnosticObserver? diagnosticObserver;
        private RecordingTransformFailure transformFailure;
        private bool transformFailed;
        private DateTimeOffset? admissionObservedAtUtc;
        private readonly object gate = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly TaskCompletionSource<Task<RecordingFileWriteOutcome>?>
            startCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
        private Task<RecordingFileWriteOutcome>? writeTask;
        private RecordingFileWriteOutcome? writeOutcome;
        private RecordingFaultDescriptor? fault;
        private Exception? startFailure;
        private bool faulted;
        private bool stopRequested;
        private bool isDisposed;
        private bool terminalCommitted;
        private RecordingSinkStopAdmission? admission;
        private readonly TaskCompletionSource<RecordingSinkStopResult> stopCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? ownedStop;

        public RecordingSinkStopAdmission? Admission
        {
            get
            {
                lock (gate)
                {
                    return admission;
                }
            }
        }

        public RecordingSinkStopAdmissionResult RequestStopAdmission(CancellationToken token)
        {
            lock (gate)
            {
                if (token.IsCancellationRequested)
                {
                    return RecordingSinkStopAdmissionResult.NotAdmitted(
                        RecordingSinkStopNotAdmittedReason.CancelledBeforeAdmission);
                }

                if (terminalCommitted || isDisposed)
                {
                    return RecordingSinkStopAdmissionResult.NotAdmitted(
                        RecordingSinkStopNotAdmittedReason.AlreadyTerminal);
                }

                if (stopRequested)
                {
                    return RecordingSinkStopAdmissionResult.NotAdmitted(
                        RecordingSinkStopNotAdmittedReason.AlreadyStoppingByOtherOperation);
                }

                admission = new RecordingSinkStopAdmission(Session, stopCompletion.Task);
                // Linearization: the canonical receipt and irreversible effect
                // are committed under the same authority as terminal publication.
                stopRequested = true;
                return RecordingSinkStopAdmissionResult.Admitted(admission);
            }
        }

        public void StartOwnedStop(Func<Task> complete)
        {
            lock (gate)
            {
                ownedStop ??= Task.Run(async () =>
                {
                    try
                    {
                        await complete().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        // Unexpected cleanup failure is not terminal proof.
                        // Keep the receipt pending and expose a bounded error.
                        admission!.RecordResolutionFault(exception);
                    }
                });
            }
        }

        public async Task<RecordingFaultDescriptor?> NotifyStopAsync()
        {
            try
            {
                await stopping.CancelAsync().ConfigureAwait(false);
                return null;
            }
            catch (Exception exception)
            {
                return RecordingFaultDescriptor.FromException(exception);
            }
        }

        public void PublishStopCompletion(RecordingSinkStopResult result) =>
            stopCompletion.TrySetResult(result);

        public ActiveRecording(
            RecordingSinkSession session,
            RecordingSinkStartRequest startRequest,
            IRecordingSessionTerminalObserver? terminalObserver,
            TimeProvider observationTimeProvider,
            IRecordingDiagnosticObserver? diagnosticObserver)
        {
            Session = session;
            this.startRequest = startRequest;
            this.terminalObserver = terminalObserver;
            this.observationTimeProvider = observationTimeProvider;
            this.diagnosticObserver = diagnosticObserver;
        }

        public RecordingSinkSession Session { get; }

        public CancellationToken StoppingToken => stopping.Token;

        public bool StopRequested
        {
            get
            {
                lock (gate)
                {
                    return stopRequested;
                }
            }
        }

        public bool TryStart(
            Stream source,
            string path)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            lock (gate)
            {
                if (isDisposed || stopRequested)
                {
                    return false;
                }

                if (writeTask is not null)
                {
                    throw new InvalidOperationException(
                        "The recording file session has already started.");
                }

                // Only a writer admitted here produces a terminal history fact.
                // Source acquisition failures/cancellations remain start attempts.
                if (terminalObserver is not null)
                {
                    try
                    {
                        admissionObservedAtUtc = observationTimeProvider.GetUtcNow();
                    }
                    catch (Exception)
                    {
                        ReportPublicationFailure();
                    }
                }

                ObserveDiagnostic(RecordingDiagnosticEventKind.WriterAdmitted);
                writeTask = RunAsync(
                    source,
                    path);
                startCompletion.TrySetResult(writeTask);
                return true;
            }
        }

        public void CompleteStartWithoutWrite()
        {
            lock (gate)
            {
                if (writeTask is null)
                {
                    terminalCommitted = true;
                    startCompletion.TrySetResult(null);
                }
            }
        }

        public void CompleteStartFailure(Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);

            lock (gate)
            {
                if (writeTask is null)
                {
                    terminalCommitted = true;
                    startFailure = exception;
                    fault = RecordingFaultDescriptor.FromException(exception);
                    startCompletion.TrySetResult(null);
                }
            }
        }

        public void RequestStop()
        {
            lock (gate)
            {
                if (isDisposed || admission is not null)
                {
                    return;
                }

                stopRequested = true;
            }

            try
            {
                stopping.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // A concurrent completion may dispose the token source.
            }
        }

        public async ValueTask WaitForCompletionAsync(
            CancellationToken cancellationToken)
        {
            var task = await startCompletion.Task.WaitAsync(
                cancellationToken).ConfigureAwait(false);

            if (task is null)
            {
                Exception? failure;

                lock (gate)
                {
                    failure = startFailure;
                }

                if (failure is not null)
                {
                    ExceptionDispatchInfo.Capture(failure).Throw();
                }

                return;
            }

            _ = await task.WaitAsync(
                cancellationToken).ConfigureAwait(false);
        }

        public RecordingFileSinkSessionStatus GetStatus()
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(
                    isDisposed,
                    this);

                if (writeTask is null)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.Starting,
                        writeResult: null);
                }

                if (!writeTask.IsCompleted)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.Writing,
                        writeResult: null);
                }

                if (faulted)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.Faulted,
                        writeResult: null,
                        fault);
                }

                if (writeOutcome?.IsNoDecodableVideoStart == true)
                {
                    return new RecordingFileSinkSessionStatus(
                        Session,
                        RecordingFileSinkSessionStatusKind.NoDecodableVideoStart,
                        writeResult: null);
                }

                return new RecordingFileSinkSessionStatus(
                    Session,
                    RecordingFileSinkSessionStatusKind.Completed,
                    writeOutcome?.WriteResult);
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
            }

            stopping.Dispose();
        }

        public RecordingFaultDescriptor? GetFault()
        {
            lock (gate)
            {
                return fault;
            }
        }

        public bool NoDecodableVideoStart
        {
            get
            {
                lock (gate)
                {
                    return writeOutcome?.IsNoDecodableVideoStart == true;
                }
            }
        }

        private async Task<RecordingFileWriteOutcome> RunAsync(
            Stream source,
            string path)
        {
            try
            {
                await using (source.ConfigureAwait(false))
                {
                    var result = await RecordingStreamFileWriter.WriteForFileSinkAsync(
                        source,
                        path,
                        (stage, exception) =>
                        {
                            lock (gate)
                            {
                                fault = RecordingFaultDescriptor.FromException(
                                    stage,
                                    exception);
                                if (diagnosticObserver is not null && exception is RecordingStreamTransformException transformException)
                                {
                                    transformFailure = transformException.Diagnostic;
                                    transformFailed = true;
                                    ObserveDiagnostic(RecordingDiagnosticEventKind.TransformFailed);
                                }
                            }
                        },
                        stopping.Token).ConfigureAwait(false);

                    lock (gate)
                    {
                        writeOutcome = result;
                    }

                    return result;
                }
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    faulted = true;
                    fault ??= RecordingFaultDescriptor.FromException(exception);
                }

                throw;
            }
            finally
            {
                // RunAsync's await-using has disposed the source before this
                // publication. Admission races this marker under the same gate.
                lock (gate)
                {
                    terminalCommitted = true;
                }

                PublishTerminalFact();
                ObserveDiagnostic(RecordingDiagnosticEventKind.Terminal);
            }
        }

        private void ObserveDiagnostic(RecordingDiagnosticEventKind kind)
        {
            if (diagnosticObserver is null)
                return;

            // Session is the existing sink-owned identity, even if Start has
            // not returned yet. Diagnostics never participate in History.
            var diagnostic = new RecordingDiagnosticEvent(
                Session.SessionId.Value,
                kind,
                transformFailure.Phase,
                transformFailure.Site,
                transformFailure.Reason,
                fault?.Stage ?? RecordingFailureStage.Unknown,
                transformFailed ? RecordingDiagnosticExceptionType.RecordingStreamTransformException : RecordingDiagnosticExceptionType.Unknown,
                kind == RecordingDiagnosticEventKind.Terminal
                    ? faulted ? RecordingSinkSessionLifecycleKind.Faulted
                        : writeOutcome?.IsNoDecodableVideoStart == true ? RecordingSinkSessionLifecycleKind.NoDecodableVideoStart
                        : RecordingSinkSessionLifecycleKind.Completed
                    : null);
            try
            {
                _ = diagnosticObserver.TryObserve(in diagnostic);
            }
            catch (Exception)
            {
                // Optional evidence must never replace a writer exception,
                // delay cleanup, or change the terminal outcome.
            }
        }

        private void PublishTerminalFact()
        {
            if (terminalObserver is null)
            {
                return;
            }

            try
            {
                if (admissionObservedAtUtc is not { } admittedAt)
                {
                    // The observation clock failed at admission. The gap was
                    // reported then; never invent an admission timestamp.
                    return;
                }

                // The writer task does not finish (and cleanup cannot remove it)
                // until this bounded application capture has completed.
                // All facts are self-contained, even if Start has not returned.
                var kind = faulted
                    ? RecordingSinkSessionLifecycleKind.Faulted
                    : writeOutcome?.IsNoDecodableVideoStart == true
                        ? RecordingSinkSessionLifecycleKind.NoDecodableVideoStart
                        : RecordingSinkSessionLifecycleKind.Completed;
                var metadata = startRequest.Candidate.Metadata;
                terminalObserver.ObserveTerminal(new RecordingSessionTerminalFact(
                    Session,
                    metadata.Name,
                    metadata.StreamType,
                    metadata.StreamExtension,
                    admittedAt,
                    observationTimeProvider.GetUtcNow(),
                    kind,
                    writeOutcome?.WriteResult,
                    faulted ? fault : null));
            }
            catch (Exception)
            {
                ReportPublicationFailure();
            }
        }

        private void ReportPublicationFailure()
        {
            try
            {
                terminalObserver?.RecordPublicationFailure();
            }
            catch (Exception)
            {
                // Even a broken secondary failure reporter cannot replace EOF,
                // successful Stop, or the original recorder fault.
            }
        }
    }
}
