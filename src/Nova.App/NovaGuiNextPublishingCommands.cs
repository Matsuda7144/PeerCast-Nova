namespace PeerCast.Nova.App;

internal sealed record NovaGuiNextPublishingCommandState(bool CanStart, bool CanInterrupt, string DiagnosticText)
{
    internal bool IsPending { get; init; }
    internal static readonly NovaGuiNextPublishingCommandState Unavailable = new(false, false, "操作境界が未接続です。");
}

internal interface INovaGuiNextPublishingCommands
{
    NovaGuiNextPublishingCommandState ReadCommands();
    Task RequestStartAsync();
    Task RequestInterruptAsync();
}

/// <summary>
/// Application-owned asynchronous command admission for one generation. The GUI
/// borrows commands and status; only the lifecycle controller owns the runtime.
/// Interrupt/shutdown wait for admitted Start, then use cancellation cleanup.
/// </summary>
internal sealed class NovaGuiNextPublishingCommands(INovaObsPublishingLifecycleController lifecycle)
    : INovaGuiNextPublishingCommands, IAsyncDisposable
{
    private readonly object _gate = new();
    private Task? _start;
    private Task? _interrupt;
    private Task? _disposal;
    private bool _closed;
    private string _failure = string.Empty;

    internal void CloseAdmission()
    {
        lock (_gate) _closed = true;
    }

    internal bool HasPendingCommands
    {
        get { lock (_gate) return _start is { IsCompleted: false } || _interrupt is { IsCompleted: false }; }
    }

    private string DiagnosticLocked() => _failure.Length > 0 ? _failure : _interrupt is { IsCompleted: false }
        ? "中断要求処理中（キャンセルによる終了）" : _start is { IsCompleted: false }
        ? "開始要求処理中（待受・OBS接続・メディア受信は未確認）" : string.Empty;

    public NovaGuiNextPublishingCommandState ReadCommands()
    {
        lock (_gate)
        {
            if (_closed) return new(false, false, DiagnosticLocked()) { IsPending = HasPendingCommands };
        }
        // The borrowed read may never return. It must not own admission's gate.
        var snapshot = lifecycle.Read();
        return ReadCommandsFromSnapshot(snapshot);
    }

    internal NovaGuiNextPublishingCommandState ReadCommandsFromSnapshot(NovaObsPublishingReadOnlySnapshot snapshot)
    {
        lock (_gate)
        {
            var available = _failure.Length == 0 && snapshot.Availability != NovaObsPublishingAvailability.Unavailable &&
                snapshot.LifecycleFailure == NovaObsPublishingLifecycleFailure.None;
            return new NovaGuiNextPublishingCommandState(
                !_closed && available && _start is null && _interrupt is null &&
                    snapshot.LifecycleState == NovaObsPublishingLifecycleState.NotStarted,
                !_closed && _failure.Length == 0 && _interrupt is null &&
                    (_start is { IsCompleted: false } || snapshot.LifecycleState is
                        NovaObsPublishingLifecycleState.Starting or NovaObsPublishingLifecycleState.Running), DiagnosticLocked())
            { IsPending = HasPendingCommands };
        }
    }

    public Task RequestStartAsync()
    {
        lock (_gate) { if (_closed || _start is not null || _interrupt is not null) return Task.CompletedTask; }
        return RequestStartFromSnapshotAsync(lifecycle.Read());
    }

    // Caller fixes the generation; this method only reads internal admission/task
    // state under the command gate. Lifecycle Start still owns actual admission.
    internal Task RequestStartFromSnapshotAsync(NovaObsPublishingReadOnlySnapshot snapshot)
    {
        lock (_gate)
        {
            if (!ReadCommandsFromSnapshot(snapshot).CanStart) return Task.CompletedTask;
            return _start = Task.Run(() =>
            {
                try { _ = lifecycle.RequestStart(); }
                catch { lock (_gate) _failure = "開始要求に失敗しました。"; }
            });
        }
    }

    public Task RequestInterruptAsync()
    {
        lock (_gate)
        {
            if (_interrupt is not null) return _interrupt;
            if (_closed) return Task.CompletedTask;
        }
        return RequestInterruptFromSnapshotAsync(lifecycle.Read());
    }

    internal Task RequestInterruptFromSnapshotAsync(NovaObsPublishingReadOnlySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_interrupt is not null) return _interrupt;
            if (!ReadCommandsFromSnapshot(snapshot).CanInterrupt) return Task.CompletedTask;
            var start = _start;
            return _interrupt = Task.Run(async () =>
            {
                if (start is not null) await start.ConfigureAwait(false);
                try { _ = await lifecycle.RequestStopAsync().ConfigureAwait(false); }
                catch { lock (_gate) _failure = "中断要求に失敗しました。"; }
            });
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            var start = _start;
            var interrupt = _interrupt;
            return new(_disposal ??= Task.Run(async () =>
            {
                if (start is not null) await JoinCommandOrTerminalAsync(start).ConfigureAwait(false);
                if (interrupt is not null) await JoinCommandOrTerminalAsync(interrupt).ConfigureAwait(false);
                await lifecycle.DisposeAsync().ConfigureAwait(false);
            }));
        }
    }

    private async Task JoinCommandOrTerminalAsync(Task command)
    {
        // A failed bounded Finish can publish a terminal while an admitted Start
        // or its deferred interrupt still settles. Do not extend that deadline
        // at the outer command boundary; lifecycle disposal reports the proof gap.
        if (await Task.WhenAny(command, lifecycle.Completion).ConfigureAwait(false) == command)
            await command.ConfigureAwait(false);
    }
}
