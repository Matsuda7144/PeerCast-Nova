namespace PeerCast.Nova.Network.Transport;

/// <summary>
/// Bytes in successful application stream writes, not TCP acknowledgements or
/// NIC traffic. Failed writes may have sent an unknowable partial buffer.
/// Rate averages up to five completed one-second monotonic buckets.
/// </summary>
public sealed record PcpDownstreamTrafficSnapshot(long CompletedWriteBytes,
    double? KilobitsPerSecond, bool HasObservation, long UnconfirmedWrites, bool Overflowed)
{
    public bool CumulativeComplete => UnconfirmedWrites == 0 && !Overflowed;
}

/// <summary>
/// Bounded scalar aggregation. Children share the same clock and gate; parents
/// retain no connections or children. Closing a connection never subtracts its
/// history. No polling, timer, packet copy or network ownership is introduced.
/// </summary>
public sealed class PcpDownstreamTrafficMeter
{
    private const int WindowSeconds = 5;
    private readonly object _gate;
    private readonly TimeProvider _clock;
    private readonly PcpDownstreamTrafficMeter? _parent;
    private readonly Bucket[] _buckets = new Bucket[WindowSeconds + 1];
    private long _bytes, _uncertain, _started;
    private bool _observed, _overflowed, _clockInvalid;

    public PcpDownstreamTrafficMeter(TimeProvider? clock = null)
    {
        _gate = new();
        _clock = clock ?? TimeProvider.System;
    }

    private PcpDownstreamTrafficMeter(PcpDownstreamTrafficMeter parent)
    {
        _parent = parent;
        _gate = parent._gate;
        _clock = parent._clock;
    }

    internal PcpDownstreamTrafficMeter CreateChild() => new(this);

    internal void Observe()
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            for (var meter = this; meter is not null; meter = meter._parent) meter.Start(now);
        }
    }

    internal void RecordCompletedWrite(long bytes) => Record(bytes, uncertain: bytes < 0);
    internal void RecordUnconfirmedWrite() => Record(0, uncertain: true);

    private void Record(long bytes, bool uncertain)
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            for (var meter = this; meter is not null; meter = meter._parent)
            {
                meter.Start(now);
                var index = meter.Index(now);
                if (uncertain) meter.Add(ref meter._uncertain, 1);
                else meter.Add(ref meter._bytes, bytes);
                if (index is null) continue;
                ref var bucket = ref meter._buckets[index.Value % meter._buckets.Length];
                if (!bucket.Used || bucket.Index != index.Value) bucket = new() { Used = true, Index = index.Value };
                if (uncertain) bucket.Uncertain = true;
                else meter.Add(ref bucket.Bytes, bytes);
            }
        }
    }

    private void Start(long now)
    {
        if (_observed) return;
        _observed = true;
        _started = now;
    }

    private long? Index(long now)
    {
        var elapsed = _clock.GetElapsedTime(_started, now);
        if (elapsed < TimeSpan.Zero) _clockInvalid = true;
        return _clockInvalid ? null : elapsed.Ticks / TimeSpan.TicksPerSecond;
    }

    private void Add(ref long target, long value)
    {
        if (value > long.MaxValue - target) { target = long.MaxValue; _overflowed = true; }
        else target += value;
    }

    public PcpDownstreamTrafficSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                double? rate = null;
                if (_observed && !_overflowed && Index(_clock.GetTimestamp()) is { } index && index > 0)
                {
                    var first = Math.Max(0, index - WindowSeconds);
                    var complete = true;
                    double bytes = 0;
                    foreach (var bucket in _buckets)
                    {
                        if (!bucket.Used || bucket.Index < first || bucket.Index >= index) continue;
                        bytes += bucket.Bytes;
                        complete &= !bucket.Uncertain;
                    }
                    if (complete) rate = bytes * 8d / (1000d * Math.Min(index, WindowSeconds));
                }
                return new(_bytes, rate, _observed, _uncertain, _overflowed);
            }
        }
    }

    private struct Bucket
    {
        public bool Used, Uncertain;
        public long Index, Bytes;
    }
}
