using System.Threading.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>One immutable generation, one connection, one serialized writer. Publication is mediated by a connection-scoped capability in production.</summary>
internal sealed class PcpAnnounceWorker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _pending = [];
    private readonly PcpAnnounceIdentity _identity;
    private readonly Func<PublishingAnnounceRuntimeSnapshot>? _runtime;
    private long _lastRuntimeRevision = -1;
    private byte[]? _lastPublished;
    private readonly PublishingAnnounceCapability? _capability;
    internal PublishingTrackerReport? TrackerReport { get; private set; }
    private readonly PcpAnnounceEndpoint _endpoint;
    private readonly PcpAnnounceAccess _access;
    private readonly IPcpAnnounceConnector _connector;
    private readonly PcpAnnounceOptions _options;
    private readonly TimeProvider _time;
    private readonly Action<PcpPushRelayRequest>? _onPushRelayRequest;
    private readonly CancellationTokenSource _ioStop = new();
    private readonly TaskCompletionSource _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private PcpAnnounceDocument _latest;
    private readonly PcpAnnounceDocument _initial;
    private IPcpAnnounceConnection? _connection;
    private Task<PcpAnnounceResult>? _run;
    private Task<PcpAnnounceResult>? _termination;
    private PcpAnnounceState _state = PcpAnnounceState.Disabled;
    private PcpAnnounceStopReason? _reason;
    private bool _closing, _deadlineExceeded, _announced, _stopNotice, _quit;
    private bool _ioDisposed;
    private PcpAnnounceFailure _failure;
    private CancellationTokenRegistration _externalStop;
    private long _pushRequestsAccepted;
    private long _pushWindowTimestamp;
    private int _pushWindowCount;
    private TimeSpan _updateInterval = PcpAnnounceOptions.UpdateInterval;

    internal PcpAnnounceWorker(PcpAnnounceIdentity identity, PcpAnnounceEndpoint endpoint,
        PcpAnnounceDocument document, PcpAnnounceAccess access, IPcpAnnounceConnector connector,
        PcpAnnounceOptions? options = null, TimeProvider? timeProvider = null, PublishingAnnounceCapability? capability = null, Func<PublishingAnnounceRuntimeSnapshot>? runtime = null,
        Action<PcpPushRelayRequest>? onPushRelayRequest = null)
    {
        _runtime = runtime;
        _capability = capability;
        _onPushRelayRequest = onPushRelayRequest;
        if (capability is not null && capability.Identity != identity) throw new ArgumentException("Capability identity mismatch.", nameof(capability));
        _identity = identity; _endpoint = endpoint; _initial = document; _latest = document; _access = access; _connector = connector;
        _options = options ?? new(); _options.Validate(); _time = timeProvider ?? TimeProvider.System;
    }
    internal bool Announced => Volatile.Read(ref _announced);
    internal bool StopNoticeSent => Volatile.Read(ref _stopNotice);
    internal long PushRequestsAccepted => Volatile.Read(ref _pushRequestsAccepted);
    internal TimeSpan CurrentUpdateInterval { get { lock (_gate) return _updateInterval; } }
    internal PcpAnnounceIdentity Identity => _identity;
    internal PcpAnnounceState State { get { lock (_gate) return _state; } }
    internal PcpOlehInfo? TrackerObservation { get; private set; }
    internal Task<PcpAnnounceResult> Completion { get { lock (_gate) return _run ?? throw new InvalidOperationException("Start was not requested."); } }
    internal bool CleanupProven { get { lock (_gate) return !_deadlineExceeded && _run is { IsCompletedSuccessfully: true } && _run.Result.CleanupProven; } }

    internal void Start(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_run is not null || _reason.HasValue) throw new InvalidOperationException("Generation was already started or stopped.");
            _run = Task.Run(RunAsync);
            _externalStop = cancellationToken.Register(() => { _ = StopAsync(PcpAnnounceStopReason.Cancelled); });
            _ = _run.ContinueWith(_ => _externalStop.Unregister(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal bool Update(PcpAnnounceIdentity identity, PcpAnnounceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            if (identity != _identity || !_initial.HasSameEndpoint(document) || _reason.HasValue || _closing || _run is null || _run.IsCompleted) return false;
            _latest = document;
            _updates.Writer.TryWrite(true);
            return true;
        }
    }

    internal Task<PcpAnnounceResult> StopAsync(PcpAnnounceStopReason reason)
    {
        lock (_gate)
        {
            if (_termination is not null) return _termination;
            _reason = reason;
            _stop.TrySetResult();
            _run ??= Task.FromResult(Result(true));
            return _termination = StopAndObserveAsync();
        }
    }
    public async ValueTask DisposeAsync() => await StopAsync(PcpAnnounceStopReason.Dispose).ConfigureAwait(false);

    private async Task<PcpAnnounceResult> StopAndObserveAsync()
    {
        try { return await _run!.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            lock (_gate) { _deadlineExceeded = true; _state = PcpAnnounceState.CleanupIncomplete; }
            CancelIo();
            Abort();
            return Result(false);
        }
    }

    private async Task<PcpAnnounceResult> RunAsync()
    {
        var destination = _access.Resolve(_endpoint);
        if (!_access.Enabled) { SetState(PcpAnnounceState.Disabled); DisposeIo(); return Result(true); }
        if (destination is null) { SetState(PcpAnnounceState.NotReady); DisposeIo(); return Result(true); }
        var cleanup = false;
        var handshaken = false;
        try
        {
            if (_stop.Task.IsCompleted) return Result(true);
            if (_capability is not null && !_capability.IsValid) throw new InvalidDataException("Publication capability expired.");
            SetState(PcpAnnounceState.Connecting);
            await BoundedAsync(ConnectAsync(destination), _options.ConnectTimeout).ConfigureAwait(false);
            SetState(PcpAnnounceState.Handshaking);
            await BoundedAsync(HandshakeAsync(), _options.HandshakeTimeout).ConfigureAwait(false);
            handshaken = true;
            SetState(PcpAnnounceState.Connected);
            if (!_stop.Task.IsCompleted)
            {
                await PublishRuntimeAsync(periodic: false).ConfigureAwait(false);
                await UpdatesAsync().ConfigureAwait(false);
            }
        }
        catch (PcpAnnounceBannedException) { Fail(PcpAnnounceFailure.Banned); }
        catch (TrackerQuitException) { Fail(PcpAnnounceFailure.TrackerQuit); }
        catch (TimeoutException) { Fail(PcpAnnounceFailure.Timeout); }
        catch (OperationCanceledException) { Fail(PcpAnnounceFailure.Cancelled); }
        catch (InvalidDataException) { Fail(PcpAnnounceFailure.Protocol); }
        catch (PcpProtocolException) { Fail(PcpAnnounceFailure.Protocol); }
        catch (Exception) { Fail(handshaken ? PcpAnnounceFailure.Transport : PcpAnnounceFailure.Connection); }
        finally
        {
            lock (_gate) _closing = true;
            _capability?.Invalidate();
            // No queued update can run after this point. Notification writes share the same writer.
            if (handshaken && _stop.Task.IsCompleted && !_deadlineExceeded)
            {
                SetState(PcpAnnounceState.Stopping);
                if (_announced)
                {
                    try
                    {
                        await WriteAsync(CurrentDocument().Encode(_identity, false, ReadRuntime())).ConfigureAwait(false);
                        _stopNotice = true;
                        SetState(PcpAnnounceState.StopNoticeSent);
                    }
                    catch (Exception) { Fail(PcpAnnounceFailure.Transport); }
                }
                if (!_deadlineExceeded)
                {
                    try
                    {
                        await WriteAsync(PcpAnnounceDocument.EncodeAtom(PcpAnnounceCodec.CreateQuit())).ConfigureAwait(false);
                        _quit = true;
                    }
                    catch (Exception) { Fail(PcpAnnounceFailure.Transport); }
                }
            }
            cleanup = await CleanupAsync().ConfigureAwait(false);
            if (cleanup) DisposeIo();
            SetState(!cleanup || _deadlineExceeded ? PcpAnnounceState.CleanupIncomplete :
                _failure == PcpAnnounceFailure.None ? PcpAnnounceState.Stopped : PcpAnnounceState.Failed);
        }
        return Result(cleanup && !_deadlineExceeded);
    }

    private async Task ConnectAsync(System.Net.IPEndPoint endpoint)
    {
        var connection = await _connector.ConnectAsync(endpoint, _ioStop.Token).ConfigureAwait(false);
        bool closing;
        lock (_gate) { _connection = connection; closing = _closing || _deadlineExceeded; }
        if (closing)
        {
            connection.Abort();
            await connection.CloseAsync().ConfigureAwait(false);
            throw new OperationCanceledException();
        }
    }

    private async Task HandshakeAsync()
    {
        var preamble = new byte[PcpPreamble.Size];
        PcpPreamble.Write(preamble);
        var helo = PcpAnnounceDocument.EncodeAtom(PcpAnnounceCodec.CreateHelo(
            "PeerCastNova/YPFoundation", _identity.SenderSessionId, _identity.BroadcastId, CurrentDocument().Port, requestPing: _capability is not null));
        await _connection!.WriteAsync(preamble.Concat(helo).ToArray(), _ioStop.Token).ConfigureAwait(false);
        var atom = await _connection.ReadAsync(_ioStop.Token).ConfigureAwait(false);
        ThrowIfQuit(atom);
        TrackerObservation = PcpAnnounceCodec.ParseOleh(atom);
        if (_capability is not null)
        {
            TrackerReport = _capability.Evaluate(TrackerObservation, _time.GetUtcNow());
            if (!TrackerReport.CanAnnounce(_capability)) throw new InvalidDataException("Tracker did not report the requested public endpoint open.");
        }
    }

    private async Task UpdatesAsync()
    {
        using var timer = new PeriodicTimer(PcpAnnounceOptions.UpdateInterval, _time);
        var tick = Track(timer.WaitForNextTickAsync(_ioStop.Token).AsTask());
        var changed = Track(_updates.Reader.ReadAsync(_ioStop.Token).AsTask());
        var received = Track(ReadControlAsync());
        var receiveWindow = _time.GetTimestamp();
        _pushWindowTimestamp = receiveWindow;
        var count = 0;
        while (!_stop.Task.IsCompleted)
        {
            await Task.WhenAny(_stop.Task, received, changed, tick).ConfigureAwait(false);
            if (_stop.Task.IsCompleted) break;
            if (received.IsCompleted)
            {
                var atom = await received.ConfigureAwait(false);
                ThrowIfQuit(atom);
                if (_time.GetElapsedTime(receiveWindow) >= TimeSpan.FromSeconds(1)) { receiveWindow = _time.GetTimestamp(); count = 0; }
                if (++count > 32) throw new InvalidDataException("Tracker control rate exceeded.");
                if (_capability is not null && atom.Name == PcpKnownAtomNames.Oleh)
                {
                    var current = PcpAnnounceCodec.ParseOleh(atom);
                    if (TrackerObservation?.SessionId != current.SessionId || !_capability.Evaluate(current, _time.GetUtcNow()).CanAnnounce(_capability))
                        throw new InvalidDataException("Tracker observation changed.");
                }
                else if (atom.Name == PcpKnownAtomNames.Root &&
                    PcpAnnounceCodec.TryParseRootControl(atom, out var rootInfo) &&
                    rootInfo is not null)
                {
                    if (rootInfo.UpdateInterval is { } interval)
                    {
                        lock (_gate) _updateInterval = interval;
                        timer.Period = interval;
                    }

                    if (rootInfo.RequestImmediateUpdate && !_stop.Task.IsCompleted)
                    {
                        await PublishRuntimeAsync(periodic: true).ConfigureAwait(false);
                    }
                }
                else if ((atom.Name == PcpKnownAtomNames.Push || atom.Name == PcpKnownAtomNames.Cout) &&
                    PcpAnnounceCodec.TryParsePushRequest(atom, out var pushRequest) &&
                    pushRequest is not null)
                {
                    if (pushRequest.ChannelId == _identity.ChannelId &&
                        _access.AllowsPushTarget(pushRequest.Address))
                    {
                        if (_time.GetElapsedTime(_pushWindowTimestamp) >= TimeSpan.FromSeconds(10))
                        {
                            _pushWindowTimestamp = _time.GetTimestamp();
                            _pushWindowCount = 0;
                        }

                        if (++_pushWindowCount <= 8)
                        {
                            Interlocked.Increment(ref _pushRequestsAccepted);
                            try
                            {
                                _onPushRelayRequest?.Invoke(pushRequest);
                            }
                            catch
                            {
                                // Push 処理側の例外で Tracker との Announce 接続自体は切断しない。
                            }
                        }
                    }
                }
                received = Track(ReadControlAsync());
                await Task.Yield();
            }
            if (changed.IsCompleted || tick.IsCompleted)
            {
                if (changed.IsCompleted) { await changed.ConfigureAwait(false); changed = Track(_updates.Reader.ReadAsync(_ioStop.Token).AsTask()); }
                // Decide periodic publication from the same observation that
                // consumes the tick; it may arrive while metadata is rearmed.
                var periodic = tick.IsCompleted;
                if (periodic) { await tick.ConfigureAwait(false); tick = Track(timer.WaitForNextTickAsync(_ioStop.Token).AsTask()); }
                if (_stop.Task.IsCompleted) break;
                await PublishRuntimeAsync(periodic).ConfigureAwait(false);
            }
        }
    }

    internal void NotifyRuntimeChanged() => _updates.Writer.TryWrite(true);
    private PublishingAnnounceRuntimeSnapshot? ReadRuntime()
    {
        var snapshot = _runtime?.Invoke();
        if (snapshot is null) return null;
        if (snapshot.ChannelId.Value != _identity.ChannelId || snapshot.SessionId.Value != _identity.PublishingSessionId ||
            snapshot.Revision < _lastRuntimeRevision)
            throw new InvalidDataException("Stale HOST generation or revision.");
        _lastRuntimeRevision = snapshot.Revision;
        return snapshot;
    }
    private async Task PublishRuntimeAsync(bool periodic)
    {
        var snapshot = ReadRuntime();
        if (snapshot is not null)
        {
            if (!snapshot.CapabilityValid) throw new InvalidDataException("HOST capability expired.");
            // Initial and periodic HOST reflect current Receiving, including false before HEAD.
        }
        var bytes = CurrentDocument().Encode(_identity, true, snapshot);
        if (!periodic && _announced && _lastPublished is not null && bytes.AsSpan().SequenceEqual(_lastPublished)) return;
        await WriteAsync(bytes).ConfigureAwait(false);
        var initial = !_announced;
        _announced = true;
        _lastPublished = bytes;
        SetState(initial ? PcpAnnounceState.AnnounceSent : PcpAnnounceState.UpdateSent);
    }
    private async Task<PcpAtom> ReadControlAsync()
    {
        var read = Track(_connection!.ReadAsync(_ioStop.Token));
        return await read.ConfigureAwait(false);
    }
    private async Task WriteAsync(byte[] bytes)
    {
        if (!_closing && _capability is not null && TrackerReport?.CanAnnounce(_capability) != true)
            throw new InvalidDataException("Publication capability expired.");
        await BoundedAsync(_connection!.WriteAsync(bytes, _ioStop.Token), _options.WriteTimeout).ConfigureAwait(false);
    }

    private async Task BoundedAsync(Task task, TimeSpan timeout)
    {
        _ = Track(task);
        try { await task.WaitAsync(timeout, _ioStop.Token).ConfigureAwait(false); }
        catch (Exception) { Abort(); throw; }
    }

    private T Track<T>(T task) where T : Task
    {
        lock (_gate) _pending.Add(task);
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (_gate) _pending.Remove(completed);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private async Task<bool> CleanupAsync()
    {
        CancelIo();
        Abort();
        try
        {
            var close = _connection is { } connection ? Track(connection.CloseAsync()) : Task.CompletedTask;
            Task[] pending;
            lock (_gate) pending = _pending.ToArray();
            var joined = Task.WhenAll(pending);
            try { await joined.WaitAsync(_options.ShutdownTimeout).ConfigureAwait(false); }
            catch (Exception) when (joined.IsCompleted) { /* Completion, even faulted, proves no outstanding operation. */ }
            lock (_gate) return close.IsCompletedSuccessfully && _pending.All(t => t.IsCompleted);
        }
        catch (Exception) { return false; }
    }

    private void Abort() { try { _connection?.Abort(); } catch (Exception) { /* Cleanup evidence remains authoritative. */ } }
    private void CancelIo() { lock (_gate) { if (!_ioDisposed) _ioStop.Cancel(); } }
    private void DisposeIo() { lock (_gate) { if (!_ioDisposed) { _ioStop.Dispose(); _ioDisposed = true; } } }
    private PcpAnnounceDocument CurrentDocument() { lock (_gate) return _latest; }
    private void SetState(PcpAnnounceState state) { lock (_gate) { if (!_deadlineExceeded) _state = state; } }
    private void Fail(PcpAnnounceFailure failure) { lock (_gate) { if (_failure == PcpAnnounceFailure.None) _failure = failure; } SetState(PcpAnnounceState.Failed); }
    private PcpAnnounceResult Result(bool cleanup) => new(_identity, _failure, _announced, _stopNotice, _quit, cleanup, _reason);
    private static void ThrowIfQuit(PcpAtom atom)
    {
        if (atom.Name != PcpKnownAtomNames.Quit) return;
        if (atom.IsParent) throw new InvalidDataException("Invalid tracker QUIT.");
        PcpWireValueCodec.DecodeUInt32(atom.Content.Span);
        throw new TrackerQuitException();
    }
    private sealed class TrackerQuitException : IOException;
}
