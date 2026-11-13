using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

/// <summary>
/// Application-owned identities and admission leases, never socket ownership.
/// Registration requires an authoritative role and stable owner session.
/// Routed bindings use the exact fan-out identity, not only ChannelId.
/// </summary>
internal sealed class GenesisDownstreamRelayOwnerRegistry : IDisposable
{
    private readonly object _gate = new();
    private readonly RelayAdmissionPolicy _policy;
    private readonly Dictionary<ChannelId, Owner> _owners = [];
    private readonly Dictionary<long, Reservation> _connections = [];
    private readonly ConditionalWeakTable<PcpRelayFanout, Owner> _bindings = new();
    private bool _disposed;

    public GenesisDownstreamRelayOwnerRegistry(RelayAdmissionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        _policy = policy;
    }

    public int ActiveRelayCount
    {
        get { lock (_gate) { return _connections.Count; } }
    }

    internal bool ContainsConnection(long connectionId)
    {
        lock (_gate) { return _connections.ContainsKey(connectionId); }
    }

    public Owner Register(ChannelId channelId, ChannelSessionId sessionId,
        RelayChannelKind kind, PcpRelayFanout? fanout = null, Func<int?>? readBitrateKbps = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (fanout is not null && _bindings.TryGetValue(fanout, out _))
                throw new ArgumentException("A fan-out identity cannot be reused for another owner.", nameof(fanout));

            var scope = _policy.OpenChannel(channelId, sessionId, kind, readBitrateKbps);
            var owner = new Owner(this, scope);
            if (_owners.TryGetValue(channelId, out var previous)) previous.Retired = true;
            _owners[channelId] = owner;
            if (fanout is not null) _bindings.Add(fanout, owner);
            return owner;
        }
    }

    public IPcpDownstreamRelayAdmission? Resolve(ChannelId channelId, PcpRelayFanout fanout)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(fanout);
        lock (_gate)
        {
            if (!_disposed && _bindings.TryGetValue(fanout, out var owner) &&
                owner.ChannelId == channelId)
                return owner;

            // Unknown role is never guessed as Viewing. An explicitly configured
            // finite policy fails closed; unlimited unbound routes retain legacy
            // behavior. A retired registry never grants new admissions.
            var limits = _policy.Limits;
            return _disposed || limits.MaxRelaysTotal is not null ||
                limits.MaxRelaysPerBroadcastChannel is not null ||
                limits.MaxRelaysPerViewingChannel is not null || _policy.BandwidthLimits.Ipv4Kbps is not null
                ? new UnavailableAdmission(channelId) : null;
        }
    }

    private bool TryReserve(Owner owner, long connectionId,
        [NotNullWhen(true)] out IDisposable? reservation, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reservation = null;
            if (_disposed || owner.Retired) return false;
            var result = owner.Scope.TryReserve(connectionId, cancellationToken);
            if (result.Reservation is null) return false;
            var lease = new Reservation(this, owner, result.Reservation);
            try { _connections.Add(connectionId, lease); }
            catch { result.Reservation.Dispose(); throw; }
            owner.ActiveReservations++;
            reservation = lease;
            return true;
        }
    }

    private void Release(Reservation reservation)
    {
        lock (_gate)
        {
            var id = reservation.Lease.RelayId;
            if (_connections.TryGetValue(id, out var current) && ReferenceEquals(current, reservation))
            {
                _connections.Remove(id);
                reservation.Owner.ActiveReservations--;
                reservation.Lease.Dispose();
            }
        }
    }

    private void Retire(Owner owner)
    {
        lock (_gate)
        {
            owner.Retired = true;
            owner.Scope.Dispose();
            if (_owners.TryGetValue(owner.ChannelId, out var current) && ReferenceEquals(current, owner))
                _owners.Remove(owner.ChannelId);
            // Weak bindings keep stale routes denied while their fan-out lives,
            // without retaining or disposing a caller-owned fan-out.
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var owner in _owners.Values)
            {
                owner.Retired = true;
                owner.Scope.Dispose();
            }
            _owners.Clear();
            _bindings.Clear();
            // Existing reservations remain until the real connection cleanup.
        }
        GC.SuppressFinalize(this);
    }

    internal string CapacityStatusText(Owner? owner = null)
    {
        lock (_gate)
        {
            var snapshot = owner?.Scope.Snapshot ?? _policy.Snapshot;
            static string Limit(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "制限なし";
            var channel = owner is null ? "" : $" / チャンネル予約 {snapshot.ChannelReservedRelays}/{Limit(snapshot.ChannelRelayLimit)}";
            var reserved = snapshot.ReservedIpv4Kbps?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
            var availability = owner is null ? "" : owner.CanReserve ? " / 新規予約可" : " / 新規予約不可";
            static string Rate(PcpDownstreamTrafficSnapshot value) => value.KilobitsPerSecond?.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown";
            var traffic = _policy.Traffic.Snapshot;
            var channelRate = owner is null ? "" : $" / CH書込完了 {Rate(owner.Traffic.Snapshot)} kbps";
            var incomplete = traffic.CumulativeComplete ? "" : "（累積一部不明）";
            return $"リレー予約 {snapshot.ReservedRelays}/{Limit(snapshot.RelayLimit)}{channel} / IPv4帯域予約 {reserved}/{Limit(snapshot.Ipv4BudgetKbps)} kbps（申告値） / 下流PCP書込完了 {Rate(traffic)} kbps{channelRate}{incomplete}{availability}";
        }
    }

    internal sealed class Owner : IPcpDownstreamRelayRegistration, IPcpDownstreamRelayCapacity, IPcpDownstreamRelayTraffic
    {
        private readonly GenesisDownstreamRelayOwnerRegistry _registry;
        internal Owner(GenesisDownstreamRelayOwnerRegistry registry, RelayAdmissionPolicy.Channel scope)
        {
            _registry = registry;
            Scope = scope;
        }
        internal RelayAdmissionPolicy.Channel Scope { get; }
        public PcpDownstreamTrafficMeter Traffic => Scope.Traffic;
        internal bool Retired { get; set; }
        internal int ActiveReservations { get; set; }
        public bool IsCurrent { get { lock (_registry._gate) return !_registry._disposed && !Retired && Scope.IsCurrent; } }
        public bool CanReserve { get { lock (_registry._gate) return !_registry._disposed && !Retired && Scope.CanReserve; } }
        public int ReservationCount { get { lock (_registry._gate) { return ActiveReservations; } } }
        public ChannelId ChannelId => Scope.ChannelId;
        public ChannelSessionId SessionId => Scope.SessionId;
        public RelayChannelKind Kind => Scope.Kind;
        public bool TryReserve(long connectionId,
            [NotNullWhen(true)] out IDisposable? reservation,
            CancellationToken cancellationToken = default) =>
            _registry.TryReserve(this, connectionId, out reservation, cancellationToken);
        public void Dispose()
        {
            _registry.Retire(this);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class Reservation(GenesisDownstreamRelayOwnerRegistry registry, Owner owner,
        RelayAdmissionPolicy.Reservation lease) : IDisposable
    {
        private int _released;
        internal RelayAdmissionPolicy.Reservation Lease { get; } = lease;
        internal Owner Owner { get; } = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            registry.Release(this);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class UnavailableAdmission(ChannelId channelId) : IPcpDownstreamRelayAdmission
    {
        public ChannelId ChannelId { get; } = channelId;
        public bool TryReserve(long connectionId,
            [NotNullWhen(true)] out IDisposable? reservation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reservation = null;
            return false;
        }
    }
}
