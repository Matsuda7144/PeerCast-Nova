using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Serializes automatic recording decisions for one channel so concurrent
/// observations cannot issue duplicate recorder transitions.
/// </summary>
/// <remarks>
/// Call <see cref="StopAsync"/> before disposing when the execution state is
/// recording. <see cref="Dispose"/> releases the coordinator gate; it is not
/// an asynchronous recorder-stop operation.
/// </remarks>
public sealed partial class RecordingAutomationChannelCoordinator : IDisposable
{
    private readonly IRecordingSink sink;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object activeOperationGate = new();
    private RecordingAutomationExecutionState state;
    private CancellationTokenSource? activeOperationCancellation;
    private bool isDisposed;

    public RecordingAutomationChannelCoordinator(
        ChannelId channelId,
        IRecordingSink sink)
        : this(
            channelId,
            sink,
            RecordingAutomationExecutionState.Idle)
    {
    }

    public RecordingAutomationChannelCoordinator(
        ChannelId channelId,
        IRecordingSink sink,
        RecordingAutomationExecutionState initialState)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(initialState);

        if (initialState.Session is { } session &&
            session.ChannelId != channelId)
        {
            throw new ArgumentException(
                "The initial recorder session channel must match the coordinator channel.",
                nameof(initialState));
        }

        ChannelId = channelId;
        this.sink = sink;
        state = initialState;
    }

    public ChannelId ChannelId { get; }

    public RecordingAutomationExecutionState State =>
        Volatile.Read(ref state);

    internal bool IsIdleWithoutSession
    {
        get
        {
            var currentState = State;
            return !currentState.AutomationState.IsRecording &&
                currentState.Session is null && !HasPendingAutomaticStop;
        }
    }

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken) =>
        EvaluateAsync(
            candidate,
            ruleSet,
            prepareStartAsync: null,
            cancellationToken);

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>?
            prepareStartAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        return EvaluateCoreAsync(candidate, () => ruleSet.Evaluate(candidate), prepareStartAsync, cancellationToken);
    }

    public ValueTask<RecordingAutomationExecutionResult> EvaluateDecisionAsync(
        RecordingCandidate candidate,
        RecordingRuleDecision decision,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>? prepareStartAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return EvaluateCoreAsync(candidate, () => decision, prepareStartAsync, cancellationToken);
    }

    private async ValueTask<RecordingAutomationExecutionResult> EvaluateCoreAsync(
        RecordingCandidate candidate,
        Func<RecordingRuleDecision> evaluate,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>? prepareStartAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.ChannelId != ChannelId)
        {
            throw new ArgumentException(
                "The recording candidate channel must match the coordinator channel.",
                nameof(candidate));
        }

        await EnterMutationAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? operationCancellation = null;
        IAsyncDisposable? startPreparation = null;

        try
        {
            ThrowIfDisposed();
            operationCancellation = BeginOperation(cancellationToken);

            var currentState = await ReconcileTerminalSessionAsync(
                state,
                operationCancellation.Token).ConfigureAwait(false);

            Volatile.Write(
                ref state,
                currentState);

            var plan = RecordingAutomationPlanner.Plan(
                currentState.AutomationState,
                evaluate());

            RecordingAutomationExecutionResult result;

            try
            {
                if (plan.Action == RecordingAutomationAction.Start &&
                    prepareStartAsync is not null)
                {
                    startPreparation = await prepareStartAsync(
                        candidate,
                        operationCancellation.Token).ConfigureAwait(false);
                    if (startPreparation is null)
                    {
                        result = new RecordingAutomationExecutionResult(
                            RecordingAutomationExecutionResultKind.StartRejected,
                            currentState,
                            plan);
                    }
                    else
                    {
                        result = await RecordingAutomationExecutor.ExecuteAsync(
                            currentState,
                            plan,
                            candidate,
                            sink,
                            operationCancellation.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    result = await RecordingAutomationExecutor.ExecuteAsync(
                        currentState,
                        plan,
                        candidate,
                        sink,
                        operationCancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
                when (plan.Action == RecordingAutomationAction.Start &&
                      !cancellationToken.IsCancellationRequested &&
                      operationCancellation.IsCancellationRequested)
            {
                result = new RecordingAutomationExecutionResult(
                    RecordingAutomationExecutionResultKind.StartRejected,
                    currentState,
                    plan);
            }

            Volatile.Write(
                ref state,
                result.State);

            return result;
        }
        finally
        {
            try
            {
                if (startPreparation is not null)
                {
                    await startPreparation.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                EndOperation(operationCancellation);
                gate.Release();
            }
        }
    }

    public async ValueTask<RecordingManualStartResult> StartManualAsync(
        RecordingCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.ChannelId != ChannelId)
        {
            throw new ArgumentException(
                "The recording candidate channel must match the coordinator channel.",
                nameof(candidate));
        }

        await EnterMutationAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? operationCancellation = null;

        try
        {
            ThrowIfDisposed();
            operationCancellation = BeginOperation(cancellationToken);

            var currentState = await ReconcileTerminalSessionAsync(
                state,
                operationCancellation.Token).ConfigureAwait(false);

            Volatile.Write(ref state, currentState);

            if (currentState.Session is not null)
            {
                if (!currentState.AutomationState.IsRecording)
                {
                    throw new InvalidOperationException(
                        "A recorder session cannot be active while execution state is idle.");
                }

                return new RecordingManualStartResult(
                    ChannelId,
                    RecordingManualStartResultKind.AlreadyRunning,
                    currentState);
            }

            if (currentState.AutomationState.IsRecording)
            {
                throw new InvalidOperationException(
                    "A recording execution state cannot be active without a recorder session.");
            }

            RecordingSinkStartResult sinkResult;

            try
            {
                sinkResult = await sink.StartAsync(
                    RecordingSinkStartRequest.Manual(candidate),
                    operationCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested &&
                      operationCancellation.IsCancellationRequested)
            {
                return new RecordingManualStartResult(
                    ChannelId,
                    RecordingManualStartResultKind.Canceled,
                    currentState);
            }

            ArgumentNullException.ThrowIfNull(sinkResult);

            if (sinkResult.Kind == RecordingSinkStartResultKind.Rejected)
            {
                return new RecordingManualStartResult(
                    ChannelId,
                    RecordingManualStartResultKind.Rejected,
                    currentState);
            }

            var session = sinkResult.Session ??
                throw new InvalidOperationException(
                    "A successful recording sink start must return a session.");

            if (session.ChannelId != candidate.ChannelId ||
                session.Origin != RecordingSessionOrigin.Manual)
            {
                throw new InvalidOperationException(
                    "A manual recording start must return a matching manual session.");
            }

            var nextState =
                RecordingAutomationExecutionState.ManualRecording(session);

            Volatile.Write(ref state, nextState);

            return new RecordingManualStartResult(
                ChannelId,
                sinkResult.Kind == RecordingSinkStartResultKind.Started
                    ? RecordingManualStartResultKind.Started
                    : RecordingManualStartResultKind.AlreadyRunning,
                nextState);
        }
        finally
        {
            EndOperation(operationCancellation);
            gate.Release();
        }
    }

    public async ValueTask<RecordingAutomationStopResult> StopAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelActiveOperation();
        await DrainAutomaticStopAsync().ConfigureAwait(false);
        await EnterMutationAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            return await StopCurrentSessionAsync(state, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Refreshes this channel's cached execution state against the sink's
    /// terminal-session monitor without starting or stopping an active session.
    /// A terminal sink session is reconciled using the same cleanup path as a
    /// later recording evaluation.
    /// </summary>
    public async ValueTask<RecordingAutomationExecutionState> ReconcileStateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnterMutationAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            var reconciled = await ReconcileTerminalSessionAsync(
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
            Volatile.Write(ref state, reconciled);
            return reconciled;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Stops only the expected manual session generation. The expected
    /// identity and origin are checked under the same per-channel gate as the
    /// recorder transition, so an automatic or replacement session cannot be
    /// stopped by a stale manual-recording command.
    /// </summary>
    public async ValueTask<RecordingAutomationStopResult> StopManualAsync(
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        cancellationToken.ThrowIfCancellationRequested();
        await EnterMutationAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();
            var currentState = state;
            var currentSession = currentState.Session;
            if (currentSession is null)
            {
                if (currentState.AutomationState.IsRecording)
                {
                    throw new InvalidOperationException(
                        "A recording automation state cannot be active without a recorder session.");
                }

                return new RecordingAutomationStopResult(
                    ChannelId,
                    RecordingAutomationStopResultKind.NoActiveSession,
                    currentState);
            }

            if (currentSession.Origin != RecordingSessionOrigin.Manual ||
                currentSession.SessionId != expectedSessionId)
            {
                return new RecordingAutomationStopResult(
                    ChannelId,
                    RecordingAutomationStopResultKind.Rejected,
                    currentState);
            }

            return await StopCurrentSessionAsync(currentState, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async ValueTask<RecordingAutomationStopResult> StopCurrentSessionAsync(
        RecordingAutomationExecutionState currentState,
        CancellationToken cancellationToken)
    {
        if (currentState.Session is null)
        {
            if (currentState.AutomationState.IsRecording)
            {
                throw new InvalidOperationException(
                    "A recording automation state cannot be active without a recorder session.");
            }

            return new RecordingAutomationStopResult(
                ChannelId,
                RecordingAutomationStopResultKind.NoActiveSession,
                currentState);
        }

        if (!currentState.AutomationState.IsRecording)
        {
            throw new InvalidOperationException(
                "A recorder session cannot be active while automation state is idle.");
        }

        var sinkResult = await sink.StopAsync(
            new RecordingSinkStopRequest(currentState.Session),
            cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(sinkResult);

        var resultKind = sinkResult.Kind switch
        {
            RecordingSinkStopResultKind.Stopped =>
                RecordingAutomationStopResultKind.Stopped,
            RecordingSinkStopResultKind.NoDecodableVideoStart =>
                RecordingAutomationStopResultKind.NoDecodableVideoStart,
            RecordingSinkStopResultKind.AlreadyStopped =>
                RecordingAutomationStopResultKind.AlreadyStopped,
            RecordingSinkStopResultKind.Rejected =>
                RecordingAutomationStopResultKind.Rejected,
            RecordingSinkStopResultKind.Faulted =>
                RecordingAutomationStopResultKind.Faulted,
            _ => throw new InvalidOperationException(
                $"Unsupported recording sink stop result: {sinkResult.Kind}."),
        };

        var nextState = sinkResult.Kind == RecordingSinkStopResultKind.Rejected
            ? currentState
            : RecordingAutomationExecutionState.Idle;
        Volatile.Write(ref state, nextState);
        return new RecordingAutomationStopResult(
            ChannelId,
            resultKind,
            nextState,
            sinkResult.Fault);
    }

    public void Dispose()
    {
        gate.Wait();

        try
        {
            if (HasPendingAutomaticStop)
            {
                throw new InvalidOperationException("Drain the admitted stop before coordinator disposal.");
            }

            isDisposed = true;
        }
        finally
        {
            gate.Release();
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask<RecordingAutomationExecutionState>
        ReconcileTerminalSessionAsync(
            RecordingAutomationExecutionState currentState,
            CancellationToken cancellationToken)
    {
        if (currentState.Session is not { } session ||
            sink is not IRecordingSinkSessionMonitor monitor ||
            !monitor.TryGetSessionStatus(
                session,
                out var status) ||
            status is null ||
            status.Kind == RecordingSinkSessionLifecycleKind.Active)
        {
            return currentState;
        }

        var stopResult = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(stopResult);

        return stopResult.Kind switch
        {
            RecordingSinkStopResultKind.Stopped =>
                RecordingAutomationExecutionState.Idle,

            RecordingSinkStopResultKind.NoDecodableVideoStart =>
                RecordingAutomationExecutionState.Idle,

            RecordingSinkStopResultKind.AlreadyStopped =>
                RecordingAutomationExecutionState.Idle,

            RecordingSinkStopResultKind.Faulted =>
                RecordingAutomationExecutionState.Idle,

            RecordingSinkStopResultKind.Rejected =>
                currentState,

            _ => throw new InvalidOperationException(
                $"Unsupported recording sink stop result: {stopResult.Kind}."),
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            isDisposed,
            this);
    }

    private CancellationTokenSource BeginOperation(
        CancellationToken cancellationToken)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        lock (activeOperationGate)
        {
            if (activeOperationCancellation is not null)
            {
                operation.Dispose();
                throw new InvalidOperationException(
                    "A channel recording operation is already active.");
            }

            activeOperationCancellation = operation;
        }

        return operation;
    }

    private void EndOperation(CancellationTokenSource? operation)
    {
        if (operation is null)
        {
            return;
        }

        lock (activeOperationGate)
        {
            if (ReferenceEquals(activeOperationCancellation, operation))
            {
                activeOperationCancellation = null;
            }
        }

        operation.Dispose();
    }

    private void CancelActiveOperation()
    {
        lock (activeOperationGate)
        {
            activeOperationCancellation?.Cancel();
        }
    }
}
