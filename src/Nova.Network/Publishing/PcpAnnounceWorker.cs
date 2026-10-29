using System.Threading.Channels;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>One immutable generation, one connection, one serialized writer. Never registered in production composition.</summary>
internal sealed class PcpAnnounceWorker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly HashSet<Task> _pending = [];
    private readonly PcpAnnounceIdentity _identity;
    private readonly PcpAnnounceEndpoint _endpoint;
    private readonly PcpAnnounceAccess _access;
    private readonly IPcpAnnounceConnector _connector;
    private readonly PcpAnnounceOptions _options;
    private readonly TimeProvider _time;
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

    internal PcpAnnounceWorker(PcpAnnounceIdentity identity, PcpAnnounceEndpoint endpoint,
        PcpAnnounceDocument document, PcpAnnounceAccess access, IPcpAnnounceConnector connector,
        PcpAnnounceOptions? options = null, TimeProvider? timeProvider = null)
    {
        _identity = identity; _endpoint = endpoint; _initial = document; _latest = document; _access = access; _connector = connector;
        _options = options ?? new(); _options.Validate(); _time = timeProvider ?? TimeProvider.System;
    }
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
            SetState(PcpAnnounceState.Connecting);
            await BoundedAsync(ConnectAsync(destination), _options.ConnectTimeout).ConfigureAwait(false);
            SetState(PcpAnnounceState.Handshaking);
            await BoundedAsync(HandshakeAsync(), _options.HandshakeTimeout).ConfigureAwait(false);
            handshaken = true;
            SetState(PcpAnnounceState.Connected);
            if (!_stop.Task.IsCompleted)
            {
                await WriteAsync(CurrentDocument().Encode(_identity, true)).ConfigureAwait(false);
                _announced = true;
                SetState(PcpAnnounceState.AnnounceSent);
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
            // No queued update can run after this point. Notification writes share the same writer.
            if (handshaken && _stop.Task.IsCompleted && !_deadlineExceeded)
            {
                SetState(PcpAnnounceState.Stopping);
                if (_announced)
                {
                    try
                    {
                        await WriteAsync(CurrentDocument().Encode(_identity, false)).ConfigureAwait(false);
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
            "PeerCastNova/YPFoundation", _identity.SenderSessionId, _identity.BroadcastId, CurrentDocument().Port));
        await _connection!.WriteAsync(preamble.Concat(helo).ToArray(), _ioStop.Token).ConfigureAwait(false);
        var atom = await _connection.ReadAsync(_ioStop.Token).ConfigureAwait(false);
        ThrowIfQuit(atom);
        TrackerObservation = PcpAnnounceCodec.ParseOleh(atom);
    }

    private async Task UpdatesAsync()
    {
        using var timer = new PeriodicTimer(PcpAnnounceOptions.UpdateInterval, _time);
        var tick = Track(timer.WaitForNextTickAsync(_ioStop.Token).AsTask());
        var changed = Track(_updates.Reader.ReadAsync(_ioStop.Token).AsTask());
        var received = Track(_connection!.ReadAsync(_ioStop.Token));
        var receiveWindow = _time.GetTimestamp();
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
                // Bounded root/control atoms are not commands to connect elsewhere or change public capability.
                received = Track(_connection.ReadAsync(_ioStop.Token));
                await Task.Yield();
            }
            if (changed.IsCompleted || tick.IsCompleted)
            {
                if (changed.IsCompleted) { await changed.ConfigureAwait(false); changed = Track(_updates.Reader.ReadAsync(_ioStop.Token).AsTask()); }
                if (tick.IsCompleted) { await tick.ConfigureAwait(false); tick = Track(timer.WaitForNextTickAsync(_ioStop.Token).AsTask()); }
                if (_stop.Task.IsCompleted) break;
                await WriteAsync(CurrentDocument().Encode(_identity, true)).ConfigureAwait(false);
                SetState(PcpAnnounceState.UpdateSent);
            }
        }
    }

    private async Task WriteAsync(byte[] bytes) =>
        await BoundedAsync(_connection!.WriteAsync(bytes, _ioStop.Token), _options.WriteTimeout).ConfigureAwait(false);

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
