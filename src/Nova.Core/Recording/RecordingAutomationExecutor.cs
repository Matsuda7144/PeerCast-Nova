namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Applies a pure automation plan to an abstract recording sink.
/// State advances only when the sink confirms the requested transition.
/// </summary>
public static class RecordingAutomationExecutor
{
    public static async ValueTask<RecordingAutomationExecutionResult> ExecuteAsync(
        RecordingAutomationExecutionState currentState,
        RecordingAutomationPlan plan,
        RecordingCandidate candidate,
        IRecordingSink sink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(currentState);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(sink);

        cancellationToken.ThrowIfCancellationRequested();

        return plan.Action switch
        {
            RecordingAutomationAction.NoChange =>
                ApplyNoChange(
                    currentState,
                    plan,
                    candidate),

            RecordingAutomationAction.Start =>
                await StartAsync(
                    currentState,
                    plan,
                    candidate,
                    sink,
                    cancellationToken).ConfigureAwait(false),

            RecordingAutomationAction.Stop =>
                await StopAsync(
                    currentState,
                    plan,
                    candidate,
                    sink,
                    cancellationToken).ConfigureAwait(false),

            _ => throw new InvalidOperationException(
                $"Unsupported recording automation action: {plan.Action}."),
        };
    }

    private static RecordingAutomationExecutionResult ApplyNoChange(
        RecordingAutomationExecutionState currentState,
        RecordingAutomationPlan plan,
        RecordingCandidate candidate)
    {
        if (!plan.NextState.IsRecording)
        {
            if (currentState.AutomationState.IsRecording ||
                currentState.Session is not null)
            {
                throw new InvalidOperationException(
                    "An idle NoChange plan cannot retain an active recorder session.");
            }

            return new RecordingAutomationExecutionResult(
                RecordingAutomationExecutionResultKind.NoChange,
                currentState,
                plan);
        }

        var session = RequireActiveSession(
            currentState,
            candidate);

        if (plan.NextState.ActiveRuleId is null)
        {
            if (session.Origin != RecordingSessionOrigin.Manual)
            {
                throw new InvalidOperationException(
                    "An active execution without a rule must use a manual recording session.");
            }

            return new RecordingAutomationExecutionResult(
                RecordingAutomationExecutionResultKind.NoChange,
                RecordingAutomationExecutionState.ManualRecording(session),
                plan);
        }

        var updatedState =
            RecordingAutomationExecutionState.Recording(
                plan.NextState,
                session);

        return new RecordingAutomationExecutionResult(
            RecordingAutomationExecutionResultKind.NoChange,
            updatedState,
            plan);
    }

    private static async ValueTask<RecordingAutomationExecutionResult> StartAsync(
        RecordingAutomationExecutionState currentState,
        RecordingAutomationPlan plan,
        RecordingCandidate candidate,
        IRecordingSink sink,
        CancellationToken cancellationToken)
    {
        if (currentState.AutomationState.IsRecording ||
            currentState.Session is not null)
        {
            throw new InvalidOperationException(
                "A Start plan requires an idle execution state.");
        }

        if (!plan.NextState.IsRecording ||
            plan.NextState.ActiveRuleId is not { } selectedRuleId)
        {
            throw new InvalidOperationException(
                "A Start plan must identify the selected recording rule.");
        }

        var sinkResult = await sink.StartAsync(
            new RecordingSinkStartRequest(
                candidate,
                selectedRuleId),
            cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(sinkResult);

        if (sinkResult.Kind == RecordingSinkStartResultKind.Rejected)
        {
            return new RecordingAutomationExecutionResult(
                RecordingAutomationExecutionResultKind.StartRejected,
                currentState,
                plan);
        }

        var session = sinkResult.Session ??
            throw new InvalidOperationException(
                "A successful recording sink start must return a session.");

        EnsureSessionMatchesCandidate(
            session,
            candidate);

        var nextState =
            RecordingAutomationExecutionState.Recording(
                plan.NextState,
                session);

        var resultKind = sinkResult.Kind switch
        {
            RecordingSinkStartResultKind.Started =>
                RecordingAutomationExecutionResultKind.Started,

            RecordingSinkStartResultKind.AlreadyRunning =>
                RecordingAutomationExecutionResultKind.StartAlreadyRunning,

            _ => throw new InvalidOperationException(
                $"Unsupported recording sink start result: {sinkResult.Kind}."),
        };

        return new RecordingAutomationExecutionResult(
            resultKind,
            nextState,
            plan);
    }

    private static async ValueTask<RecordingAutomationExecutionResult> StopAsync(
        RecordingAutomationExecutionState currentState,
        RecordingAutomationPlan plan,
        RecordingCandidate candidate,
        IRecordingSink sink,
        CancellationToken cancellationToken)
    {
        if (plan.NextState.IsRecording)
        {
            throw new InvalidOperationException(
                "A Stop plan must transition to idle.");
        }

        var session = RequireActiveSession(
            currentState,
            candidate);

        var sinkResult = await sink.StopAsync(
            new RecordingSinkStopRequest(session),
            cancellationToken).ConfigureAwait(false);

        ArgumentNullException.ThrowIfNull(sinkResult);

        var resultKind = sinkResult.Kind switch
        {
            RecordingSinkStopResultKind.Stopped =>
                RecordingAutomationExecutionResultKind.Stopped,

            RecordingSinkStopResultKind.NoDecodableVideoStart =>
                RecordingAutomationExecutionResultKind.StopNoDecodableVideoStart,

            RecordingSinkStopResultKind.AlreadyStopped =>
                RecordingAutomationExecutionResultKind.StopAlreadyStopped,

            RecordingSinkStopResultKind.Rejected =>
                RecordingAutomationExecutionResultKind.StopRejected,

            RecordingSinkStopResultKind.Faulted =>
                RecordingAutomationExecutionResultKind.StopFaulted,

            _ => throw new InvalidOperationException(
                $"Unsupported recording sink stop result: {sinkResult.Kind}."),
        };

        var nextState = sinkResult.Kind ==
            RecordingSinkStopResultKind.Rejected
            ? currentState
            : RecordingAutomationExecutionState.Idle;

        return new RecordingAutomationExecutionResult(
            resultKind,
            nextState,
            plan);
    }

    private static RecordingSinkSession RequireActiveSession(
        RecordingAutomationExecutionState currentState,
        RecordingCandidate candidate)
    {
        if (!currentState.AutomationState.IsRecording ||
            currentState.Session is not { } session)
        {
            throw new InvalidOperationException(
                "An active automation transition requires an active recorder session.");
        }

        EnsureSessionMatchesCandidate(
            session,
            candidate);

        return session;
    }

    private static void EnsureSessionMatchesCandidate(
        RecordingSinkSession session,
        RecordingCandidate candidate)
    {
        if (session.ChannelId != candidate.ChannelId)
        {
            throw new InvalidOperationException(
                "The recorder session channel does not match the automation candidate.");
        }
    }
}
