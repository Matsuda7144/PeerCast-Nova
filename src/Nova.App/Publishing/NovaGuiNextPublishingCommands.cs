namespace PeerCast.Nova.App;

internal sealed record NovaGuiNextPublishingCommandState(bool CanStart, bool CanInterrupt, string DiagnosticText)
{
    internal bool IsPending { get; init; }
    internal bool CanFinish { get; init; }
    internal long? Generation { get; init; }
    internal static readonly NovaGuiNextPublishingCommandState Unavailable = new(false, false, "操作境界が未接続です。");
}

internal interface INovaGuiNextPublishingCommands
{
    NovaGuiNextPublishingCommandState ReadCommands();
    Task RequestStartAsync();
    Task RequestInterruptAsync();
    Task RequestFinishAsync();
}

/// <summary>
/// Application-owned asynchronous command admission for one generation. The GUI
/// borrows commands and status; only the lifecycle controller owns the runtime.
/// Interrupt/shutdown wait for admitted Start, then use cancellation cleanup.
/// </summary>
internal sealed class NovaGuiNextPublishingCommands(INovaObsPublishingLifecycleController lifecycle, TimeSpan? finishTimeout = null)
    : INovaGuiNextPublishingCommands, IAsyncDisposable
{
    private readonly object _gate = new();
    private Task? _start;
    private Task? _interrupt;
    private Task? _disposal;
    private Task? _finish;
    private Task? _forcedStop;
    private readonly TimeSpan _finishTimeout = finishTimeout ?? TimeSpan.FromSeconds(30);
    internal Task? AcceptedFinish { get { lock (_gate) return _finish; } }
    internal bool FinishPending { get { lock (_gate) return _finish is { IsCompleted: false }; } }
    private bool _closed;
    private string _failure = string.Empty;

    internal void CloseAdmission()
    {
        lock (_gate) _closed = true;
    }

    internal bool HasPendingCommands
    {
        get { lock (_gate) return _start is { IsCompleted: false } || _interrupt is { IsCompleted: false } || _finish is { IsCompleted: false }; }
    }

    private string DiagnosticLocked() => _failure.Length > 0 ? _failure : _interrupt is { IsCompleted: false }
        ? "中断要求処理中（キャンセルによる終了）" : _finish is { IsCompleted: false } ? "配信終了要求中（Novaローカル・期限付き）" : _start is { IsCompleted: false }
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
                !_closed && available && _start is null && _interrupt is null && _finish is null &&
                    snapshot.LifecycleState == NovaObsPublishingLifecycleState.NotStarted,
                !_closed && _failure.Length == 0 && _interrupt is null &&
                    (_finish is { IsCompleted: false } || _start is { IsCompleted: false } || snapshot.LifecycleState is
                        NovaObsPublishingLifecycleState.Starting or NovaObsPublishingLifecycleState.Running), DiagnosticLocked())
            { IsPending = HasPendingCommands, CanFinish = !_closed && available && _finish is null && _interrupt is null && snapshot.CanRequestFinish };
        }
    }

    public Task RequestStartAsync()
    {
        lock (_gate) { if (_closed || _start is not null || _interrupt is not null || _finish is not null) return Task.CompletedTask; }
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
            if (_finish is { IsCompleted: false }) return ForceStopLocked();
        }
        return RequestInterruptFromSnapshotAsync(lifecycle.Read());
    }

    internal Task RequestInterruptFromSnapshotAsync(NovaObsPublishingReadOnlySnapshot snapshot)
    {
        lock (_gate)
        {
            if (_interrupt is not null) return _interrupt;
            if (!_closed && _finish is { IsCompleted: false }) return ForceStopLocked();
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

    public Task RequestFinishAsync()
    {
        lock (_gate)
        {
            if (_finish is not null) return _finish;
            if (_closed || _interrupt is not null || lifecycle is not NovaObsPublishingLifecycleController owned || !owned.CanRequestFinish)
                return Task.CompletedTask;
            // Sealed application admission reads only internal state, never Snapshot.
            // Its existing monotonic deadline begins here, before worker scheduling.
            _finish = owned.RequestFinishAsync(_finishTimeout);
            ObserveCommandFault(_finish);
            return _finish;
        }
    }

    private Task ForceStopLocked()
    {
        _forcedStop ??= lifecycle.RequestStopAsync();
        ObserveCommandFault(_forcedStop);
        return _interrupt ??= _forcedStop;
    }

    private void ObserveCommandFault(Task task) => _ = task.ContinueWith(t =>
    {
        _ = t.Exception;
        lock (_gate) _failure = "配信操作の完了を確認できません。";
    }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _closed = true;
            var start = _start;
            var interrupt = _interrupt;
            var finish = _finish;
            if (finish is { IsCompleted: false }) _ = ForceStopLocked();
            return new(_disposal ??= Task.Run(async () =>
            {
                if (start is not null) await JoinCommandOrTerminalAsync(start).ConfigureAwait(false);
                if (interrupt is not null) await JoinCommandOrTerminalAsync(interrupt).ConfigureAwait(false);
                if (finish is not null) await JoinCommandOrTerminalAsync(finish).ConfigureAwait(false);
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
