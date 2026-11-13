namespace PeerCast.Nova.Network.Transport;

/// <summary>Current, locally established PCP media transports. Not unique peers or physical wire bytes.</summary>
public sealed record PcpTransportSnapshot(Guid Scope, long Generation, int Connections, long SentBytes, long ReceivedBytes, bool IsValid, long Timestamp, long TimestampFrequency);

// No sockets, payloads, endpoints or timer are retained here. Each counter belongs to its transport.
public sealed class PcpTransportStatistics
{
    public static PcpTransportStatistics Shared { get; } = new();
    private readonly object gate = new();
    private readonly Dictionary<long, PcpTransportCounter> active = [];
    private readonly Guid scope = Guid.NewGuid();
    private long generation;
    private bool identityConflict;
    private readonly TimeProvider clock;
    internal PcpTransportStatistics(TimeProvider? clock = null) { this.clock = clock ?? TimeProvider.System; }
    internal PcpTransportCounter CreateCounter(long id) => new(this, id);

    public PcpTransportSnapshot Capture()
    {
        lock (gate)
        {
            long sent = 0, received = 0;
            var valid = !identityConflict;
            foreach (var counter in active.Values)
            {
                var tx = Interlocked.Read(ref counter.Sent);
                var rx = Interlocked.Read(ref counter.Received);
                if (Volatile.Read(ref counter.Invalid) != 0 || tx > long.MaxValue - sent || rx > long.MaxValue - received)
                { valid = false; continue; }
                sent += tx; received += rx;
            }
            return new(scope, generation, active.Count, sent, received, valid, clock.GetTimestamp(), clock.TimestampFrequency);
        }
    }

    internal IDisposable Establish(PcpTransportCounter counter)
    {
        lock (gate)
        {
            if (counter.Closed) return new Lease(null);
            if (counter.References == 0)
            {
                if (!active.TryAdd(counter.Id, counter)) { identityConflict = true; return new Lease(null); }
                generation = unchecked(generation + 1);
            }
            counter.References++;
            return new Lease(() => Release(counter));
        }
    }
    private void Release(PcpTransportCounter counter)
    {
        lock (gate)
        {
            if (counter.Closed || --counter.References != 0) return;
            active.Remove(counter.Id); generation = unchecked(generation + 1);
        }
    }
    internal void Close(PcpTransportCounter counter)
    {
        lock (gate)
        {
            if (counter.Closed) return;
            counter.Closed = true;
            if (counter.References > 0) { active.Remove(counter.Id); generation = unchecked(generation + 1); }
            counter.References = 0;
        }
    }
    internal bool IsEstablished(PcpTransportCounter counter)
    { lock (gate) { return !counter.Closed && counter.References > 0; } }
    private sealed class Lease(Action? release) : IDisposable
    {
        private Action? release = release;
        public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
    }
}

internal sealed class PcpTransportCounter(PcpTransportStatistics owner, long id)
{
    private PcpDownstreamTrafficMeter? _downstream;
    internal long Sent, Received;
    internal int Invalid, References;
    internal bool Closed;
    internal long Id => id;
    internal bool IsEstablished => owner.IsEstablished(this);
    internal IDisposable Establish() => owner.Establish(this);
    internal void Close() => owner.Close(this);
    internal void Invalidate() => Interlocked.Exchange(ref Invalid, 1);
    internal PcpDownstreamTrafficMeter ObserveDownstream(PcpDownstreamTrafficMeter scope)
    {
        var connection = scope.CreateChild();
        if (Interlocked.CompareExchange(ref _downstream, connection, null) is not null)
            throw new InvalidOperationException("A transport already belongs to a downstream observation.");
        connection.Observe();
        return connection;
    }
    internal void AddSent(long value)
    {
        Add(ref Sent, value);
        // Keep TX completion evidence independent of the existing combined
        // RX/TX validity flag (a cancelled read must not erase a later write).
        Volatile.Read(ref _downstream)?.RecordCompletedWrite(value);
    }
    internal void InvalidateSent()
    {
        Invalidate();
        Volatile.Read(ref _downstream)?.RecordUnconfirmedWrite();
    }
    internal void AddReceived(long value) => Add(ref Received, value);
    private void Add(ref long total, long value)
    {
        if (value < 0) { Invalidate(); return; }
        while (Volatile.Read(ref Invalid) == 0)
        {
            var before = Interlocked.Read(ref total);
            if (value > long.MaxValue - before) { Invalidate(); return; }
            if (Interlocked.CompareExchange(ref total, before + value, before) == before) return;
        }
    }
}
