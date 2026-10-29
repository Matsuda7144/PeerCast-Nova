using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Channels;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Publishing;
using PeerCast.Nova.Network.Transport;
using PeerCast.Nova.Protocol.Pcp;

namespace PeerCast.Nova.Network.Tests;

internal static class AnnounceTestData
{
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    internal static readonly IPEndPoint Target = new(IPAddress.Loopback, 17144);
    internal static PcpAnnounceEndpoint Endpoint => PcpAnnounceEndpoint.Parse("pcp://127.0.0.1:17144");
    internal static PcpAnnounceAccess Access => PcpAnnounceAccess.ForLoopbackFixture(Target);
    internal static PcpAnnounceDocument Document(string name = "Motion + tone", int port = 7144) => new(
        new ChannelMetadataSnapshot(name, "test", "公開する説明", "https://example.com/", "FLV", 128,
            comment: "公開コメント", streamExtension: "flv", track: new("track", "genre", "album", "artist")),
        new IPEndPoint(IPAddress.Parse("192.0.2.42"), port), 1, 2, 10, true);
    internal static PcpAtom Decode(byte[] bytes)
    {
        var reader = new SequenceReader<byte>(new ReadOnlySequence<byte>(bytes));
        Assert.True(PcpAtomTreeCodec.TryRead(ref reader, PcpAnnounceOptions.Limits, out var atom, out _));
        Assert.Equal(bytes.Length, reader.Consumed);
        return atom;
    }
    internal static PcpAtom Field(PcpAtom atom, string name) => Assert.Single(atom.Children, c => c.Name == PcpAtomName.FromAscii(name));
    internal static string Name(PcpAtom bcst) => PcpWireValueCodec.DecodeNullTerminatedUtf8(Field(Field(Field(bcst, "chan"), "info"), "name").Content.Span);
    internal static PcpAnnounceWorker Start(PcpAnnounceOwner owner, long generation = 1, PcpAnnounceAccess? access = null,
        PcpAnnounceEndpoint? endpoint = null, CancellationToken cancellationToken = default)
    {
        var session = Guid.NewGuid();
        return owner.Start(generation, Guid.NewGuid(), session, session, endpoint ?? Endpoint, Document(), access ?? Access, cancellationToken: cancellationToken);
    }
}

internal sealed class AnnounceFakeConnector : IPcpAnnounceConnector
{
    internal AnnounceFakeConnection Connection { get; } = new();
    internal int Calls { get; private set; }
    internal Func<CancellationToken, Task<IPcpAnnounceConnection>>? Connect { get; set; }
    public Task<IPcpAnnounceConnection> ConnectAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        Calls++;
        Assert.True(IPAddress.IsLoopback(endpoint.Address));
        return Connect?.Invoke(cancellationToken) ?? Task.FromResult<IPcpAnnounceConnection>(Connection);
    }
}

internal sealed class AnnounceFakeConnection : IPcpAnnounceConnection
{
    private readonly Channel<PcpAtom> _incoming = Channel.CreateUnbounded<PcpAtom>();
    private readonly Channel<byte[]> _written = Channel.CreateUnbounded<byte[]>();
    private readonly CancellationTokenSource _abort = new();
    private int _writes, _active;
    internal int BlockWriteAt { get; set; } = -1;
    internal TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CloseRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CloseObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool DelayClose { get; set; }
    internal bool ThrowOnWrite { get; set; }
    internal bool IgnoreIdleReadCancellation { get; set; }
    internal TaskCompletionSource IdleReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource IdleReadFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<PcpAtom> ReadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _readCount, _pendingReads;
    internal int PendingReads => Volatile.Read(ref _pendingReads);
    internal bool Aborted { get; private set; }
    internal bool Closed { get; private set; }
    internal int MaxActiveWrites { get; private set; }
    internal AnnounceFakeConnection() => Receive(PcpHeloCodec.CreateOleh("fake tracker", Guid.NewGuid()));
    internal void Receive(PcpAtom atom) => _incoming.Writer.TryWrite(atom);
    internal void ReplaceHandshake(PcpAtom atom) { _incoming.Reader.TryRead(out _); Receive(atom); }
    internal void Disconnect() => _incoming.Writer.TryComplete(new IOException("fixture disconnected"));
    internal Task<byte[]> NextWriteAsync() => _written.Reader.ReadAsync().AsTask().WaitAsync(AnnounceTestData.Deadline);
    internal bool TryWrite(out byte[]? bytes) => _written.Reader.TryRead(out bytes);
    public async Task WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _active);
        MaxActiveWrites = Math.Max(MaxActiveWrites, active);
        try
        {
            if (Interlocked.Increment(ref _writes) == BlockWriteAt)
            {
                Blocked.TrySetResult();
                await Release.Task; // Deliberately ignores cancellation and Abort.
            }
            if (ThrowOnWrite) throw new IOException("fixture write failure");
            _written.Writer.TryWrite(bytes.ToArray());
        }
        finally { Interlocked.Decrement(ref _active); }
    }
    public async Task<PcpAtom> ReadAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _abort.Token);
        Interlocked.Increment(ref _pendingReads);
        try
        {
            if (Interlocked.Increment(ref _readCount) > 1)
            {
                IdleReadStarted.TrySetResult();
                if (IgnoreIdleReadCancellation) return await ReadRelease.Task;
            }
            return await _incoming.Reader.ReadAsync(linked.Token);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingReads);
            if (Volatile.Read(ref _readCount) > 1) IdleReadFinished.TrySetResult();
        }
    }
    public void Abort() { Aborted = true; _abort.Cancel(); }
    public async Task CloseAsync()
    {
        Abort();
        if (DelayClose) await CloseRelease.Task;
        Closed = true;
        CloseObserved.TrySetResult();
    }
    public ValueTask DisposeAsync() => new(CloseAsync());
}

internal sealed class AnnounceLoopbackTracker : IAsyncDisposable
{
    private readonly TcpTransportListener _listener = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly CancellationTokenSource _deadline = new(AnnounceTestData.Deadline);
    private TcpTransportConnection? _peer;
    private PipeReader? _pipe;
    private PcpAtomPipeMessageReader? _reader;
    internal AnnounceLoopbackTracker() => _listener.Start();
    internal IPEndPoint Endpoint => _listener.LocalEndPoint;
    internal async Task<PcpAtom> AcceptHeloAsync()
    {
        _peer = await _listener.AcceptAsync(_deadline.Token);
        var preamble = new byte[12];
        await _peer.TransportStream.ReadExactlyAsync(preamble, _deadline.Token);
        Assert.Equal(new byte[] { 0x70, 0x63, 0x70, 0x0a, 4, 0, 0, 0, 1, 0, 0, 0 }, preamble);
        _pipe = PipeReader.Create(_peer.TransportStream, new StreamPipeReaderOptions(leaveOpen: true));
        _reader = new(_pipe, PcpAnnounceOptions.Limits, PcpAnnounceOptions.MaximumControlBytes);
        var helo = await ReadAsync();
        Assert.Equal(PcpKnownAtomNames.Helo, helo.Name); // A raw PCP stream, never an HTTP GET.
        return helo;
    }
    internal Task<PcpAtom> ReadAsync() => _reader!.ReadNextAsync(_deadline.Token).AsTask();
    internal Task WriteAsync(PcpAtom atom) => _peer!.WriteAsync(PcpAnnounceDocument.EncodeAtom(atom), _deadline.Token).AsTask();
    internal Task RawAsync(byte[] bytes) => _peer!.WriteAsync(bytes, _deadline.Token).AsTask();
    public async ValueTask DisposeAsync()
    {
        _deadline.Cancel();
        if (_peer is not null) await _peer.DisposeAsync();
        if (_reader is not null) await _reader.DisposeAsync();
        if (_pipe is not null) await _pipe.CompleteAsync();
        await _listener.DisposeAsync();
        _deadline.Dispose();
    }
}

internal sealed class AnnounceManualTimeProvider : TimeProvider, IDisposable
{
    private long _ticks;
    private readonly List<ManualTimer> _timers = [];
    private readonly object _gate = new();
    internal int TimerCount { get { lock (_gate) return _timers.Count; } }
    internal int ActiveTimerCount { get { lock (_gate) return _timers.Count(t => !t.Disposed); } }
    internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_ticks);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            TimerCreated.TrySetResult();
            return timer;
        }
    }
    internal void Advance(TimeSpan amount)
    {
        var now = Interlocked.Add(ref _ticks, amount.Ticks);
        ManualTimer[] timers;
        lock (_gate) timers = _timers.ToArray();
        foreach (var timer in timers) timer.Fire(now);
    }
    public void Dispose() { lock (_gate) foreach (var timer in _timers) timer.Dispose(); }
    private sealed class ManualTimer(AnnounceManualTimeProvider clock, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        private readonly object _gate = new();
        private bool _disposed;
        private long _due = Due(clock.GetTimestamp(), dueTime);
        private TimeSpan _period = period;
        internal bool Disposed { get { lock (_gate) return _disposed; } }
        public bool Change(TimeSpan nextDue, TimeSpan newPeriod)
        { lock (_gate) { _due = Due(clock.GetTimestamp(), nextDue); _period = newPeriod; return !_disposed; } }
        public void Dispose() { lock (_gate) _disposed = true; }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        internal void Fire(long now)
        {
            lock (_gate)
            {
                if (_disposed || now < _due) return;
                _due = _period <= TimeSpan.Zero ? long.MaxValue : now + _period.Ticks;
            }
            callback(state);
        }
        private static long Due(long now, TimeSpan time) => time == Timeout.InfiniteTimeSpan ? long.MaxValue : now + time.Ticks;
    }
}
