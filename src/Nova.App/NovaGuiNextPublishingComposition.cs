namespace PeerCast.Nova.App;

/// <summary>
/// Stable borrowed GUI boundaries over application-owned generations. Replacement
/// requires explicit Start, proven terminal cleanup and drained commands. Each
/// generation has its own lifecycle, status adapter and command tasks.
/// </summary>
internal sealed class NovaGuiNextPublishingComposition : INovaObsPublishingReadOnlyStatus,
    INovaGuiNextPublishingCommands, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly INovaObsPublishingRuntimeFactory _factory;
    private readonly TimeProvider _finishTimeProvider;
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<INovaObsPublishingRuntime, object> _seenOwners = new();
    private readonly HashSet<Guid> _seenSessions = [];
    private Generation _current;
    private Generation? _discarded;
    private Task? _replacement;
    private Task? _disposal;
    private bool _closed;
    private string _failure = string.Empty;

    private NovaGuiNextPublishingComposition(INovaObsPublishingRuntimeFactory factory, TimeProvider finishTimeProvider)
    {
        _factory = factory;
        _finishTimeProvider = finishTimeProvider;
        INovaObsPublishingRuntime? owner;
        try { owner = factory.Create(); }
        catch { owner = null; } // Startup remains available; no raw exception text.
        _current = new(1, owner, _finishTimeProvider);
        if (owner is not null) _seenOwners.Add(owner, new());
    }

    // Application/testing access only. GUI binding receives stable interfaces.
    internal INovaObsPublishingLifecycleController Lifecycle { get { lock (_gate) return _current.Lifecycle; } }
    internal INovaObsPublishingReadOnlyStatus Status => this;
    internal INovaGuiNextPublishingCommands Commands => this;

    // Application-only; GUI continues to borrow the unchanged command interface.
    // Same gate as restart and factory admission. Closing never creates, starts
    // or disposes an owner and does not wait for replacement work.
    internal void CloseAdmission()
    {
        lock (_gate)
        {
            _closed = true;
            _current.Commands.CloseAdmission();
        }
    }

    internal static NovaGuiNextPublishingComposition Create(INovaObsPublishingRuntimeFactory factory, TimeProvider? finishTimeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(factory, finishTimeProvider ?? TimeProvider.System);
    }

    internal Action? CommandObservationReady { get; set; }

    public NovaObsPublishingReadOnlySnapshot Read()
    {
        Generation generation;
        lock (_gate) { generation = _current; }
        var snapshot = generation.Lifecycle.Read();
        lock (_gate)
        {
            if (!ReferenceEquals(generation, _current)) return SupersededSnapshotLocked();
            CaptureIdentityLocked(generation, snapshot);
            return snapshot with { Generation = generation.Number };
        }
    }

    private void CaptureIdentityLocked(Generation generation, NovaObsPublishingReadOnlySnapshot snapshot)
    {
        if (!_closed && generation.SessionId is null && snapshot.SessionId is { } session)
        {
            generation.SessionId = session;
            generation.ChannelId = snapshot.ChannelId;
            _seenSessions.Add(session);
        }
    }

    private NovaObsPublishingReadOnlySnapshot SupersededSnapshotLocked() =>
        new(NovaObsPublishingAvailability.Unavailable, _current.ChannelId, _current.SessionId,
            "利用不可", "配信generationが変更されました。", PeerCast.Nova.Windows.NovaObsPublishingTone.Warning, null, null, null)
        { Generation = _current.Number };

    private static bool HasProvenTerminalCleanup(Generation generation) =>
        generation.Lifecycle.Completion.IsCompletedSuccessfully && generation.Lifecycle.Completion.Result is
        {
            State: NovaObsPublishingLifecycleState.Stopped,
            Failure: NovaObsPublishingLifecycleFailure.None,
            CleanupCompleted: true,
            Kind: NovaObsPublishingCompletionKind.Cancelled or NovaObsPublishingCompletionKind.ObsNormalEos or NovaObsPublishingCompletionKind.LocalCompleted,
        } terminal && generation.SessionId is not null &&
        (!terminal.FinishRequested || (generation.Lifecycle.HasCurrentGenerationProof && (terminal.LocalCompletedProven || terminal.NormalEosProven ||
            (terminal.Kind == NovaObsPublishingCompletionKind.Cancelled && terminal.OwnerCompletionObserved &&
                terminal.EndReason == PeerCast.Nova.Network.Publishing.ObsPublishChannelEndReason.HostCancelled &&
                terminal.LocalTermination?.Outcome == PeerCast.Nova.Network.Publishing.ObsPublishLocalTerminationOutcome.Cancelled)) && terminal.IdentityMatched &&
            terminal.ChannelId == generation.ChannelId && terminal.SessionId == generation.SessionId));

    // Application-only boundary; no new native GUI operation is bound in this phase.
    internal Task<NovaObsPublishingLifecycleResult> RequestFinishAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_closed || _replacement is { IsCompleted: false })
                return Task.FromResult(new NovaObsPublishingLifecycleResult(NovaObsPublishingLifecycleState.Failed,
                    NovaObsPublishingCompletionKind.Failed, NovaObsPublishingLifecycleFailure.FinishUnavailable, null, false, false));
            return _current.Lifecycle.RequestFinishAsync(timeout, cancellationToken);
        }
    }

    private sealed record CommandObservation(Generation Generation, NovaObsPublishingReadOnlySnapshot Snapshot,
        NovaGuiNextPublishingCommandState Commands, NovaGuiNextPublishingCommandState State);

    private NovaGuiNextPublishingCommandState DisabledCommandsLocked() =>
        new(false, false, _failure) { IsPending = _replacement is { IsCompleted: false } || _current.Commands.HasPendingCommands };

    public NovaGuiNextPublishingCommandState ReadCommands()
    {
        var observation = ReadCommandObservation();
        lock (_gate) return observation is not null && !_closed && ReferenceEquals(observation.Generation, _current)
            ? observation.State : DisabledCommandsLocked();
    }

    private CommandObservation? ReadCommandObservation()
    {
        Generation generation;
        lock (_gate)
        {
            if (_closed) return null;
            generation = _current;
        }
        var snapshot = generation.Lifecycle.Read(); // External work, without admission gates.
        lock (_gate)
        {
            if (_closed || !ReferenceEquals(generation, _current)) return null;
            CaptureIdentityLocked(generation, snapshot);
            var commands = generation.Commands.ReadCommandsFromSnapshot(snapshot);
            var replacing = _replacement is { IsCompleted: false };
            var replaceable = !commands.IsPending && snapshot.Availability != NovaObsPublishingAvailability.Unavailable &&
                HasProvenTerminalCleanup(generation);
            var diagnostic = _failure.Length > 0 ? _failure : commands.DiagnosticText;
            if (diagnostic.Length == 0)
            {
                if (replacing) diagnostic = "新しいgenerationを準備中（待受未確認）";
                else if (snapshot.LifecycleFailure == NovaObsPublishingLifecycleFailure.CleanupFailed)
                    diagnostic = "再開始不可：前generationのcleanup完了が未証明です。";
                else if (generation.Lifecycle.Completion.IsCompleted && !replaceable && !commands.CanStart)
                    diagnostic = "再開始不可：前generationの安全な終了・識別情報を確認できません。";
                else if (snapshot.LifecycleState == NovaObsPublishingLifecycleState.Stopping ||
                    (snapshot.EndReason is not null && !generation.Lifecycle.Completion.IsCompleted))
                    diagnostic = "再開始不可：終了結果とcleanup完了の確認待ちです。";
            }
            var state = new NovaGuiNextPublishingCommandState(_failure.Length == 0 && !replacing &&
                ((commands.CanStart && snapshot.IsSourceNotStarted) || replaceable),
                _failure.Length == 0 && commands.CanInterrupt, diagnostic)
            { IsPending = replacing || commands.IsPending };
            return new(generation, snapshot, commands, state);
        }
    }

    public Task RequestStartAsync()
    {
        var observation = ReadCommandObservation();
        CommandObservationReady?.Invoke(); // Optional test barrier, outside admission locks.
        lock (_gate)
        {
            if (observation is null || _closed || _failure.Length != 0 || !ReferenceEquals(observation.Generation, _current) ||
                _replacement is { IsCompleted: false } || !observation.State.CanStart) return Task.CompletedTask;
            if (observation.Commands.CanStart) return _current.Commands.RequestStartFromSnapshotAsync(observation.Snapshot);
            // Reconcile task and proof state again: observation is not admission.
            if (_current.Commands.HasPendingCommands || !HasProvenTerminalCleanup(_current)) return Task.CompletedTask;
            var retired = _current;
            return _replacement = Task.Run(() => ReplaceAndStartAsync(retired));
        }
    }

    public Task RequestInterruptAsync()
    {
        var observation = ReadCommandObservation();
        CommandObservationReady?.Invoke();
        lock (_gate)
        {
            if (observation is null || _closed || _failure.Length != 0 || !ReferenceEquals(observation.Generation, _current) ||
                !observation.State.CanInterrupt) return Task.CompletedTask;
            return _current.Commands.RequestInterruptFromSnapshotAsync(observation.Snapshot);
        }
    }

    private async Task ReplaceAndStartAsync(Generation retired)
    {
        try { await retired.Commands.DisposeAsync().ConfigureAwait(false); }
        catch { SetFailure("再開始不可：前generationのcleanup完了が未証明です。"); return; }

        // This check is factory admission's linearization point. _replacement
        // was published before scheduling us; shutdown joins that task even if
        // Close wins after admission and before the factory actually executes.
        lock (_gate) { if (_closed) return; }
        INovaObsPublishingRuntime? owner;
        try { owner = _factory.Create(); }
        catch { SetFailure("再開始不可：新しい配信ownerを作成できません。"); return; }
        bool closed;
        lock (_gate)
        {
            if (owner is not null && _seenOwners.TryGetValue(owner, out _))
            {
                _failure = "再開始不可：既存ownerの再利用は許可されていません。";
                return; // Already-owned resources must not be disposed again.
            }
            if (owner is not null) _seenOwners.Add(owner, new());
            closed = _closed;
        }
        var candidate = new Generation(retired.Number + 1, owner, _finishTimeProvider);
        var snapshot = closed ? null : candidate.Lifecycle.Read();
        Task? start = null;
        lock (_gate)
        {
            var valid = owner is INovaObsPublishingLifecycleRuntime && snapshot is not null &&
                snapshot.Availability == NovaObsPublishingAvailability.NotStarted && snapshot.IsSourceNotStarted &&
                snapshot.SessionId is { } session && !_seenSessions.Contains(session);
            if (!_closed && valid)
            {
                candidate.SessionId = snapshot!.SessionId;
                candidate.ChannelId = snapshot.ChannelId;
                _seenSessions.Add(snapshot.SessionId!.Value);
                _current = candidate;
                // Admission and publication are atomic with application shutdown.
                start = candidate.Commands.RequestStartFromSnapshotAsync(snapshot!);
            }
            else if (!_closed) _failure = "再開始不可：新owner・新SessionId・未起動状態を確認できません。";
        }
        if (start is not null) await start.ConfigureAwait(false);
        else
        {
            try { await candidate.Commands.DisposeAsync().ConfigureAwait(false); }
            catch
            {
                lock (_gate)
                {
                    _discarded = candidate; // Preserve failed cleanup ownership for shutdown reporting.
                    _failure = "再開始不可：候補generationのcleanup完了が未証明です。";
                }
            }
        }
    }

    private void SetFailure(string message) { lock (_gate) _failure = message; }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            CloseAdmission();
            var replacement = _replacement;
            return new(_disposal ??= Task.Run(async () =>
            {
                if (replacement is not null) await replacement.ConfigureAwait(false);
                Generation current;
                Generation? discarded;
                lock (_gate) { current = _current; discarded = _discarded; }
                try { await current.Commands.DisposeAsync().ConfigureAwait(false); }
                finally { if (discarded is not null) await discarded.Commands.DisposeAsync().ConfigureAwait(false); }
            }));
        }
    }

    private sealed class Generation
    {
        internal Generation(long number, INovaObsPublishingRuntime? owner, TimeProvider finishTimeProvider)
        {
            Number = number;
            Lifecycle = new NovaObsPublishingLifecycleController(owner) { FinishTimeProvider = finishTimeProvider };
            Commands = new(Lifecycle);
        }
        internal long Number { get; }
        internal Guid? SessionId { get; set; }
        internal Guid? ChannelId { get; set; }
        internal NovaObsPublishingLifecycleController Lifecycle { get; }
        internal NovaGuiNextPublishingCommands Commands { get; }
    }
}
