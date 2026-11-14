using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Pcp;

public sealed partial class PcpChannelSession
{
    private PcpSmartRelaySnapshot _smartSnapshot = new();
    private PcpSmartRelayMediaConsumer? _smartMedia;
    private readonly TaskCompletionSource _firstSmartMediaProgress = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _smartCleanupFailed;
    internal Func<CancellationToken, Task>? RecoveryReset { get; init; }
    internal Action? RecoveryResume { get; init; }
    internal Func<bool>? RecoveryOwnerIsCurrent { get; init; }
    public PcpSmartRelaySnapshot SmartRelaySnapshot => Volatile.Read(ref _smartSnapshot);
    public bool SmartRelayEnabled => _options.SmartRelay is not null;
    public Task FirstSmartMediaProgress => _firstSmartMediaProgress.Task;
    public bool SmartRelayCleanupConfirmed => Volatile.Read(ref _smartCleanupFailed) == 0;

    internal Action<PcpSmartRelayState>? RecoveryStateChanged { get; init; }
    private void SmartState(PcpSmartRelayState state)
    {
        // Serialize the downstream command boundary before publishing Recovery.
        RecoveryStateChanged?.Invoke(state);
        Volatile.Write(ref _smartSnapshot, SmartRelaySnapshot with
        {
            State = state,
            ResumePosition = _smartMedia?.Cursor,
            MediaGeneration = _smartMedia?.Generation ?? 0,
            Discontinuities = _smartMedia?.Discontinuities ?? 0
        });
    }

    private async Task<PcpChannelSessionResult> RunSmartRelayAsync(string host, int port,
        long initialPosition, CancellationToken caller)
    {
        var options = _options.SmartRelay!;
        var clock = options.TimeProvider;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _stopSource.Token);
        var token = linked.Token;
        var progress = new SessionProgress();
        long? outage = null;
        var shortStep = 0;
        var longStep = 0;
        PcpRelayHostEndpoint? current = null;
        _smartMedia = new(_packetConsumer, clock, RecoveryReset, RecoveryResume, () =>
        {
            _firstSmartMediaProgress.TrySetResult();
            if (current is not null) _peers?.RecordMediaProgress(current);
            if (_smartMedia!.Stable)
            {
                outage = null; shortStep = 0; longStep = 0;
                Volatile.Write(ref _smartSnapshot, SmartRelaySnapshot with { ConsecutiveFailures = 0, LowFrequency = false });
            }
            SmartState(PcpSmartRelayState.Receiving);
        });
        var consumer = new CountingPacketConsumer(_smartMedia);
        var attempts = 0;
        var waveStart = clock.GetTimestamp();
        var waveAttempts = 0;
        var bootstrapTried = false;
        var visited = new HashSet<PcpRelayHostEndpoint>();
        try
        {
            _statusTracker.TransitionTo(ChannelSessionState.Connecting);
            await _smartMedia.SuspendAsync(token).ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (_peers?.IsActive == false || RecoveryOwnerIsCurrent?.Invoke() == false)
                { RequestStop(); token.ThrowIfCancellationRequested(); }
                var elapsed = outage is { } since ? clock.GetElapsedTime(since) : TimeSpan.Zero;
                if (outage is not null && options.Mode == PcpSmartRelayMode.Limited && elapsed >= options.RecoveryLimit)
                {
                    SmartState(PcpSmartRelayState.Failed);
                    RecordFault(new(ChannelFaultCategory.RemoteEnded, ChannelFaultStage.Receiving, attempts, Math.Max(0, attempts - 1)));
                    _negotiation.TrySetResult(new(PcpChannelSessionNegotiationOutcome.Rejected, progress.LastHttpStatusCode ?? 0, null));
                    return CreateResult(PcpChannelSessionEndReason.RecoveryExhausted, attempts, Math.Max(0, attempts - 1),
                        progress.LastHttpStatusCode, progress.ChannelMessageCount, consumer);
                }
                var low = elapsed >= TimeSpan.FromMinutes(5);
                SmartState(PcpSmartRelayState.Discovering);
                var candidate = _peers?.Snapshot().Where(e => e.Host.Firewalled != true)
                    .OrderByDescending(e => e.LastMediaReceived)
                    .ThenByDescending(e => e.Host.Receiving == true)
                    .ThenByDescending(e => e.Host.RelayAvailable == true)
                    .ThenByDescending(e => e.HasSucceeded).ThenByDescending(e => e.ObservedAt)
                    .ThenBy(e => e.Host.Hops ?? byte.MaxValue)
                    .FirstOrDefault(e => e.Host.GlobalEndpoint is { } ep && !visited.Contains(ep))?.Host.GlobalEndpoint;
                // Initial bootstrap is explicit input; HOST candidates always passed directory/policy validation.
                AttemptTarget? target = attempts == 0 || candidate is null && !bootstrapTried
                    ? new(host, port, null)
                    : candidate is not null ? new(candidate.Address.ToString(), candidate.Port, new(candidate.Address, candidate.Port)) : null;
                var remaining = TimeSpan.FromSeconds(30) - clock.GetElapsedTime(waveStart);
                if (outage is not null && options.Mode == PcpSmartRelayMode.Limited)
                    remaining = TimeSpan.FromTicks(Math.Min(remaining.Ticks, (options.RecoveryLimit - elapsed).Ticks));
                if (target is null || waveAttempts >= (low ? 1 : 8) || remaining <= TimeSpan.Zero)
                {
                    if (low) _smartMedia.ForgetDuplicateProof();
                    outage ??= clock.GetTimestamp();
                    var delay = PcpSmartRelayOptions.Backoff(low, low ? longStep : shortStep, options.JitterSample());
                    if (low) longStep = Math.Min(2, longStep + 1); else shortStep = Math.Min(5, shortStep + 1);
                    if (options.Mode == PcpSmartRelayMode.Limited)
                        delay = TimeSpan.FromTicks(Math.Max(0, Math.Min(delay.Ticks,
                            (options.RecoveryLimit - clock.GetElapsedTime(outage.Value)).Ticks)));
                    Volatile.Write(ref _smartSnapshot, SmartRelaySnapshot with { LowFrequency = low, RetryDelay = delay });
                    SmartState(PcpSmartRelayState.Backoff);
                    await Task.Delay(delay, clock, token).ConfigureAwait(false);
                    waveStart = clock.GetTimestamp(); waveAttempts = 0; bootstrapTried = false; visited.Clear();
                    continue;
                }
                if (target.RedirectEndpoint is { } selected) visited.Add(new(selected.Address, selected.Port));
                else bootstrapTried = true;
                waveAttempts++;
                attempts = (int)Math.Min(int.MaxValue, (long)attempts + 1);
                Volatile.Write(ref _smartSnapshot, SmartRelaySnapshot with { AttemptCount = attempts, WaveAttemptCount = waveAttempts, RetryDelay = null, LowFrequency = low });
                using var budget = new CancellationTokenSource(remaining, clock);
                using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, budget.Token);
                var budgetToken = attemptCancellation.Token;
                var acquired = false;
                void ReleaseSlot() { if (acquired) { acquired = false; PcpSmartRelayAttemptLimit.Slots.Release(); } }
                AttemptResult? attempt = null;
                try
                {
                    await PcpSmartRelayAttemptLimit.Slots.WaitAsync(budgetToken).ConfigureAwait(false);
                    acquired = true;
                    token.ThrowIfCancellationRequested();
                    if (_peers?.IsActive == false || RecoveryOwnerIsCurrent?.Invoke() == false)
                    { RequestStop(); token.ThrowIfCancellationRequested(); }
                    SmartState(PcpSmartRelayState.Connecting);
                    _smartMedia.BeginAttempt();
                    attempt = await RunAttemptAsync(target.Host, target.Port, target.RedirectEndpoint,
                        initialPosition, consumer, progress, token, budgetToken, endpoint =>
                        {
                            current = target.RedirectEndpoint is { } logical ? new(logical.Address, logical.Port) : endpoint;
                            budget.CancelAfter(Timeout.InfiniteTimeSpan); ReleaseSlot();
                        }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (budget.IsCancellationRequested && !token.IsCancellationRequested)
                { /* The same wave budget includes waiting, connect and HELO. */ }
                finally { ReleaseSlot(); }
                current = target.RedirectEndpoint is { } directed ? new(directed.Address, directed.Port) : attempt?.ConnectedEndpoint;
                if (current is not null) { visited.Add(current); _peers?.RecordConnectionResult(current, succeeded: false); }
                if (attempt?.Handshake is { } handshake && handshake.Outcome != PcpCompatibilityOutcome.Accepted &&
                    handshake.Outcome != PcpCompatibilityOutcome.RelayFull &&
                    handshake.Quit?.Reason != PcpQuitReason.Unavailable && handshake.Quit?.Code is not (1008 or 1009))
                {
                    SmartState(PcpSmartRelayState.Failed);
                    RecordFault(CreateHandshakeFaultSnapshot(handshake.Outcome, attempts, Math.Max(0, attempts - 1)));
                    _negotiation.TrySetResult(new(PcpChannelSessionNegotiationOutcome.Rejected, handshake.HttpStatusCode, handshake.Outcome));
                    return CreateTerminalHandshakeResult(handshake, attempts, Math.Max(0, attempts - 1), progress, consumer);
                }
                if (attempt?.Stream?.Quit is { } quit && quit.Reason is not (PcpQuitReason.Unavailable or PcpQuitReason.Unspecified) &&
                    quit.Code is not (1008 or 1009))
                {
                    SmartState(quit.Reason == PcpQuitReason.Normal ? PcpSmartRelayState.Stopped : PcpSmartRelayState.Failed);
                    return CreateResult(PcpChannelSessionEndReason.QuitReceived, attempts, Math.Max(0, attempts - 1),
                        progress.LastHttpStatusCode, progress.ChannelMessageCount, consumer) with
                    { Quit = quit };
                }
                // OFFAIR/SHUTDOWN from one relay cannot prove global broadcast end.
                // They use the same bounded recovery policy; explicit identity errors stop above.
                if (_smartMedia.Stable)
                { waveStart = clock.GetTimestamp(); waveAttempts = 0; bootstrapTried = false; visited.Clear(); }
                outage ??= clock.GetTimestamp();
                current = null;
                Volatile.Write(ref _smartSnapshot, SmartRelaySnapshot with { ConsecutiveFailures = Math.Min(int.MaxValue - 1, SmartRelaySnapshot.ConsecutiveFailures) + 1 });
                SmartState(PcpSmartRelayState.Recovering);
                _stateAdapter.BeginAttempt();
                await _smartMedia.SuspendAsync(token).ConfigureAwait(false);
                _statusTracker.TransitionTo(ChannelSessionState.Reconnecting);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _negotiation.TrySetCanceled(token);
            SmartState(PcpSmartRelayState.Stopped);
            TransitionToStopped();
            if (caller.IsCancellationRequested && !_stopSource.IsCancellationRequested) throw;
            return CreateResult(PcpChannelSessionEndReason.Stopped, attempts, Math.Max(0, attempts - 1), progress.LastHttpStatusCode, progress.ChannelMessageCount, consumer);
        }
        catch (Exception exception)
        {
            SmartState(PcpSmartRelayState.Failed);
            _negotiation.TrySetException(exception);
            RecordFault(CreateFaultSnapshot(exception, InferFaultStage(_statusTracker.Snapshot.State), attempts, Math.Max(0, attempts - 1)));
            throw;
        }
        finally
        {
            _firstSmartMediaProgress.TrySetCanceled(CancellationToken.None);
            _smartMedia.ForgetDuplicateProof();
        }
    }

    private async Task<PcpChannelStreamResult> ReadSmartStreamAsync(PcpChannelStreamReader reader, CancellationToken token)
    {
        if (_options.SmartRelay is not { MediaStallTimeout: { } timeout } options || _smartMedia is null)
            return await reader.ReadToEndAsync(token).ConfigureAwait(false);
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);
        var stalled = false;
        var monitor = MonitorAsync();
        try { return await reader.ReadToEndAsync(reading.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stalled && !token.IsCancellationRequested)
        { throw new TimeoutException("Valid media stopped advancing despite PCP control activity."); }
        finally { reading.Cancel(); await monitor.ConfigureAwait(false); }
        async Task MonitorAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), options.TimeProvider, reading.Token).ConfigureAwait(false);
                    var idle = options.TimeProvider.GetElapsedTime(_smartMedia.LastProgress);
                    if (idle >= timeout) { stalled = true; reading.Cancel(); return; }
                    if (idle >= options.MediaDegradedAfter) SmartState(PcpSmartRelayState.Degraded);
                }
            }
            catch (OperationCanceledException) when (reading.IsCancellationRequested) { }
        }
    }
}
