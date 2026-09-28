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
            RecordingAutomationManager.DefaultMaximumChannelCount)
    {
        ArgumentNullException.ThrowIfNull(sourceProvider);
        ArgumentNullException.ThrowIfNull(pathProvider);

        var sink = new RecordingFileSink(
            sourceProvider,
            pathProvider,
            maximumActiveSessionCount);
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

    public ValueTask<RecordingAutomationStopResult> StopAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        return sessions.StopAsync(channelId, cancellationToken);
    }

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAutomaticAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ruleSet);
        return sessions.EvaluateAsync(candidate, ruleSet, cancellationToken);
    }

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
