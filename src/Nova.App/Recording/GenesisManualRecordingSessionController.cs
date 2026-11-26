using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App;

internal enum GenesisRecordingCoreStateKind
{
    Unavailable,
    Idle,
    Manual,
    Automatic,
}

internal sealed record GenesisRecordingCoreSnapshot(
    bool IsCandidateValid,
    GenesisRecordingCoreStateKind State,
    RecordingSessionId? SessionId = null)
{
    public static GenesisRecordingCoreSnapshot Unavailable { get; } =
        new(false, GenesisRecordingCoreStateKind.Unavailable);
}

internal sealed record GenesisManualRecordingSessionIdentity(
    ChannelId ChannelId,
    RecordingSessionId SessionId,
    string ChannelName);

internal sealed record GenesisManualRecordingSessionSnapshot(
    bool IsAvailable,
    IReadOnlyList<GenesisManualRecordingSessionIdentity> Sessions)
{
    public static GenesisManualRecordingSessionSnapshot Unavailable { get; } =
        new(false, Array.AsReadOnly(Array.Empty<GenesisManualRecordingSessionIdentity>()));
}

internal sealed record GenesisActiveRecordingSession(
    ChannelId ChannelId,
    RecordingSessionId SessionId,
    RecordingSessionOrigin Origin,
    Guid? AutomaticRuleId,
    string? RetainedChannelName);

internal sealed record GenesisActiveRecordingSessionSnapshot(
    bool IsAvailable,
    IReadOnlyList<GenesisActiveRecordingSession> Sessions)
{
    public static GenesisActiveRecordingSessionSnapshot Unavailable { get; } =
        new(false, Array.AsReadOnly(Array.Empty<GenesisActiveRecordingSession>()));
}

internal static class GenesisRecordingDisplayName
{
    public static string Fallback(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        var id = channelId.Value.ToString("N").ToUpperInvariant();
        return $"Channel {id[..8]}";
    }
}

internal enum GenesisManualRecordingCommandKind
{
    Started,
    AlreadyRunning,
    Rejected,
    Cancelled,
    Failed,
    Stopped,
    NoDecodableVideoStart,
    NoActiveSession,
    AlreadyStopped,
    Faulted,
}

internal sealed record GenesisManualRecordingCommandOutcome(
    GenesisManualRecordingCommandKind Kind,
    GenesisRecordingCoreSnapshot Snapshot);

internal interface IGenesisSharedPcpRecordingOwnerProvider
{
    Task<GenesisSharedPcpRecordingOwnerAttachment> AttachRecordingOwnerAsync(
        DailyPcpPlaybackSource source,
        CancellationToken cancellationToken);
}

internal interface IGenesisManualRecordingSessionController
{
    GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel);

    GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId);

    bool IsKnownManualSession(ChannelId channelId, RecordingSessionId sessionId);

    ValueTask<GenesisActiveRecordingSessionSnapshot> GetActiveRecordingSessionsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<GenesisManualRecordingSessionSnapshot> GetActiveManualRecordingsAsync(
        CancellationToken cancellationToken = default);

    ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default);

    ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default);

    ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// GUI application boundary for manual recording. It reuses the shared PCP
/// owner and the Recording Runtime supplied by the existing production
/// composition; it owns neither transport nor file writing.
/// </summary>
internal sealed class GenesisManualRecordingSessionController :
    IGenesisManualRecordingSessionController,
    IAsyncDisposable
{
    private readonly object _lifecycleGate = new();
    private readonly IGenesisSharedPcpRecordingOwnerProvider _ownerProvider;
    private readonly RecordingRuntime _recording;
    private readonly Dictionary<ChannelId, SemaphoreSlim> _channelLanes = [];
    private readonly Dictionary<ChannelId, GenesisManualRecordingSessionIdentity>
        _manualSessions = [];
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private bool _accepting = true;
    private int _activeOperations;
    private TaskCompletionSource? _operationsDrained;
    private Task? _shutdownTask;

    public GenesisManualRecordingSessionController(
        IGenesisSharedPcpRecordingOwnerProvider ownerProvider,
        RecordingRuntime recording)
    {
        ArgumentNullException.ThrowIfNull(ownerProvider);
        ArgumentNullException.ThrowIfNull(recording);
        _ownerProvider = ownerProvider;
        _recording = recording;
    }

    public GenesisRecordingCoreSnapshot GetSnapshot(GenesisChannelItem? channel)
    {
        var candidateValid = GenesisRecordingCandidateAdapter.TryCreate(
            channel,
            out _);
        if (!GenesisRecordingCandidateAdapter.TryGetChannelId(
                channel,
                out var channelId) ||
            channelId is null)
        {
            return GenesisRecordingCoreSnapshot.Unavailable;
        }

        try
        {
            return ReadSnapshot(channel!, channelId) with
            {
                IsCandidateValid = candidateValid,
            };
        }
        catch
        {
            return GenesisRecordingCoreSnapshot.Unavailable;
        }
    }

    public GenesisRecordingCoreSnapshot GetSnapshot(ChannelId channelId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        try
        {
            return ReadSnapshot(channelId);
        }
        catch
        {
            return GenesisRecordingCoreSnapshot.Unavailable;
        }
    }

    public bool IsKnownManualSession(
        ChannelId channelId,
        RecordingSessionId sessionId)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        lock (_lifecycleGate)
        {
            return _manualSessions.TryGetValue(channelId, out var current) &&
                current.SessionId == sessionId;
        }
    }

    public async ValueTask<GenesisActiveRecordingSessionSnapshot>
        GetActiveRecordingSessionsAsync(
            CancellationToken cancellationToken = default)
    {
        var operation = TryBeginOperation(cancellationToken);
        if (operation is null)
        {
            return GenesisActiveRecordingSessionSnapshot.Unavailable;
        }

        using (operation)
        {
            try
            {
                GenesisManualRecordingSessionIdentity[] cleanupCandidates;
                lock (_lifecycleGate)
                {
                    cleanupCandidates = [.. _manualSessions.Values];
                }

                var coreSessions = await _recording
                    .GetActiveRecordingSessionsAsync(operation.Token)
                    .ConfigureAwait(false);
                operation.Token.ThrowIfCancellationRequested();

                var activeManualIdentities = coreSessions
                    .Where(static session =>
                        session.Origin == RecordingSessionOrigin.Manual)
                    .Select(static session => (session.ChannelId, session.SessionId))
                    .ToHashSet();

                Dictionary<ChannelId, GenesisManualRecordingSessionIdentity> knownSessions;
                lock (_lifecycleGate)
                {
                    CleanupStaleManualSessionIdentities(
                        _manualSessions,
                        cleanupCandidates,
                        activeManualIdentities);

                    knownSessions = new Dictionary<ChannelId,
                        GenesisManualRecordingSessionIdentity>(_manualSessions);
                }

                var projected = coreSessions
                    .Select(session => ProjectActiveRecordingSession(
                        session,
                        knownSessions))
                    .OrderBy(static session => session.ChannelId.Value)
                    .ThenBy(static session => session.SessionId.Value)
                    .ThenBy(static session => session.Origin)
                    .ToArray();

                return new(true, Array.AsReadOnly(projected));
            }
            catch
            {
                return GenesisActiveRecordingSessionSnapshot.Unavailable;
            }
        }
    }

    internal static void CleanupStaleManualSessionIdentities(
        IDictionary<ChannelId, GenesisManualRecordingSessionIdentity> currentRegistry,
        IReadOnlyCollection<GenesisManualRecordingSessionIdentity> cleanupCandidates,
        IReadOnlySet<(ChannelId ChannelId, RecordingSessionId SessionId)> activeManualIdentities)
    {
        ArgumentNullException.ThrowIfNull(currentRegistry);
        ArgumentNullException.ThrowIfNull(cleanupCandidates);
        ArgumentNullException.ThrowIfNull(activeManualIdentities);

        foreach (var candidate in cleanupCandidates)
        {
            if (!activeManualIdentities.Contains((candidate.ChannelId, candidate.SessionId)))
            {
                _ = TryRemoveManualIdentityIfCurrent(currentRegistry, candidate);
            }
        }
    }

    internal static GenesisActiveRecordingSession ProjectActiveRecordingSession(
        RecordingSinkSession session,
        IReadOnlyDictionary<ChannelId, GenesisManualRecordingSessionIdentity>
            currentManualSessions)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(currentManualSessions);

        var currentChannelName = session.Origin == RecordingSessionOrigin.Manual &&
                currentManualSessions.TryGetValue(session.ChannelId, out var current) &&
                current.SessionId == session.SessionId
            ? current.ChannelName
            : null;

        return new GenesisActiveRecordingSession(
            session.ChannelId,
            session.SessionId,
            session.Origin,
            session.AutomaticRuleId,
            currentChannelName);
    }

    internal static bool TryRemoveManualIdentityIfCurrent(
        IDictionary<ChannelId, GenesisManualRecordingSessionIdentity> currentRegistry,
        GenesisManualRecordingSessionIdentity expected)
    {
        ArgumentNullException.ThrowIfNull(currentRegistry);
        ArgumentNullException.ThrowIfNull(expected);

        if (!currentRegistry.TryGetValue(expected.ChannelId, out var current) ||
            current.SessionId != expected.SessionId)
        {
            return false;
        }

        return currentRegistry.Remove(expected.ChannelId);
    }

    public async ValueTask<GenesisManualRecordingSessionSnapshot>
        GetActiveManualRecordingsAsync(
            CancellationToken cancellationToken = default)
    {
        var snapshot = await GetActiveRecordingSessionsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!snapshot.IsAvailable)
        {
            return GenesisManualRecordingSessionSnapshot.Unavailable;
        }

        var manual = snapshot.Sessions
            .Where(static session => session.Origin == RecordingSessionOrigin.Manual)
            .Select(static session => new GenesisManualRecordingSessionIdentity(
                session.ChannelId,
                session.SessionId,
                session.RetainedChannelName ??
                    GenesisRecordingDisplayName.Fallback(session.ChannelId)))
            .ToArray();
        return new(true, Array.AsReadOnly(manual));
    }

    public async ValueTask<GenesisManualRecordingCommandOutcome> StartAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!GenesisRecordingCandidateAdapter.TryCreate(channel, out var adapted) ||
            adapted is null)
        {
            return Outcome(
                GenesisManualRecordingCommandKind.Rejected,
                GenesisRecordingCoreSnapshot.Unavailable);
        }

        var channelId = adapted.Candidate.ChannelId;
        var operation = TryBeginOperation(cancellationToken);
        if (operation is null)
        {
            return Outcome(
                GenesisManualRecordingCommandKind.Cancelled,
                GetSnapshot(channel));
        }

        using (operation)
        {
            var lane = GetChannelLane(channelId);
            try
            {
                await lane.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    var current = ReadState(channelId);
                    if (current == GenesisRecordingCoreStateKind.Manual)
                    {
                        return Outcome(
                            GenesisManualRecordingCommandKind.AlreadyRunning,
                            new(true, current));
                    }

                    if (current != GenesisRecordingCoreStateKind.Idle)
                    {
                        return Outcome(
                            GenesisManualRecordingCommandKind.Rejected,
                            new(true, current));
                    }

                    await using var attachment = await _ownerProvider
                        .AttachRecordingOwnerAsync(
                            adapted.Source,
                            operation.Token)
                        .ConfigureAwait(false);
                    if (!ReferenceEquals(attachment.Recording, _recording))
                    {
                        throw new InvalidOperationException(
                            "The shared PCP owner returned a different Recording Runtime.");
                    }

                    var result = await attachment.Recording
                        .StartManualAsync(adapted.Candidate, operation.Token)
                        .ConfigureAwait(false);
                    if (result.Kind == RecordingManualStartResultKind.Started &&
                        result.State.Session is { } session &&
                        session.Origin == RecordingSessionOrigin.Manual &&
                        session.ChannelId == channelId)
                    {
                        lock (_lifecycleGate)
                        {
                            _manualSessions[channelId] = new(
                                channelId,
                                session.SessionId,
                                channel.Name);
                        }
                    }

                    return Outcome(
                        result.Kind switch
                        {
                            RecordingManualStartResultKind.Started =>
                                GenesisManualRecordingCommandKind.Started,
                            RecordingManualStartResultKind.AlreadyRunning =>
                                GenesisManualRecordingCommandKind.AlreadyRunning,
                            RecordingManualStartResultKind.Rejected =>
                                GenesisManualRecordingCommandKind.Rejected,
                            RecordingManualStartResultKind.Canceled =>
                                GenesisManualRecordingCommandKind.Cancelled,
                            _ => GenesisManualRecordingCommandKind.Failed,
                        },
                        ReadSnapshot(channel, channelId));
                }
                finally
                {
                    lane.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return Outcome(
                    GenesisManualRecordingCommandKind.Cancelled,
                    ReadSnapshot(channel, channelId));
            }
            catch
            {
                return Outcome(
                    GenesisManualRecordingCommandKind.Failed,
                    ReadSnapshot(channel, channelId));
            }
        }
    }

    public async ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        GenesisChannelItem channel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return !GenesisRecordingCandidateAdapter.TryGetChannelId(channel, out var channelId) ||
            channelId is null
                ? Outcome(
                    GenesisManualRecordingCommandKind.Rejected,
                    GenesisRecordingCoreSnapshot.Unavailable)
                : await StopCoreAsync(channelId, expectedSessionId: null, cancellationToken)
                    .ConfigureAwait(false);
    }

    public ValueTask<GenesisManualRecordingCommandOutcome> StopAsync(
        ChannelId channelId,
        RecordingSessionId expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(expectedSessionId);
        return StopCoreAsync(channelId, expectedSessionId, cancellationToken);
    }

    private async ValueTask<GenesisManualRecordingCommandOutcome> StopCoreAsync(
        ChannelId channelId,
        RecordingSessionId? expectedSessionId,
        CancellationToken cancellationToken)
    {
        if (channelId is null)
        {
            return Outcome(
                GenesisManualRecordingCommandKind.Rejected,
                GenesisRecordingCoreSnapshot.Unavailable);
        }

        var operation = TryBeginOperation(cancellationToken);
        if (operation is null)
        {
            return Outcome(
                GenesisManualRecordingCommandKind.Cancelled,
                GetSnapshot(channelId));
        }

        using (operation)
        {
            var lane = GetChannelLane(channelId);
            try
            {
                await lane.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    GenesisManualRecordingSessionIdentity? identity;
                    lock (_lifecycleGate)
                    {
                        _ = _manualSessions.TryGetValue(channelId, out identity);
                    }

                    if (identity is null)
                    {
                        var untrackedState = await _recording.ReconcileStateAsync(
                                channelId,
                                operation.Token)
                            .ConfigureAwait(false);
                        var untrackedSnapshot = ToSnapshot(
                            isCandidateValid: false,
                            untrackedState);
                        return untrackedState.Session is null &&
                            !untrackedState.AutomationState.IsRecording
                                ? Outcome(
                                    GenesisManualRecordingCommandKind.NoActiveSession,
                                    untrackedSnapshot)
                                : Outcome(
                                    GenesisManualRecordingCommandKind.Rejected,
                                    untrackedSnapshot);
                    }

                    if (expectedSessionId is not null &&
                        identity.SessionId != expectedSessionId)
                    {
                        return Outcome(
                            GenesisManualRecordingCommandKind.Rejected,
                            ReadSnapshot(channelId));
                    }

                    var current = await _recording.ReconcileStateAsync(
                            channelId,
                            operation.Token)
                        .ConfigureAwait(false);
                    if (current.Session is null && !current.AutomationState.IsRecording)
                    {
                        RemoveManualIdentityIfCurrent(identity);
                        return Outcome(
                            GenesisManualRecordingCommandKind.NoActiveSession,
                            ReadSnapshot(channelId));
                    }

                    if (current.Session is not { } session ||
                        session.Origin != RecordingSessionOrigin.Manual ||
                        session.SessionId != identity.SessionId)
                    {
                        RemoveManualIdentityIfCurrent(identity);
                        return Outcome(
                            GenesisManualRecordingCommandKind.Rejected,
                            ReadSnapshot(channelId));
                    }

                    var result = await _recording.StopManualAsync(
                            channelId,
                            identity.SessionId,
                            operation.Token)
                        .ConfigureAwait(false);
                    var stateAfterStop = ToSnapshot(
                        isCandidateValid: false,
                        result.State);
                    if (stateAfterStop.State != GenesisRecordingCoreStateKind.Manual ||
                        stateAfterStop.SessionId != identity.SessionId)
                    {
                        RemoveManualIdentityIfCurrent(identity);
                    }

                    return Outcome(
                        result.Kind switch
                        {
                            RecordingAutomationStopResultKind.NoActiveSession =>
                                GenesisManualRecordingCommandKind.NoActiveSession,
                            RecordingAutomationStopResultKind.Stopped =>
                                GenesisManualRecordingCommandKind.Stopped,
                            RecordingAutomationStopResultKind.NoDecodableVideoStart =>
                                GenesisManualRecordingCommandKind.NoDecodableVideoStart,
                            RecordingAutomationStopResultKind.AlreadyStopped =>
                                GenesisManualRecordingCommandKind.AlreadyStopped,
                            RecordingAutomationStopResultKind.Rejected =>
                                GenesisManualRecordingCommandKind.Rejected,
                            RecordingAutomationStopResultKind.Faulted =>
                                GenesisManualRecordingCommandKind.Faulted,
                            _ => GenesisManualRecordingCommandKind.Failed,
                        },
                        stateAfterStop);
                }
                finally
                {
                    lane.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return Outcome(
                    GenesisManualRecordingCommandKind.Cancelled,
                    ReadSnapshot(channelId));
            }
            catch
            {
                return Outcome(
                    GenesisManualRecordingCommandKind.Failed,
                    ReadSnapshot(channelId));
            }
        }
    }

    public ValueTask ShutdownAsync()
    {
        Task shutdownTask;
        Task drainTask;
        lock (_lifecycleGate)
        {
            if (_shutdownTask is not null)
            {
                return new(_shutdownTask);
            }

            _accepting = false;
            drainTask = _activeOperations == 0
                ? Task.CompletedTask
                : (_operationsDrained = new(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdownTask = shutdownTask = completion.Task;
            _ = CompleteShutdownAsync(drainTask, completion);
        }

        try
        {
            _shutdownCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed shutdown has already released the token source.
        }

        return new(shutdownTask);
    }

    public ValueTask DisposeAsync() => ShutdownAsync();

    private async Task CompleteShutdownAsync(
        Task drainTask,
        TaskCompletionSource completion)
    {
        try
        {
            await drainTask.ConfigureAwait(false);
            lock (_lifecycleGate)
            {
                foreach (var lane in _channelLanes.Values)
                {
                    lane.Dispose();
                }

                _channelLanes.Clear();
            }

            _shutdownCancellation.Dispose();
            lock (_lifecycleGate)
            {
                _manualSessions.Clear();
            }
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private GenesisRecordingCoreSnapshot ReadSnapshot(
        GenesisChannelItem channel,
        ChannelId channelId)
    {
        var snapshot = ReadSnapshot(channelId);
        return snapshot with
        {
            IsCandidateValid = GenesisRecordingCandidateAdapter.TryCreate(channel, out _),
        };
    }

    private GenesisRecordingCoreSnapshot ReadSnapshot(ChannelId channelId)
    {
        if (!_recording.TryGetState(channelId, out var state) || state is null)
        {
            return new(false, GenesisRecordingCoreStateKind.Idle);
        }

        return ToSnapshot(isCandidateValid: false, state);
    }

    private GenesisRecordingCoreStateKind ReadState(ChannelId channelId)
    {
        return GetSnapshot(channelId).State;
    }

    private static GenesisRecordingCoreSnapshot ToSnapshot(
        bool isCandidateValid,
        RecordingAutomationExecutionState state)
    {
        if (state.Session is null)
        {
            return new(
                isCandidateValid,
                state.AutomationState.IsRecording
                    ? GenesisRecordingCoreStateKind.Unavailable
                    : GenesisRecordingCoreStateKind.Idle);
        }

        return state.Session.Origin switch
        {
            RecordingSessionOrigin.Manual => new(
                isCandidateValid,
                GenesisRecordingCoreStateKind.Manual,
                state.Session.SessionId),
            RecordingSessionOrigin.AutomaticRule => new(
                isCandidateValid,
                GenesisRecordingCoreStateKind.Automatic,
                state.Session.SessionId),
            _ => new(isCandidateValid, GenesisRecordingCoreStateKind.Unavailable),
        };
    }

    private void RemoveManualIdentityIfCurrent(
        GenesisManualRecordingSessionIdentity identity)
    {
        lock (_lifecycleGate)
        {
            _ = TryRemoveManualIdentityIfCurrent(_manualSessions, identity);
        }
    }

    private OperationLease? TryBeginOperation(CancellationToken cancellationToken)
    {
        lock (_lifecycleGate)
        {
            if (!_accepting)
            {
                return null;
            }

            _activeOperations++;
            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdownCancellation.Token);
            return new(this, linked);
        }
    }

    private SemaphoreSlim GetChannelLane(ChannelId channelId)
    {
        lock (_lifecycleGate)
        {
            if (!_channelLanes.TryGetValue(channelId, out var lane))
            {
                lane = new SemaphoreSlim(1, 1);
                _channelLanes.Add(channelId, lane);
            }

            return lane;
        }
    }

    private void EndOperation()
    {
        lock (_lifecycleGate)
        {
            _activeOperations--;
            if (!_accepting && _activeOperations == 0)
            {
                _operationsDrained?.TrySetResult();
            }
        }
    }

    private static GenesisManualRecordingCommandOutcome Outcome(
        GenesisManualRecordingCommandKind kind,
        GenesisRecordingCoreSnapshot snapshot) =>
        new(kind, snapshot);

    private sealed class OperationLease : IDisposable
    {
        private GenesisManualRecordingSessionController? _owner;
        private readonly CancellationTokenSource _linked;

        public OperationLease(
            GenesisManualRecordingSessionController owner,
            CancellationTokenSource linked)
        {
            _owner = owner;
            _linked = linked;
        }

        public CancellationToken Token => _linked.Token;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null)
            {
                return;
            }

            _linked.Dispose();
            owner.EndOperation();
        }
    }
}
