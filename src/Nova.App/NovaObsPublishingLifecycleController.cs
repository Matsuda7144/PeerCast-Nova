using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal enum NovaObsPublishingLifecycleState { NotStarted, Starting, Running, Stopping, Stopped, Failed }
internal enum NovaObsPublishingStartResult { Accepted, AlreadyStarted, CancelledBeforeAdmission, ClosedGeneration, Unavailable, Failed }
internal enum NovaObsPublishingCompletionKind { None, Cancelled, ObsNormalEos, Disconnected, IncompleteEos, Failed, LocalCompleted, TimedOut, DeliveryFailed, CleanupIncomplete }
internal enum NovaObsPublishingLifecycleFailure { None, OwnerUnavailable, LifecycleUnavailable, StartFailed, CompletionFailed, CleanupFailed, NormalEosUnproven, SourceFailed, FinishUnavailable, FinishFailed, FinishTimedOut, IdentityUnproven }

internal sealed record NovaObsPublishingLifecycleResult(
    NovaObsPublishingLifecycleState State,
    NovaObsPublishingCompletionKind Kind,
    NovaObsPublishingLifecycleFailure Failure,
    ObsPublishChannelEndReason? EndReason,
    bool CleanupCompleted,
    bool NormalEosProven)
{
    public ObsPublishLocalTerminationResult? LocalTermination { get; init; }
    public bool FinishRequested { get; init; }
    public bool OwnerCompletionObserved { get; init; }
    public bool IdentityMatched { get; init; }
    public Guid? ChannelId { get; init; }
    public Guid? SessionId { get; init; }
    public bool LocalCompletedProven { get; init; }
    public bool FinishDeadlineExpired { get; init; }
}

internal interface INovaObsPublishingLifecycleController : INovaObsPublishingReadOnlyStatus, IAsyncDisposable
{
    NovaObsPublishingStartResult RequestStart(CancellationToken cancellationToken = default);
    // Stop is forced cleanup; Finish is local termination, never OBS EOS.
    Task<NovaObsPublishingLifecycleResult> RequestStopAsync();
    Task<NovaObsPublishingLifecycleResult> RequestFinishAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<NovaObsPublishingLifecycleResult> Completion { get; }
}

/// <summary>
/// Owns one generation. Commands admit Start once and serialize cleanup after
/// synchronous Start settles. Completion, cancellation and cleanup are distinct
/// facts; the GUI sees only the existing bounded status shape. No restart/polling.
/// </summary>
internal sealed partial class NovaObsPublishingLifecycleController : INovaObsPublishingLifecycleController
{
    private readonly object _gate = new();
    private readonly INovaObsPublishingRuntime? _owner;
    private readonly NovaObsPublishingReadOnlyStatus _status;
    private readonly TaskCompletionSource<NovaObsPublishingLifecycleResult> _terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NovaObsPublishingLifecycleState _state;
    private NovaObsPublishingLifecycleFailure _failure;
    private bool _started;
    private bool _closing;
    private TaskCompletionSource? _startFinished;
    private Task? _observation;
    private ObsPublishChannelResult? _cleanupResult;
    private Task<ObsPublishChannelResult>? _postDisposeCompletion;
    internal Task<ObsPublishChannelResult>? PostDisposeCompletion => _postDisposeCompletion;
    private Task? _stopWorker;
    private Task<bool>? _cleanup;
    private Task? _disposal;
    private Task<ObsPublishChannelResult>? _ownerCompletion;
    private bool _stopRequested;
    private NovaObsPublishingLifecycleFailure _cleanupFailure;
    internal int LateFaultCount => Volatile.Read(ref _lateFaultCount);
    private int _lateFaultCount;

    internal NovaObsPublishingLifecycleController(INovaObsPublishingRuntime? owner)
    {
        _owner = owner;
        _status = new(owner);
        _state = owner is null ? NovaObsPublishingLifecycleState.Failed : NovaObsPublishingLifecycleState.NotStarted;
        _failure = owner is null ? NovaObsPublishingLifecycleFailure.OwnerUnavailable : NovaObsPublishingLifecycleFailure.None;
    }

    internal Action? StatusReadContentionObserver
    {
        get => _status.ContendedReadObserver;
        set => _status.ContendedReadObserver = value;
    }

    internal bool StopAdmissionObserved { get { lock (_gate) return _stopRequested; } }

    internal bool CanRequestFinish
    {
        get
        {
            lock (_gate) return _started && !_closing && !_terminal.Task.IsCompleted && _disposal is null &&
            _failure == NovaObsPublishingLifecycleFailure.None && _owner is INovaObsPublishingFinishRuntime;
        }
    }

    public Task<NovaObsPublishingLifecycleResult> Completion => _terminal.Task;

    // No borrowed callbacks: used immediately before explicit UI disclosure/copy.
    internal bool CanDiscloseConnection(NovaObsPublishingLifecycleState? expected)
    {
        lock (_gate) return expected == _state && _owner is not null && !_closing && !_stopRequested && _disposal is null &&
            !_terminal.Task.IsCompleted && _ownerCompletion?.IsCompleted != true &&
            _failure == NovaObsPublishingLifecycleFailure.None &&
            _state is NovaObsPublishingLifecycleState.NotStarted or NovaObsPublishingLifecycleState.Starting or NovaObsPublishingLifecycleState.Running;
    }

    public NovaObsPublishingStartResult RequestStart(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (cancellationToken.IsCancellationRequested) return NovaObsPublishingStartResult.CancelledBeforeAdmission;
            if (_closing || _terminal.Task.IsCompleted) return NovaObsPublishingStartResult.ClosedGeneration;
            if (_owner is not INovaObsPublishingLifecycleRuntime)
            {
                _failure = _owner is null ? NovaObsPublishingLifecycleFailure.OwnerUnavailable : NovaObsPublishingLifecycleFailure.LifecycleUnavailable;
                _state = NovaObsPublishingLifecycleState.Failed;
                _closing = true;
                return NovaObsPublishingStartResult.Unavailable;
            }
            if (_started) return NovaObsPublishingStartResult.AlreadyStarted;
            _started = true;
            _state = NovaObsPublishingLifecycleState.Starting;
            _startFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Task<ObsPublishChannelResult>? completion = null;
        try
        {
            // Caller cancellation controls admission only. Owned lifetime ends
            // through explicit Stop/disposal, never an abandoned request token.
            _owner.Start(CancellationToken.None);
            completion = ((INovaObsPublishingLifecycleRuntime)_owner).Completion;
            if (completion is null) throw new InvalidOperationException("Missing publishing completion.");
        }
        catch
        {
            lock (_gate) { if (!_terminal.Task.IsCompleted) _failure = NovaObsPublishingLifecycleFailure.StartFailed; _closing = true; }
        }
        lock (_gate)
        {
            _ownerCompletion = completion;
            if (!_closing) _state = NovaObsPublishingLifecycleState.Running;
            if (completion is not null)
            {
                ObserveLateFault(completion);
                _observation = Task.Run(() => ObserveCompletionAsync(completion), CancellationToken.None);
                ObserveLateFault(_observation);
            }
            _startFinished!.TrySetResult();
        }
        if (completion is not null) return NovaObsPublishingStartResult.Accepted;
        _ = RequestStopAsync();
        return NovaObsPublishingStartResult.Failed;
    }

    public Task<NovaObsPublishingLifecycleResult> RequestStopAsync()
    {
        lock (_gate)
        {
            if (_terminal.Task.IsCompleted) return _terminal.Task;
            _closing = true;
            _state = NovaObsPublishingLifecycleState.Stopping;
            _stopRequested = true;
            EnsureStopWorkerLocked();
            return _terminal.Task;
        }
    }

    private async Task StopCoreAsync()
    {
        if (_startFinished is not null) await _startFinished.Task.ConfigureAwait(false);
        var cleaned = await CleanupOnceAsync().ConfigureAwait(false);
        lock (_gate) { if (_finishRequest is not null) return; }
        if (cleaned && _observation is not null)
        {
            await _observation.ConfigureAwait(false);
        }
        else Finish(_cleanupResult, cleaned);
    }

    private async Task ObserveCompletionAsync(Task<ObsPublishChannelResult> completion)
    {
        // A terminal cleanup failure also retires a pending observation.
        if (await Task.WhenAny(completion, _terminal.Task).ConfigureAwait(false) != completion) return;
        ObsPublishChannelResult? result = null;
        try
        {
            result = await completion.ConfigureAwait(false);
            if (result is null) throw new InvalidOperationException("Missing publishing result.");
        }
        catch { lock (_gate) { if (!_terminal.Task.IsCompleted) _failure = NovaObsPublishingLifecycleFailure.CompletionFailed; } }
        lock (_gate)
        {
            if (_finishRequest is not null) return;
            if (!_terminal.Task.IsCompleted) { _closing = true; _state = NovaObsPublishingLifecycleState.Stopping; }
        }
        var cleaned = await CleanupOnceAsync().ConfigureAwait(false);
        Finish(result, cleaned);
    }

    private Task<bool> CleanupOnceAsync()
    {
        lock (_gate)
        {
            if (_cleanup is not null) return _cleanup;
            // Publish before calling the owner: synchronous completion can
            // reenter observation; it must share this same cleanup task.
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cleanup = pending.Task;
            var worker = Task.Run(() => CleanupCoreAsync(pending));
            ObserveLateFault(worker);
            return _cleanup;
        }
    }

    private async Task CleanupCoreAsync(TaskCompletionSource<bool> pending)
    {
        try
        {
            if (_owner is not null) await _owner.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            RecordCleanupFailure(NovaObsPublishingLifecycleFailure.CleanupFailed);
            pending.TrySetResult(false);
            return;
        }

        // No owner means no owned resources. An existing owner must supply
        // actual shutdown evidence even when Start was never requested.
        if (_owner is null) { pending.TrySetResult(true); return; }
        try
        {
            if (_owner is not INovaObsPublishingLifecycleRuntime runtime)
            {
                RecordCleanupFailure(NovaObsPublishingLifecycleFailure.LifecycleUnavailable);
                pending.TrySetResult(false);
                return;
            }
            // The production host rejects Completion access before Start or
            // Dispose. Read only after disposal; never await a broken pending task.
            var completion = runtime.Completion;
            if (completion is not null)
            {
                lock (_gate) { _postDisposeCompletion = completion; }
                ObserveLateFault(completion);
            }
            if (completion is null || !completion.IsCompletedSuccessfully)
                throw new InvalidOperationException("Publishing completion is unavailable.");
            var result = await completion.ConfigureAwait(false);
            if (result is null) throw new InvalidOperationException("Publishing result is unavailable.");
            lock (_gate) { _cleanupResult = result; }
            pending.TrySetResult(result.ShutdownCompleted);
        }
        catch
        {
            RecordCleanupFailure(NovaObsPublishingLifecycleFailure.CompletionFailed);
            pending.TrySetResult(false);
        }
    }

    private void Finish(ObsPublishChannelResult? result, bool cleaned)
    {
        lock (_gate)
        {
            if (_terminal.Task.IsCompleted) return; // Late completion cannot repair a proven cleanup failure.
            cleaned &= result?.ShutdownCompleted ?? true;
            var kind = NovaObsPublishingCompletionKind.Cancelled;
            var normal = IsNormalEosProven(result);
            if (!cleaned && _failure == NovaObsPublishingLifecycleFailure.None)
                _failure = NovaObsPublishingLifecycleFailure.CleanupFailed;
            if (_failure == NovaObsPublishingLifecycleFailure.None && result is not null)
            {
                kind = result.EndReason switch
                {
                    ObsPublishChannelEndReason.PublisherStopped when normal => NovaObsPublishingCompletionKind.ObsNormalEos,
                    ObsPublishChannelEndReason.PublisherStopped => NovaObsPublishingCompletionKind.IncompleteEos,
                    ObsPublishChannelEndReason.HostCancelled => NovaObsPublishingCompletionKind.Cancelled,
                    ObsPublishChannelEndReason.Disconnected => NovaObsPublishingCompletionKind.Disconnected,
                    _ => NovaObsPublishingCompletionKind.Failed,
                };
                if (kind == NovaObsPublishingCompletionKind.IncompleteEos) _failure = NovaObsPublishingLifecycleFailure.NormalEosUnproven;
                if (kind is NovaObsPublishingCompletionKind.Disconnected or NovaObsPublishingCompletionKind.Failed)
                    _failure = NovaObsPublishingLifecycleFailure.SourceFailed;
            }
            if (_failure != NovaObsPublishingLifecycleFailure.None && kind == NovaObsPublishingCompletionKind.Cancelled)
                kind = NovaObsPublishingCompletionKind.Failed;
            _state = _failure == NovaObsPublishingLifecycleFailure.None ? NovaObsPublishingLifecycleState.Stopped : NovaObsPublishingLifecycleState.Failed;
            _closing = true;
            _terminal.TrySetResult(new(_state, kind, _failure, result?.EndReason, cleaned, normal && cleaned && _failure == NovaObsPublishingLifecycleFailure.None));
        }
    }

    private static bool IsNormalEosProven(ObsPublishChannelResult? result) =>
        result is
        {
            EndReason: ObsPublishChannelEndReason.PublisherStopped, ShutdownCompleted: true,
            RelayDrain: PcpRelayDrainOutcome.Completed, HttpDrained: true, HeadCount: 1, AcceptedPacketCount: > 0, AcceptedByteCount: > 0,
            Ingest.EndReason: ObsRtmpIngestEndReason.PublisherStopped
        } && result.Ingest.AcceptedPacketCount == result.AcceptedPacketCount && result.Ingest.AcceptedByteCount == result.AcceptedByteCount;

    public NovaObsPublishingReadOnlySnapshot Read()
    {
        var snapshot = _status.Read();
        Volatile.Read(ref _generationProof)?.Observe(snapshot);
        lock (_gate)
        {
            var terminal = _terminal.Task.IsCompletedSuccessfully ? _terminal.Task.Result : null;
            var text = _state switch
            {
                NovaObsPublishingLifecycleState.Starting => "開始中",
                NovaObsPublishingLifecycleState.Stopping => "終了中",
                NovaObsPublishingLifecycleState.Failed when _owner is not null => "配信エラー",
                NovaObsPublishingLifecycleState.Stopped when terminal?.Kind == NovaObsPublishingCompletionKind.Cancelled => "停止（キャンセル）",
                NovaObsPublishingLifecycleState.Stopped when terminal?.Kind == NovaObsPublishingCompletionKind.LocalCompleted => "終了（Novaローカル）",
                NovaObsPublishingLifecycleState.Stopped => "停止（OBS正常EOS）",
                NovaObsPublishingLifecycleState.Running when snapshot.Availability == NovaObsPublishingAvailability.NotStarted => "開始要求受付済み（待受未確認）",
                _ => snapshot.StateText,
            };
            return snapshot with
            {
                StateText = text,
                Availability = snapshot.Availability == NovaObsPublishingAvailability.Unavailable ? snapshot.Availability :
                    _state == NovaObsPublishingLifecycleState.NotStarted ? snapshot.Availability :
                    _state == NovaObsPublishingLifecycleState.Stopped || terminal?.CleanupCompleted == true ? NovaObsPublishingAvailability.Stopped : NovaObsPublishingAvailability.Observed,
                DiagnosticText = _failure == NovaObsPublishingLifecycleFailure.None ? snapshot.DiagnosticText : FailureText(_failure),
                Tone = _failure != NovaObsPublishingLifecycleFailure.None ? NovaObsPublishingTone.Warning :
                    _state == NovaObsPublishingLifecycleState.Stopped ?
                        terminal?.Kind == NovaObsPublishingCompletionKind.Cancelled ? NovaObsPublishingTone.Special : NovaObsPublishingTone.Normal : snapshot.Tone,
                EndReason = terminal?.EndReason ?? snapshot.EndReason,
                LifecycleState = _state,
                CompletionKind = terminal?.Kind ?? NovaObsPublishingCompletionKind.None,
                LifecycleFailure = _failure,
                LocalTerminationOutcome = terminal?.LocalTermination?.Outcome,
                WriterEvidence = terminal?.LocalTermination?.WriterEvidence,
                CleanupCompleted = terminal?.CleanupCompleted,
                CanRequestFinish = CanRequestFinish,
            };
        }
    }

    private static string FailureText(NovaObsPublishingLifecycleFailure failure) => failure switch
    {
        NovaObsPublishingLifecycleFailure.OwnerUnavailable => "配信ownerが接続されていません。",
        NovaObsPublishingLifecycleFailure.LifecycleUnavailable => "配信終了結果の取得境界が接続されていません。",
        NovaObsPublishingLifecycleFailure.StartFailed => "配信開始要求を処理できませんでした。",
        NovaObsPublishingLifecycleFailure.CompletionFailed => "配信終了結果を取得できませんでした。",
        NovaObsPublishingLifecycleFailure.CleanupFailed => "配信リソースの解放完了を確認できません。",
        NovaObsPublishingLifecycleFailure.NormalEosUnproven => "OBS停止後の正常EOS・ドレイン完了が未証明です。",
        NovaObsPublishingLifecycleFailure.FinishUnavailable => "Novaローカル終了の境界が接続されていません。",
        NovaObsPublishingLifecycleFailure.FinishTimedOut => "Novaローカル終了の期限内完了を確認できません。",
        NovaObsPublishingLifecycleFailure.IdentityUnproven => "配信終了結果のgeneration識別情報を確認できません。",
        NovaObsPublishingLifecycleFailure.FinishFailed => "Novaローカル終了を確認できません。",
        _ => "配信接続が正常EOS以外で終了しました。",
    };

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposal is null)
            {
                if (!_terminal.Task.IsCompleted) { _stopRequested = true; _closing = true; EnsureStopWorkerLocked(); }
                _disposal = Task.Run(DisposeCoreAsync);
                ObserveLateFault(_disposal);
            }
            return new(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var terminal = await RequestStopAsync().ConfigureAwait(false);
        if (!terminal.CleanupCompleted) throw new InvalidOperationException("Publishing cleanup is incomplete.");
    }
}
