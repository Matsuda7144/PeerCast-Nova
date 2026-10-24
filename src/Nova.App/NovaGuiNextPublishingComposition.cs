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
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<INovaObsPublishingRuntime, object> _seenOwners = new();
    private readonly HashSet<Guid> _seenSessions = [];
    private Generation _current;
    private Generation? _discarded;
    private Task? _replacement;
    private Task? _disposal;
    private bool _closed;
    private string _failure = string.Empty;

    private NovaGuiNextPublishingComposition(INovaObsPublishingRuntimeFactory factory)
    {
        _factory = factory;
        INovaObsPublishingRuntime? owner;
        try { owner = factory.Create(); }
        catch { owner = null; } // Startup remains available; no raw exception text.
        _current = new(1, owner);
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

    internal static NovaGuiNextPublishingComposition Create(INovaObsPublishingRuntimeFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(factory);
    }

    public NovaObsPublishingReadOnlySnapshot Read()
    {
        lock (_gate) return ReadCurrent();
    }

    private NovaObsPublishingReadOnlySnapshot ReadCurrent()
    {
        var snapshot = _current.Lifecycle.Read();
        if (_current.SessionId is null && snapshot.SessionId is { } session)
        {
            _current.SessionId = session;
            _seenSessions.Add(session);
        }
        return snapshot with { Generation = _current.Number };
    }

    private static bool HasProvenTerminalCleanup(Generation generation) =>
        generation.Lifecycle.Completion.IsCompletedSuccessfully && generation.Lifecycle.Completion.Result is
        {
            State: NovaObsPublishingLifecycleState.Stopped,
            Failure: NovaObsPublishingLifecycleFailure.None,
            CleanupCompleted: true,
            Kind: NovaObsPublishingCompletionKind.Cancelled or NovaObsPublishingCompletionKind.ObsNormalEos,
        } && generation.SessionId is not null;

    public NovaGuiNextPublishingCommandState ReadCommands()
    {
        lock (_gate)
        {
            var snapshot = ReadCurrent();
            var commands = _current.Commands.ReadCommands();
            var replacing = _replacement is { IsCompleted: false };
            var replaceable = !commands.IsPending && HasProvenTerminalCleanup(_current);
            var diagnostic = _failure.Length > 0 ? _failure : commands.DiagnosticText;
            if (diagnostic.Length == 0)
            {
                if (replacing) diagnostic = "新しいgenerationを準備中（待受未確認）";
                else if (snapshot.LifecycleFailure == NovaObsPublishingLifecycleFailure.CleanupFailed)
                    diagnostic = "再開始不可：前generationのcleanup完了が未証明です。";
                else if (_current.Lifecycle.Completion.IsCompleted && !replaceable && !commands.CanStart)
                    diagnostic = "再開始不可：前generationの安全な終了・識別情報を確認できません。";
                else if (snapshot.LifecycleState == NovaObsPublishingLifecycleState.Stopping ||
                    (snapshot.EndReason is not null && !_current.Lifecycle.Completion.IsCompleted))
                    diagnostic = "再開始不可：終了結果とcleanup完了の確認待ちです。";
            }
            return new(!_closed && _failure.Length == 0 && !replacing &&
                ((commands.CanStart && snapshot.IsSourceNotStarted) || replaceable),
                !_closed && _failure.Length == 0 && commands.CanInterrupt, diagnostic)
            {
                IsPending = replacing || commands.IsPending,
            };
        }
    }

    public Task RequestStartAsync()
    {
        lock (_gate)
        {
            if (!ReadCommands().CanStart) return Task.CompletedTask;
            if (_current.Commands.ReadCommands().CanStart) return _current.Commands.RequestStartAsync();
            var retired = _current;
            return _replacement = Task.Run(() => ReplaceAndStartAsync(retired));
        }
    }

    public Task RequestInterruptAsync()
    {
        lock (_gate)
            return ReadCommands().CanInterrupt ? _current.Commands.RequestInterruptAsync() : Task.CompletedTask;
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
        var candidate = new Generation(retired.Number + 1, owner);
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
                _seenSessions.Add(snapshot.SessionId!.Value);
                _current = candidate;
                // Admission and publication are atomic with application shutdown.
                start = candidate.Commands.RequestStartAsync();
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
        internal Generation(long number, INovaObsPublishingRuntime? owner)
        {
            Number = number;
            Lifecycle = new NovaObsPublishingLifecycleController(owner);
            Commands = new(Lifecycle);
        }
        internal long Number { get; }
        internal Guid? SessionId { get; set; }
        internal NovaObsPublishingLifecycleController Lifecycle { get; }
        internal NovaGuiNextPublishingCommands Commands { get; }
    }
}
