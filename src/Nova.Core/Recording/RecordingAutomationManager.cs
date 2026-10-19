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
public sealed partial class RecordingAutomationManager : IDisposable
{
    public const int DefaultMaximumChannelCount = 4096;

    private readonly object gate = new();
    private readonly IRecordingSink sink;
    private readonly int maximumChannelCount;
    private readonly Dictionary<ChannelId, CoordinatorEntry> coordinators = [];
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

    public ValueTask<RecordingAutomationExecutionResult> EvaluateAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        CancellationToken cancellationToken) =>
        EvaluateAsync(
            candidate,
            ruleSet,
            prepareStartAsync: null,
            cancellationToken);

    public async ValueTask<RecordingAutomationExecutionResult> EvaluateAsync(
        RecordingCandidate candidate,
        RecordingRuleSet ruleSet,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>?
            prepareStartAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ruleSet);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinatorEntry entry;

        lock (gate)
        {
            ThrowIfDisposed();

            if (!acceptsEvaluations)
            {
                throw new InvalidOperationException(
                    "The recording automation manager is shutting down and no longer accepts evaluations.");
            }

            entry = AcquireCoordinator(candidate.ChannelId);
        }

        try
        {
            return await entry.Coordinator.EvaluateAsync(
                candidate,
                ruleSet,
                prepareStartAsync,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseCoordinator(candidate.ChannelId, entry);
        }
    }

    public async ValueTask<RecordingAutomationExecutionResult> EvaluateDecisionAsync(
        RecordingCandidate candidate,
        RecordingRuleDecision decision,
        Func<RecordingCandidate, CancellationToken, ValueTask<IAsyncDisposable?>>? prepareStartAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(decision);
        cancellationToken.ThrowIfCancellationRequested();
        CoordinatorEntry entry;
        lock (gate)
        {
            ThrowIfDisposed();
            if (!acceptsEvaluations)
            {
                throw new InvalidOperationException("The recording automation manager is shutting down.");
            }

            entry = AcquireCoordinator(candidate.ChannelId);
        }

        try
        {
            return await entry.Coordinator.EvaluateDecisionAsync(
                candidate, decision, prepareStartAsync, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseCoordinator(candidate.ChannelId, entry);
        }
    }

    public async ValueTask<RecordingManualStartResult> StartManualAsync(
        RecordingCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinatorEntry entry;

        lock (gate)
        {
            ThrowIfDisposed();

            if (!acceptsEvaluations)
            {
                throw new InvalidOperationException(
                    "The recording automation manager is shutting down and no longer accepts starts.");
            }

            entry = AcquireCoordinator(candidate.ChannelId);
        }

        try
        {
            return await entry.Coordinator.StartManualAsync(
                candidate,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseCoordinator(candidate.ChannelId, entry);
        }
    }

    public async ValueTask<RecordingAutomationStopResult> StopAsync(
        ChannelId channelId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinatorEntry? entry;

        lock (gate)
        {
            ThrowIfDisposed();
            entry = TryAcquireCoordinator(channelId);
        }

        if (entry is null)
        {
            return new RecordingAutomationStopResult(
                channelId,
                RecordingAutomationStopResultKind.NoActiveSession,
                RecordingAutomationExecutionState.Idle);
        }

        try
        {
            return await entry.Coordinator.StopAsync(
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseCoordinator(channelId, entry);
        }
    }

    public async ValueTask<RecordingAutomationStopResult> StopManualAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinatorEntry? entry;
        lock (gate)
        {
            ThrowIfDisposed();
            entry = TryAcquireCoordinator(channelId);
        }

        if (entry is null)
        {
            return new RecordingAutomationStopResult(
                channelId,
                RecordingAutomationStopResultKind.NoActiveSession,
                RecordingAutomationExecutionState.Idle);
        }

        try
        {
            return await entry.Coordinator.StopManualAsync(
                expectedSessionId,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseCoordinator(channelId, entry);
        }
    }

    public async ValueTask<RecordingAutomationExecutionState> ReconcileStateAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        cancellationToken.ThrowIfCancellationRequested();

        CoordinatorEntry? entry;
        lock (gate)
        {
            ThrowIfDisposed();
            entry = TryAcquireCoordinator(channelId);
        }

        if (entry is null)
        {
            return RecordingAutomationExecutionState.Idle;
        }

        try
        {
            return await entry.Coordinator.ReconcileStateAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseCoordinator(channelId, entry);
        }
    }

    internal async ValueTask<IReadOnlyList<RecordingSinkSession>>
        GetActiveRecordingSessionsAsync(
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        CoordinatorEntry[] owned;
        lock (gate)
        {
            ThrowIfDisposed();

            owned = coordinators
                .OrderBy(static pair => pair.Key.Value)
                .Select(static pair => pair.Value)
                .ToArray();

            foreach (var entry in owned)
            {
                entry.ActiveOperations++;
            }
        }

        try
        {
            var activeSessions = new List<RecordingSinkSession>(owned.Length);

            foreach (var entry in owned)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var state = entry.Coordinator.HasPendingAutomaticStop
                    ? entry.Coordinator.State
                    : await entry.Coordinator.ReconcileStateAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

                if (state.Session is not { } session)
                {
                    continue;
                }

                if (!state.AutomationState.IsRecording ||
                    session.ChannelId != entry.Coordinator.ChannelId)
                {
                    throw new InvalidOperationException(
                        "A retained recording session must match its active coordinator state.");
                }

                activeSessions.Add(session);
            }

            return Array.AsReadOnly(activeSessions.ToArray());
        }
        finally
        {
            foreach (var entry in owned)
            {
                ReleaseCoordinator(
                    entry.Coordinator.ChannelId,
                    entry);
            }
        }
    }

    public bool TryGetState(
        ChannelId channelId,
        out RecordingAutomationExecutionState? state)
    {
        ArgumentNullException.ThrowIfNull(channelId);

        CoordinatorEntry? entry;

        lock (gate)
        {
            ThrowIfDisposed();
            _ = coordinators.TryGetValue(
                channelId,
                out entry);
        }

        if (entry is null)
        {
            state = null;
            return false;
        }

        state = entry.Coordinator.State;
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
                .Select(static pair => pair.Value.Coordinator)
                .ToArray();
        }

        try
        {
            await DrainAutomaticAdmissionsAsync().ConfigureAwait(false);
            foreach (var coordinator in owned)
            {
                await coordinator.DrainAutomaticStopAsync().ConfigureAwait(false);
            }

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

            if (admissionOperations != 0 ||
                coordinators.Values.Any(static entry => entry.Coordinator.HasPendingAutomaticStop))
            {
                throw new InvalidOperationException("Drain admitted recording stops before disposal.");
            }

            isDisposed = true;
            owned = coordinators.Values
                .Select(static entry => entry.Coordinator)
                .ToArray();
            coordinators.Clear();
        }

        foreach (var coordinator in owned)
        {
            coordinator.Dispose();
        }

        admissionClosing.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            isDisposed,
            this);
    }

    private CoordinatorEntry AcquireCoordinator(
        ChannelId channelId)
    {
        if (coordinators.TryGetValue(channelId, out var existing))
        {
            existing.ActiveOperations++;
            return existing;
        }

        if (coordinators.Count >= maximumChannelCount)
        {
            throw new InvalidOperationException(
                "The recording automation channel limit has been reached.");
        }

        var created = new CoordinatorEntry(
            new RecordingAutomationChannelCoordinator(
                channelId,
                sink));
        created.ActiveOperations = 1;
        coordinators.Add(channelId, created);
        return created;
    }

    private CoordinatorEntry? TryAcquireCoordinator(ChannelId channelId)
    {
        if (!coordinators.TryGetValue(channelId, out var existing))
        {
            return null;
        }

        existing.ActiveOperations++;
        return existing;
    }

    private void ReleaseCoordinator(
        ChannelId channelId,
        CoordinatorEntry entry)
    {
        var disposeCoordinator = false;

        lock (gate)
        {
            if (entry.ActiveOperations <= 0)
            {
                throw new InvalidOperationException(
                    "A recording automation coordinator operation was released more than once.");
            }

            entry.ActiveOperations--;

            // The manager lock keeps acquisition atomic with this final-use
            // check and exact-entry removal.
            if (entry.ActiveOperations == 0 &&
                !isDisposed &&
                !stopAllInProgress &&
                entry.Coordinator.IsIdleWithoutSession &&
                coordinators.TryGetValue(channelId, out var current) &&
                ReferenceEquals(current, entry))
            {
                coordinators.Remove(channelId);
                disposeCoordinator = true;
            }
        }

        if (disposeCoordinator)
        {
            entry.Coordinator.Dispose();
        }
    }

    private sealed class CoordinatorEntry
    {
        public CoordinatorEntry(
            RecordingAutomationChannelCoordinator coordinator)
        {
            Coordinator = coordinator;
        }

        public RecordingAutomationChannelCoordinator Coordinator { get; }

        public int ActiveOperations { get; set; }

        public Task? StopOwner { get; set; }
    }
}
