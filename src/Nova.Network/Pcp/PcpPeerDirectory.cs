using System.Collections.Immutable;

namespace PeerCast.Nova.Network.Pcp;

internal enum PcpPeerSourceKind { UpstreamHandshake, UpstreamStream, DownstreamControl }
internal readonly record struct PcpPeerSource(PcpPeerSourceKind Kind, long ConnectionId);
internal sealed record PcpPeerObservation(PcpRelayHostCandidate Host, long ObservedAt,
    PcpPeerSource Source, int FailureCount, bool HasSucceeded, long? LastFailure, long? LastMediaReceived = null);
internal sealed record PcpRelayPeerIdentity(Guid? SessionId, PcpRelayHostEndpoint Endpoint,
    PcpRelayHostEndpoint? ListeningEndpoint = null);

/// <summary>One channel-owner generation; no sockets, timers, media or external callbacks.</summary>
internal sealed class PcpPeerDirectory : IDisposable
{
    internal const int MaximumCandidates = 256;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(120);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    private readonly List<PcpPeerObservation> _entries = [];
    private readonly Dictionary<long, PcpRelayPeerIdentity> _downstreams = [];
    private PcpRelayHostEndpoint? _localEndpoint;
    private bool _retired;
    private long _window;
    private int _updates;
    private int _untrackedDownstreams;

    internal PcpPeerDirectory(Guid channelId, Guid localSessionId, TimeProvider? clock = null)
    {
        if (channelId == Guid.Empty || localSessionId == Guid.Empty)
            throw new ArgumentException("Channel and owner identities must be nonempty.");
        ChannelId = channelId;
        LocalSessionId = localSessionId;
        _clock = clock ?? TimeProvider.System;
        _window = _clock.GetTimestamp();
    }

    internal Guid ChannelId { get; }
    internal Guid LocalSessionId { get; }
    internal bool IsActive { get { lock (_gate) return !_retired; } }
    internal int Count { get { lock (_gate) { Prune(_clock.GetTimestamp()); return _entries.Count; } } }

    internal void SetLocalEndpoint(PcpRelayHostEndpoint endpoint)
    {
        lock (_gate) { if (!_retired) _localEndpoint = endpoint; }
    }

    internal bool Observe(PcpRelayHostCandidate host, PcpPeerSource source)
    {
        lock (_gate)
        {
            if (_retired) return false;
            var now = _clock.GetTimestamp();
            if (_clock.GetElapsedTime(_window, now) >= TimeSpan.FromSeconds(1))
            { _window = now; _updates = 0; }
            // Bound work as well as retained state, including rejected observations.
            if (_updates++ >= 64) { _updates = 64; return false; }
            Prune(now);
            if (!Eligible(host) || Excluded(host, null)) return false;
            var matches = _entries.Where(e => e.Host.GlobalEndpoint == host.GlobalEndpoint ||
                host.PeerSessionId is { } sid && e.Host.PeerSessionId == sid).ToArray();
            var failures = matches.Select(e => e.FailureCount).DefaultIfEmpty().Max();
            var succeeded = matches.Any(e => e.HasSucceeded);
            var lastFailure = matches.Select(e => e.LastFailure).Max();
            var lastMedia = matches.Select(e => e.LastMediaReceived).Max();
            foreach (var entry in matches) _entries.Remove(entry);
            // A SID-less refresh must not erase a previously learned wire identity.
            if (host.PeerSessionId is null)
                host = host with { PeerSessionId = matches.Select(e => e.Host.PeerSessionId).FirstOrDefault(s => s is not null) };
            if (_entries.Count == MaximumCandidates)
                _entries.Remove(_entries.MinBy(e => e.ObservedAt)!);
            _entries.Add(new(host, now, source, failures, succeeded, lastFailure, lastMedia));
            return true;
        }
    }

    internal void RecordConnectionResult(PcpRelayHostEndpoint endpoint, bool succeeded)
    {
        lock (_gate)
        {
            if (_retired) return;
            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.Host.GlobalEndpoint != endpoint) continue;
                _entries[i] = entry with
                {
                    FailureCount = succeeded ? entry.FailureCount : Math.Min(64, entry.FailureCount + 1),
                    HasSucceeded = entry.HasSucceeded || succeeded,
                    LastFailure = succeeded ? null : _clock.GetTimestamp(),
                };
            }
        }
    }

    internal void RecordMediaProgress(PcpRelayHostEndpoint endpoint)
    {
        lock (_gate)
        {
            if (_retired) return;
            for (var i = 0; i < _entries.Count; i++)
                if (_entries[i].Host.GlobalEndpoint == endpoint)
                    _entries[i] = _entries[i] with { LastMediaReceived = _clock.GetTimestamp() };
        }
    }

    internal ImmutableArray<PcpPeerObservation> Snapshot(PcpRelayPeerIdentity? requester = null)
    {
        lock (_gate)
        {
            var now = _clock.GetTimestamp();
            Prune(now);
            if (_retired || _untrackedDownstreams != 0) return [];
            return _entries.Where(e => Eligible(e.Host) && !Excluded(e.Host, requester) &&
                (e.LastFailure is not { } failed || _clock.GetElapsedTime(failed, now) >= TimeSpan.FromSeconds(30)))
                .ToImmutableArray();
        }
    }

    internal bool IsKnownLoop(PcpRelayHostCandidate host)
    {
        lock (_gate) return _retired || Excluded(host, null);
    }

    internal IDisposable TrackDownstream(long connectionId, PcpRelayPeerIdentity peer)
    {
        lock (_gate)
        {
            if (_retired) return new Lease(null);
            // Actual sessions are already bounded by the listener. Overflow
            // knowledge fails closed instead of dropping a loop exclusion.
            if (_downstreams.Count >= MaximumCandidates)
            {
                _untrackedDownstreams++;
                return new Lease(() => { lock (_gate) _untrackedDownstreams--; });
            }
            if (!_downstreams.TryAdd(connectionId, peer))
                throw new InvalidOperationException("A downstream connection is already tracked.");
            return new Lease(() =>
            {
                lock (_gate)
                    if (_downstreams.TryGetValue(connectionId, out var current) && ReferenceEquals(current, peer))
                        _downstreams.Remove(connectionId);
            });
        }
    }

    private bool Eligible(PcpRelayHostCandidate host) =>
        host.ChannelId == ChannelId && host.PeerSessionId != Guid.Empty &&
        host.GlobalEndpoint is { } endpoint && PcpRelayRedirectPolicy.Evaluate(
            new(endpoint, PcpRelayRedirectEndpointRole.Global), 0).IsEligible;

    private bool Excluded(PcpRelayHostCandidate host, PcpRelayPeerIdentity? requester) =>
        host.PeerSessionId == LocalSessionId || host.GlobalEndpoint == _localEndpoint ||
        _localEndpoint is not null && host.UpstreamEndpoint == _localEndpoint ||
        requester is not null && Matches(host, requester) ||
        _downstreams.Values.Any(peer => Matches(host, peer));

    private static bool Matches(PcpRelayHostCandidate host, PcpRelayPeerIdentity peer) =>
        peer.SessionId is { } sid && sid != Guid.Empty && host.PeerSessionId == sid ||
        host.GlobalEndpoint == peer.Endpoint ||
        peer.ListeningEndpoint is not null && host.GlobalEndpoint == peer.ListeningEndpoint;

    private void Prune(long now) =>
        _entries.RemoveAll(e => _clock.GetElapsedTime(e.ObservedAt, now) >= Lifetime);

    public void Dispose()
    {
        lock (_gate) { _retired = true; _entries.Clear(); _downstreams.Clear(); }
    }

    private sealed class Lease(Action? release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
