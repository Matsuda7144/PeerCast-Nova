using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Rtmp;
using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App;

internal enum NovaObsPublishingLifecycleState { NotStarted, Starting, Running, Stopping, Stopped, Failed }
internal enum NovaObsPublishingStartResult { Accepted, AlreadyStarted, CancelledBeforeAdmission, ClosedGeneration, Unavailable, Failed }
internal enum NovaObsPublishingCompletionKind { None, Cancelled, ObsNormalEos, Disconnected, IncompleteEos, Failed }
internal enum NovaObsPublishingLifecycleFailure { None, OwnerUnavailable, LifecycleUnavailable, StartFailed, CompletionFailed, CleanupFailed, NormalEosUnproven, SourceFailed }

internal sealed record NovaObsPublishingLifecycleResult(
    NovaObsPublishingLifecycleState State,
    NovaObsPublishingCompletionKind Kind,
    NovaObsPublishingLifecycleFailure Failure,
    ObsPublishChannelEndReason? EndReason,
    bool CleanupCompleted,
    bool NormalEosProven);

internal interface INovaObsPublishingLifecycleController : INovaObsPublishingReadOnlyStatus, IAsyncDisposable
{
    NovaObsPublishingStartResult RequestStart(CancellationToken cancellationToken = default);
    // Existing foundation supports cancellation cleanup, not a normal-EOS request.
    Task<NovaObsPublishingLifecycleResult> RequestStopAsync();
    Task<NovaObsPublishingLifecycleResult> Completion { get; }
}

/// <summary>
/// Owns one generation. Commands admit Start once and serialize cleanup after
/// synchronous Start settles. Completion, cancellation and cleanup are distinct
/// facts; the GUI sees only the existing bounded status shape. No restart/polling.
/// </summary>
internal sealed class NovaObsPublishingLifecycleController : INovaObsPublishingLifecycleController
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
    private Task? _stopWorker;
    private Task<bool>? _cleanup;
    private Task? _disposal;

    internal NovaObsPublishingLifecycleController(INovaObsPublishingRuntime? owner)
    {
        _owner = owner;
        _status = new(owner);
        _state = owner is null ? NovaObsPublishingLifecycleState.Failed : NovaObsPublishingLifecycleState.NotStarted;
        _failure = owner is null ? NovaObsPublishingLifecycleFailure.OwnerUnavailable : NovaObsPublishingLifecycleFailure.None;
    }

    public Task<NovaObsPublishingLifecycleResult> Completion => _terminal.Task;

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
            lock (_gate) { _failure = NovaObsPublishingLifecycleFailure.StartFailed; _closing = true; }
        }
        lock (_gate)
        {
            if (!_closing) _state = NovaObsPublishingLifecycleState.Running;
            if (completion is not null)
            {
                _observation = ObserveCompletionAsync(completion);
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
            _stopWorker ??= StopCoreAsync();
            return _terminal.Task;
        }
    }

    private async Task StopCoreAsync()
    {
        if (_startFinished is not null) await _startFinished.Task.ConfigureAwait(false);
        var cleaned = await CleanupOnceAsync().ConfigureAwait(false);
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
        lock (_gate) { if (!_terminal.Task.IsCompleted) { _closing = true; _state = NovaObsPublishingLifecycleState.Stopping; } }
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
            _ = CleanupCoreAsync(pending);
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
            lock (_gate) _failure = NovaObsPublishingLifecycleFailure.CleanupFailed;
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
                lock (_gate) _failure = NovaObsPublishingLifecycleFailure.LifecycleUnavailable;
                pending.TrySetResult(false);
                return;
            }
            // The production host rejects Completion access before Start or
            // Dispose. Read only after disposal; never await a broken pending task.
            var completion = runtime.Completion;
            if (completion is null || !completion.IsCompletedSuccessfully)
                throw new InvalidOperationException("Publishing completion is unavailable.");
            var result = await completion.ConfigureAwait(false);
            if (result is null) throw new InvalidOperationException("Publishing result is unavailable.");
            _cleanupResult = result;
            pending.TrySetResult(result.ShutdownCompleted);
        }
        catch
        {
            lock (_gate)
            {
                if (_failure == NovaObsPublishingLifecycleFailure.None)
                    _failure = NovaObsPublishingLifecycleFailure.CompletionFailed;
            }
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
        lock (_gate)
        {
            var snapshot = _status.Read();
            var terminal = _terminal.Task.IsCompletedSuccessfully ? _terminal.Task.Result : null;
            var text = _state switch
            {
                NovaObsPublishingLifecycleState.Starting => "開始中",
                NovaObsPublishingLifecycleState.Stopping => "終了中",
                NovaObsPublishingLifecycleState.Failed when _owner is not null => "配信エラー",
                NovaObsPublishingLifecycleState.Stopped when terminal?.Kind == NovaObsPublishingCompletionKind.Cancelled => "停止（キャンセル）",
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
        _ => "配信接続が正常EOS以外で終了しました。",
    };

    public ValueTask DisposeAsync()
    {
        lock (_gate) { _disposal ??= DisposeCoreAsync(); return new(_disposal); }
    }

    private async Task DisposeCoreAsync()
    {
        var terminal = await RequestStopAsync().ConfigureAwait(false);
        if (!terminal.CleanupCompleted) throw new InvalidOperationException("Publishing cleanup is incomplete.");
    }
}
