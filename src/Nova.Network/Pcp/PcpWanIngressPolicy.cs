using System.Net;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>WAN control admission, separate from media relay reservations. These are Nova safety limits, not PCP constants.</summary>
public sealed record PcpWanIngressOptions
{
    public int MaxPendingHandshakes { get; init; } = 16;
    public int MaxPendingPerAddress { get; init; } = 2;
    public int MaxConcurrentPings { get; init; } = 4;
    public int PingResponsesPerSecond { get; init; } = 8;
    public TimeSpan IdentificationTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan PingTimeout { get; init; } = TimeSpan.FromSeconds(3);
    internal Func<IPAddress?, bool> SourceAllowed { get; init; } = PublishingIpv4.IsSourceAddress;
    internal TimeProvider Clock { get; init; } = TimeProvider.System;
    internal void Validate()
    {
        if (MaxPendingHandshakes is < 1 or > 128 || MaxPendingPerAddress < 1 || MaxPendingPerAddress > MaxPendingHandshakes ||
            MaxConcurrentPings < 1 || MaxConcurrentPings > MaxPendingHandshakes || PingResponsesPerSecond is < 1 or > 128 ||
            IdentificationTimeout <= TimeSpan.Zero || IdentificationTimeout > TimeSpan.FromSeconds(10) ||
            PingTimeout <= TimeSpan.Zero || PingTimeout > TimeSpan.FromSeconds(10)) throw new ArgumentException("Invalid WAN ingress limits.");
    }
}
public sealed record PcpWanIngressSnapshot(int PendingHandshakes, int ActivePings, long PingResponses, long RejectedConnections);

internal sealed class PcpWanIngressPolicy(PcpWanIngressOptions options)
{
    private readonly object _gate = new();
    private readonly Dictionary<IPAddress, int> _pendingByAddress = [];
    private int _pending, _pings;
    private readonly Queue<long> _pingAttempts = new();
    private long _responses, _rejected;
    private bool _closed;
    internal PcpWanIngressSnapshot Snapshot { get { lock (_gate) return new(_pending, _pings, _responses, _rejected); } }
    internal void Close() { lock (_gate) _closed = true; }
    internal Lease? TryAccept(IPAddress? address)
    {
        lock (_gate)
        {
            bool allowed;
            try { allowed = address is not null && options.SourceAllowed(address); }
            catch { allowed = false; }
            if (!allowed || _closed || _pending >= options.MaxPendingHandshakes || _pendingByAddress.GetValueOrDefault(address!) >= options.MaxPendingPerAddress)
            { _rejected++; return null; }
            _pending++;
            _pendingByAddress[address!] = _pendingByAddress.GetValueOrDefault(address!) + 1;
            return new(this, address!, options.Clock.GetTimestamp());
        }
    }
    internal sealed class Lease(PcpWanIngressPolicy owner, IPAddress address, long accepted) : IDisposable
    {
        private bool _pending = true, _ping, _released;
        internal TimeProvider Clock => owner.Options.Clock;
        internal TimeSpan Remaining(TimeSpan budget)
        {
            var value = budget - Clock.GetElapsedTime(accepted);
            if (value <= TimeSpan.Zero) throw new OperationCanceledException("WAN handshake deadline expired.");
            return value;
        }
        internal TimeSpan IdentificationTimeout => owner.Options.IdentificationTimeout;
        internal TimeSpan PingTimeout => owner.Options.PingTimeout;
        internal bool TryBeginPing()
        {
            lock (owner._gate)
            {
                if (owner._closed || _released || _ping || owner._pings >= owner.Options.MaxConcurrentPings) return false;
                while (owner._pingAttempts.TryPeek(out var oldest) && Clock.GetElapsedTime(oldest) >= TimeSpan.FromSeconds(1))
                    owner._pingAttempts.Dequeue();
                if (owner._pingAttempts.Count >= owner.Options.PingResponsesPerSecond) return false;
                owner._pingAttempts.Enqueue(Clock.GetTimestamp()); owner._pings++; _ping = true;
                return true;
            }
        }
        internal void Handshaken(bool pingResponse = false)
        {
            lock (owner._gate)
            {
                ReleasePending();
                if (pingResponse) owner._responses++;
            }
        }
        private void ReleasePending()
        {
            if (!_pending) return;
            _pending = false; owner._pending--;
            var count = owner._pendingByAddress[address] - 1;
            if (count == 0) owner._pendingByAddress.Remove(address); else owner._pendingByAddress[address] = count;
        }
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_released) return;
                _released = true; ReleasePending();
                if (_ping) owner._pings--;
            }
        }
    }
    private PcpWanIngressOptions Options => options;
}
