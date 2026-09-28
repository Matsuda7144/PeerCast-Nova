using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Owns one serialized automation coordinator per channel while allowing
/// different channels to progress independently.
/// </summary>
/// <remarks>
/// Call <see cref="ShutdownAsync"/> before <see cref="Dispose"/> so active
/// recorder sessions receive their asynchronous stop transition. Dispose
/// releases coordinator ownership after that shutdown boundary.
/// </remarks>
public sealed class RecordingAutomationManager : IDisposable
{
    public const int DefaultMaximumChannelCount = 4096;

    private readonly object gate = new();
    private readonly IRecordingSink sink;
    private readonly int maximumChannelCount;
    private readonly Dictionary<ChannelId, RecordingAutomationChannelCoordinator>
        coordinators = [];
    private bool acceptsEvaluations = true;
    private bool stopAllInProgress;
    private bool isDisposed;

    public RecordingAutomationManager(
        IRecordingSink sink,
        int maximumChannelCount = DefaultMaximumChannelCount)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumChannelCount);

        this.sink = sink;
        this.maximumChannelCount = maximumChannelCount;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                ThrowIfDisposed();
                return coordinators.Count;
            }
        }
    }

    public bool AcceptingEvaluations
    {
        get
        {
            lock (gate)
            {
                ThrowIfDisposed();
                return acceptsEvaluations;
            }
        }
    }

    public async ValueTask<RecordingAutomationExecutionResult> EvaluateAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ruleSet);
        cancellationToken.ThrowIfCancellationRequested();

        RecordingAutomationChannelCoordinator coordinator;

        lock (gate)
        {
            ThrowIfDisposed();

            if (!acceptsEvaluations)
            {
                throw new InvalidOperationException(
                    "The recording automation manager is shutting down and no longer accepts evaluations.");
            }

            coordinator = GetOrCreateCoordinator(candidate.ChannelId);
        }

        return await coordinator.EvaluateAsync(
            candidate,
            ruleSet,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RecordingManualStartResult> StartManualAsync(
        RecordingCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        RecordingAutomationChannelCoordinator coordinator;

        lock (gate)
        {
            ThrowIfDisposed();

            if (!acceptsEvaluations)
            {
                throw new InvalidOperationException(
                    "The recording automation manager is shutting down and no longer accepts starts.");
            }

            coordinator = GetOrCreateCoordinator(candidate.ChannelId);
        }

        return await coordinator.StartManualAsync(
            candidate,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<RecordingAutomationStopResult> StopAsync(
        ChannelId channelId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        cancellationToken.ThrowIfCancellationRequested();

        RecordingAutomationChannelCoordinator? coordinator;

        lock (gate)
        {
            ThrowIfDisposed();
            _ = coordinators.TryGetValue(channelId, out coordinator);
        }

        if (coordinator is null)
        {
            return new RecordingAutomationStopResult(
                channelId,
                RecordingAutomationStopResultKind.NoActiveSession,
                RecordingAutomationExecutionState.Idle);
        }

        return await coordinator.StopAsync(
            cancellationToken).ConfigureAwait(false);
    }

    public bool TryGetState(
        ChannelId channelId,
        out RecordingAutomationExecutionState? state)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        RecordingAutomationChannelCoordinator? coordinator;

        lock (gate)
        {
            ThrowIfDisposed();
            _ = coordinators.TryGetValue(
                channelId,
                out coordinator);
        }

        if (coordinator is null)
        {
            state = null;
            return false;
        }

        state = coordinator.State;
        return true;
    }

    public async ValueTask<RecordingAutomationShutdownReport> ShutdownAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RecordingAutomationChannelCoordinator[] owned;

        lock (gate)
        {
            ThrowIfDisposed();

            if (stopAllInProgress)
            {
                throw new InvalidOperationException(
                    "A recording automation shutdown pass is already in progress.");
            }

            acceptsEvaluations = false;
            stopAllInProgress = true;

            owned = coordinators
                .OrderBy(static pair => pair.Key.Value)
                .Select(static pair => pair.Value)
                .ToArray();
        }

        try
        {
            var results = new List<RecordingAutomationStopResult>(
                owned.Length);

            foreach (var coordinator in owned)
            {
                cancellationToken.ThrowIfCancellationRequested();

                results.Add(
                    await coordinator.StopAsync(
                        cancellationToken).ConfigureAwait(false));
            }

            return new RecordingAutomationShutdownReport(
                results);
        }
        finally
        {
            lock (gate)
            {
                stopAllInProgress = false;
            }
        }
    }

    public void Dispose()
    {
        RecordingAutomationChannelCoordinator[] owned;

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            if (stopAllInProgress)
            {
                throw new InvalidOperationException(
                    "Cannot dispose the recording automation manager while a shutdown pass is in progress.");
            }

            isDisposed = true;
            owned = [.. coordinators.Values];
            coordinators.Clear();
        }

        foreach (var coordinator in owned)
        {
            coordinator.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            isDisposed,
            this);
    }

    private RecordingAutomationChannelCoordinator GetOrCreateCoordinator(
        ChannelId channelId)
    {
        if (coordinators.TryGetValue(channelId, out var existing))
        {
            return existing;
        }

        if (coordinators.Count >= maximumChannelCount)
        {
            throw new InvalidOperationException(
                "The recording automation channel limit has been reached.");
        }

        var created = new RecordingAutomationChannelCoordinator(
            channelId,
            sink);
        coordinators.Add(channelId, created);
        return created;
    }
}
