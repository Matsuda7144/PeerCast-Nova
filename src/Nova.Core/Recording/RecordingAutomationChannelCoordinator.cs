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
public sealed class RecordingAutomationChannelCoordinator : IDisposable
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

    public async ValueTask<RecordingAutomationExecutionResult> EvaluateAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ruleSet);

        if (candidate.ChannelId != ChannelId)
        {
            throw new ArgumentException(
                "The recording candidate channel must match the coordinator channel.",
                nameof(candidate));
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        CancellationTokenSource? operationCancellation = null;

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

            var plan = RecordingAutomationPlanner.Evaluate(
                currentState.AutomationState,
                candidate,
                ruleSet);

            RecordingAutomationExecutionResult result;

            try
            {
                result = await RecordingAutomationExecutor.ExecuteAsync(
                    currentState,
                    plan,
                    candidate,
                    sink,
                    operationCancellation.Token).ConfigureAwait(false);
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
            EndOperation(operationCancellation);
            gate.Release();
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

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            ThrowIfDisposed();

            var currentState = state;

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
                new RecordingSinkStopRequest(
                    currentState.Session),
                cancellationToken).ConfigureAwait(false);

            ArgumentNullException.ThrowIfNull(sinkResult);

            var resultKind = sinkResult.Kind switch
            {
                RecordingSinkStopResultKind.Stopped =>
                    RecordingAutomationStopResultKind.Stopped,

                RecordingSinkStopResultKind.AlreadyStopped =>
                    RecordingAutomationStopResultKind.AlreadyStopped,

                RecordingSinkStopResultKind.Rejected =>
                    RecordingAutomationStopResultKind.Rejected,

                RecordingSinkStopResultKind.Faulted =>
                    RecordingAutomationStopResultKind.Faulted,

                _ => throw new InvalidOperationException(
                    $"Unsupported recording sink stop result: {sinkResult.Kind}."),
            };

            var nextState = sinkResult.Kind ==
                RecordingSinkStopResultKind.Rejected
                ? currentState
                : RecordingAutomationExecutionState.Idle;

            Volatile.Write(
                ref state,
                nextState);

            return new RecordingAutomationStopResult(
                ChannelId,
                resultKind,
                nextState,
                sinkResult.Fault);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Wait();

        try
        {
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
