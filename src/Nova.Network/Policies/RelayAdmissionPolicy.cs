using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.Network.Policies;

public enum RelayChannelKind
{
    Broadcast,
    Viewing,
}

public sealed record RelayAdmissionLimits(
    int? MaxRelaysTotal = null,
    int? MaxRelaysPerBroadcastChannel = null,
    int? MaxRelaysPerViewingChannel = null);

public enum RelayAdmissionDecision
{
    Admitted,
    TotalLimitReached,
    ChannelLimitReached,
    DuplicateRelay,
    OwnerRetired,
    BandwidthLimitReached,
    BandwidthAccountingUnavailable,
    BitrateUnavailable,
}

public sealed record RelayAdmissionResult(
    RelayAdmissionDecision Decision,
    RelayAdmissionPolicy.Reservation? Reservation);

/// <summary>Admission reservations, not measured traffic or a sending-rate limit.</summary>
public sealed record RelayAdmissionCapacitySnapshot(
    long ReservedRelays, int? RelayLimit, long? ChannelReservedRelays, int? ChannelRelayLimit,
    int? Ipv4BudgetKbps, long? ReservedIpv4Kbps);

/// <summary>
/// Reservations for validated downstream PCP relays only. The caller owns the
/// connection and must retain its reservation until connection cleanup completes.
/// No sockets, local media consumers, or existing sessions are controlled here.
/// </summary>
public sealed class RelayAdmissionPolicy : IDisposable
{
    private readonly object _gate = new();
    private readonly UpstreamBandwidthPolicy _bandwidth;
    private readonly Dictionary<ChannelId, ChannelState> _channels = [];
    private readonly Dictionary<long, Reservation> _reservations = [];
    private long _reservedCount;
    private bool _disposed;

    public RelayAdmissionPolicy(RelayAdmissionLimits? limits = null, UpstreamBandwidthPolicy? bandwidth = null)
    {
        _bandwidth = bandwidth ?? new();
        Limits = limits is null ? new() : limits with { };
        if (Limits.MaxRelaysTotal < 0 ||
            Limits.MaxRelaysPerBroadcastChannel < 0 ||
            Limits.MaxRelaysPerViewingChannel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits),
                "Relay limits cannot be negative.");
        }
    }

    public RelayAdmissionLimits Limits { get; }
    public PcpDownstreamTrafficMeter Traffic { get; } = new();
    public UpstreamBandwidthLimits BandwidthLimits => _bandwidth.Limits;
    public RelayAdmissionCapacitySnapshot Snapshot { get { lock (_gate) return ReadSnapshot(null); } }

    public long ReservedCount
    {
        get { lock (_gate) { return _reservedCount; } }
    }

    /// <summary>
    /// Publishes one owner generation. Replacing an owner retires its admission
    /// rights, but its outstanding reservations still count toward both limits.
    /// Classification must come from the channel owner, not a peer address.
    /// </summary>
    public Channel OpenChannel(ChannelId channelId, ChannelSessionId sessionId,
        RelayChannelKind kind, Func<int?>? readBitrateKbps = null)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_channels.TryGetValue(channelId, out var state))
            {
                if (!state.CurrentOwner.Retired && state.CurrentOwner.SessionId == sessionId)
                {
                    throw new InvalidOperationException("This channel generation already has an owner.");
                }
                state.CurrentOwner.Retired = true;
            }
            var owner = new Channel(this, channelId, sessionId, kind, readBitrateKbps);
            if (state is null)
            {
                _channels.Add(channelId, new ChannelState(owner));
            }
            else
            {
                state.CurrentOwner = owner;
            }
            return owner;
        }
    }

    private RelayAdmissionResult Reserve(Channel owner, long relayId,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(relayId);
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || owner.Retired)
                return new(RelayAdmissionDecision.OwnerRetired, null);
            if (_reservations.ContainsKey(relayId))
                return new(RelayAdmissionDecision.DuplicateRelay, null);
            if (Limits.MaxRelaysTotal is { } total && _reservedCount >= total)
                return new(RelayAdmissionDecision.TotalLimitReached, null);

            var state = _channels[owner.ChannelId];
            var channelLimit = owner.Kind == RelayChannelKind.Broadcast
                ? Limits.MaxRelaysPerBroadcastChannel
                : Limits.MaxRelaysPerViewingChannel;
            if (channelLimit is { } maximum && state.ReservedCount >= maximum)
                return new(RelayAdmissionDecision.ChannelLimitReached, null);

            var bitrate = ReadBitrate(owner);
            var bandwidth = EvaluateBandwidth(bitrate);
            if (bandwidth != BandwidthAdmissionDecision.Allowed)
                return new(bandwidth switch
                {
                    BandwidthAdmissionDecision.LimitReached => RelayAdmissionDecision.BandwidthLimitReached,
                    BandwidthAdmissionDecision.BitrateUnavailable => RelayAdmissionDecision.BitrateUnavailable,
                    _ => RelayAdmissionDecision.BandwidthAccountingUnavailable,
                }, null);

            // Count and bandwidth admission share this gate and this reservation.
            // Neither resource is committed before both checks have succeeded.
            var reservation = new Reservation(this, owner, relayId, bitrate);
            _reservations.Add(relayId, reservation);
            state.ReservedCount++;
            _reservedCount++;
            return new(RelayAdmissionDecision.Admitted, reservation);
        }
    }

    private void Release(Reservation reservation)
    {
        lock (_gate)
        {
            if (!_reservations.TryGetValue(reservation.RelayId, out var current) ||
                !ReferenceEquals(current, reservation))
                return;

            _reservations.Remove(reservation.RelayId);
            var state = _channels[reservation.Owner.ChannelId];
            state.ReservedCount--;
            _reservedCount--;
            if (state.ReservedCount == 0 && (_disposed || state.CurrentOwner.Retired))
                _channels.Remove(reservation.Owner.ChannelId);
        }
    }

    private void Retire(Channel owner)
    {
        lock (_gate)
        {
            owner.Retired = true;
            if (_channels.TryGetValue(owner.ChannelId, out var state) &&
                ReferenceEquals(state.CurrentOwner, owner) && state.ReservedCount == 0)
                _channels.Remove(owner.ChannelId);
        }
    }

    private bool ReadIsCurrent(Channel owner)
    {
        lock (_gate) return !_disposed && !owner.Retired && _channels.TryGetValue(owner.ChannelId, out var state) &&
            ReferenceEquals(state.CurrentOwner, owner);
    }
    private bool ReadCanReserve(Channel owner)
    {
        lock (_gate)
        {
            if (_disposed || owner.Retired || !_channels.TryGetValue(owner.ChannelId, out var state) ||
                !ReferenceEquals(state.CurrentOwner, owner)) return false;
            var maximum = owner.Kind == RelayChannelKind.Broadcast ? Limits.MaxRelaysPerBroadcastChannel : Limits.MaxRelaysPerViewingChannel;
            return (Limits.MaxRelaysTotal is null || _reservedCount < Limits.MaxRelaysTotal) &&
                (maximum is null || state.ReservedCount < maximum) &&
                EvaluateBandwidth(ReadBitrate(owner)) == BandwidthAdmissionDecision.Allowed;
        }
    }

    private static int? ReadBitrate(Channel owner)
    {
        var bitrate = owner.ReadBitrateKbps?.Invoke();
        return bitrate is > 0 ? bitrate : null;
    }

    private long? ReadReservedIpv4Kbps()
    {
        long total = 0;
        foreach (var reservation in _reservations.Values)
        {
            var current = ReadBitrate(reservation.Owner);
            if (current is null) return null;
            // A newer, larger declaration increases the estimate used for new
            // admissions. A smaller declaration never discounts the original
            // reservation, including a retired generation still cleaning up.
            total = checked(total + Math.Max(reservation.BitrateKbps ?? 0, current.Value));
        }
        return total;
    }

    private BandwidthAdmissionDecision EvaluateBandwidth(int? bitrate) =>
        // Current application-owned downstream routes are IPv4 only. IPv6 WAN
        // and its accounting are deliberately not claimed by this integration.
        _bandwidth.Evaluate(IPAddress.Loopback,
            new(_bandwidth.Limits.Ipv4Kbps is null ? null : ReadReservedIpv4Kbps(), null), bitrate);

    private RelayAdmissionCapacitySnapshot ReadSnapshot(Channel? owner) => new(
        _reservedCount, Limits.MaxRelaysTotal,
        owner is null ? null : ReadChannelCount(owner.ChannelId),
        owner?.Kind == RelayChannelKind.Broadcast ? Limits.MaxRelaysPerBroadcastChannel :
            owner is null ? null : Limits.MaxRelaysPerViewingChannel,
        _bandwidth.Limits.Ipv4Kbps, ReadReservedIpv4Kbps());
    private long ReadChannelCount(ChannelId channelId)
    {
        lock (_gate)
        {
            return _channels.TryGetValue(channelId, out var state) ? state.ReservedCount : 0;
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; }
        // Reservations remain counted until their connection owner releases them.
        GC.SuppressFinalize(this);
    }

    private sealed class ChannelState(Channel owner)
    {
        public Channel CurrentOwner { get; set; } = owner;
        public long ReservedCount { get; set; }
    }

    public sealed class Channel : IDisposable
    {
        private readonly RelayAdmissionPolicy _policy;

        internal Channel(RelayAdmissionPolicy policy, ChannelId channelId,
            ChannelSessionId sessionId, RelayChannelKind kind, Func<int?>? readBitrateKbps)
        {
            _policy = policy;
            ChannelId = channelId;
            SessionId = sessionId;
            Kind = kind;
            ReadBitrateKbps = readBitrateKbps;
            Traffic = policy.Traffic.CreateChild();
        }

        public ChannelId ChannelId { get; }
        public ChannelSessionId SessionId { get; }
        public RelayChannelKind Kind { get; }
        public PcpDownstreamTrafficMeter Traffic { get; }
        internal bool Retired { get; set; }
        // Pure owner metadata observation; never perform I/O or call admission.
        internal Func<int?>? ReadBitrateKbps { get; }

        /// <summary>Outstanding reservations for this channel across all generations.</summary>
        public bool IsCurrent => _policy.ReadIsCurrent(this);
        public bool CanReserve => _policy.ReadCanReserve(this);
        public long ReservedCount => _policy.ReadChannelCount(ChannelId);
        public RelayAdmissionCapacitySnapshot Snapshot { get { lock (_policy._gate) return _policy.ReadSnapshot(this); } }

        /// <summary>
        /// A relay ID is allocated once per connection (for example ConnectionId).
        /// Duplicate notifications receive no second reservation. Cancellation
        /// before admission changes nothing; later cancellation must complete
        /// connection cleanup before disposing the original reservation.
        /// </summary>
        public RelayAdmissionResult TryReserve(long relayId,
            CancellationToken cancellationToken = default) =>
            _policy.Reserve(this, relayId, cancellationToken);

        public void Dispose()
        {
            _policy.Retire(this);
            GC.SuppressFinalize(this);
        }
    }

    public sealed class Reservation : IDisposable
    {
        private readonly RelayAdmissionPolicy _policy;
        private int _released;

        internal Reservation(RelayAdmissionPolicy policy, Channel owner, long relayId, int? bitrateKbps)
        {
            _policy = policy;
            Owner = owner;
            RelayId = relayId;
            BitrateKbps = bitrateKbps;
        }

        internal Channel Owner { get; }
        public long RelayId { get; }
        public ChannelId ChannelId => Owner.ChannelId;
        public ChannelSessionId SessionId => Owner.SessionId;
        public int? BitrateKbps { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            _policy.Release(this);
            GC.SuppressFinalize(this);
        }
    }
}
