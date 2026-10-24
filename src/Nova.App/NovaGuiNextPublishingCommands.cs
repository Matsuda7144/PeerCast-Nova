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

    public NovaGuiNextPublishingCommandState ReadCommands()
    {
        lock (_gate)
        {
            var snapshot = lifecycle.Read();
            var available = _failure.Length == 0 && snapshot.Availability != NovaObsPublishingAvailability.Unavailable &&
                snapshot.LifecycleFailure == NovaObsPublishingLifecycleFailure.None;
            return new NovaGuiNextPublishingCommandState(
                !_closed && available && _start is null && _interrupt is null &&
                    snapshot.LifecycleState == NovaObsPublishingLifecycleState.NotStarted,
                !_closed && _failure.Length == 0 && _interrupt is null &&
                    (_start is { IsCompleted: false } || snapshot.LifecycleState is
                        NovaObsPublishingLifecycleState.Starting or NovaObsPublishingLifecycleState.Running),
                _failure.Length > 0 ? _failure : _interrupt is { IsCompleted: false }
                    ? "中断要求処理中（キャンセルによる終了）" : _start is { IsCompleted: false }
                    ? "開始要求処理中（待受・OBS接続・メディア受信は未確認）" : string.Empty)
            {
                IsPending = _start is { IsCompleted: false } || _interrupt is { IsCompleted: false },
            };
        }
    }

    public Task RequestStartAsync()
    {
        lock (_gate)
        {
            if (!ReadCommands().CanStart) return Task.CompletedTask;
            // Queue synchronous foundation Start off the native UI thread. The
            // task is published under the same gate as admission and shutdown.
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
            if (!ReadCommands().CanInterrupt) return Task.CompletedTask;
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
                if (start is not null) await start.ConfigureAwait(false);
                if (interrupt is not null) await interrupt.ConfigureAwait(false);
                await lifecycle.DisposeAsync().ConfigureAwait(false);
            }));
        }
    }
}
