using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// GUI-independent recording boundary for manual sessions and rule decisions.
/// It owns the file sink and shares one per-channel session coordinator across
/// both start paths.
/// </summary>
public sealed class RecordingRuntime : IAsyncDisposable
{
    private readonly RecordingAutomationManager sessions;
    private readonly object lifecycleGate = new();
    private Task? disposeTask;

    public RecordingRuntime(
        IRecordingStreamSourceProvider sourceProvider,
        IRecordingFilePathProvider pathProvider,
        int maximumActiveSessionCount =
            RecordingFileSink.DefaultMaximumActiveSessionCount,
        int maximumChannelCount =
            RecordingAutomationManager.DefaultMaximumChannelCount,
        IRecordingSessionTerminalObserver? terminalObserver = null,
        TimeProvider? observationTimeProvider = null,
        IRecordingDiagnosticObserver? diagnosticObserver = null)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);
        ArgumentNullException.ThrowIfNull(pathProvider);

        var sink = new RecordingFileSink(
            sourceProvider,
            pathProvider,
            maximumActiveSessionCount,
            terminalObserver,
            observationTimeProvider,
            diagnosticObserver);
        sessions = new RecordingAutomationManager(
            sink,
            maximumChannelCount);
    }

    public ValueTask<RecordingManualStartResult> StartManualAsync(
        RecordingCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return sessions.StartManualAsync(candidate, cancellationToken);
    }

    public ValueTask<RecordingAutomaticStopAdmissionResult> RequestAutomaticStopAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken admissionToken = default) =>
        sessions.RequestAutomaticStopAsync(channelId, expectedSessionId, admissionToken);

    public bool TryGetAutomaticStopAdmission(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        out RecordingAutomaticStopAdmission? admission) =>
        sessions.TryGetAutomaticStopAdmission(channelId, expectedSessionId, out admission);

    public ValueTask<RecordingAutomationStopResult> StopAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        return sessions.StopAsync(channelId, cancellationToken);
    }

    /// <summary>
    /// Stops only the matching manual session generation. Automatic sessions
    /// and a later session for the same channel are left untouched.
    /// </summary>
    public ValueTask<RecordingAutomationStopResult> StopManualAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        return sessions.StopManualAsync(
            channelId,
            expectedSessionId,
            cancellationToken);
    }

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAutomaticAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken = default) =>
        EvaluateAutomaticAsync(
            candidate,
            ruleSet,
            prepareStartAsync: null,
            cancellationToken);

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAutomaticAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>?
            prepareStartAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ruleSet);
        return sessions.EvaluateAsync(
            candidate,
            ruleSet,
            prepareStartAsync,
            cancellationToken);
    }

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAutomaticDecisionAsync(
        RecordingCandidate candidate,
        RecordingRuleDecision decision,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>? prepareStartAsync = null,
        CancellationToken cancellationToken = default) =>
        sessions.EvaluateDecisionAsync(candidate, decision, prepareStartAsync, cancellationToken);

    public bool TryGetState(
        ChannelId channelId,
        out RecordingAutomationExecutionState? state)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        return sessions.TryGetState(channelId, out state);
    }

    public RecordingAutomationExecutionState GetState(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        return sessions.TryGetState(channelId, out var state)
            ? state!
            : RecordingAutomationExecutionState.Idle;
    }

    /// <summary>
    /// Returns a bounded read-only snapshot of active recorder-owned sessions.
    /// Existing channel coordinators are reconciled before their session is
    /// included; this call does not create coordinators or start recordings.
    /// </summary>
    public ValueTask<IReadOnlyList<RecordingSinkSession>>
        GetActiveRecordingSessionsAsync(
            CancellationToken cancellationToken = default)
    {
        return sessions.GetActiveRecordingSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// Reconciles one known channel against the sink's terminal-session
    /// monitor. Call <see cref="GetActiveRecordingSessionsAsync"/> when a
    /// caller needs the currently active sessions across retained channels.
    /// </summary>
    public ValueTask<RecordingAutomationExecutionState> ReconcileStateAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        return sessions.ReconcileStateAsync(channelId, cancellationToken);
    }

    public ValueTask<RecordingAutomationShutdownReport> ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        return sessions.ShutdownAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            disposeTask ??= DisposeCoreAsync();
            return new ValueTask(disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await sessions.ShutdownAsync(CancellationToken.None)
            .ConfigureAwait(false);
        sessions.Dispose();
    }
}
