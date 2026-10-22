using PeerCast.Nova.Core.Channels;

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
}

public sealed record RelayAdmissionResult(
    RelayAdmissionDecision Decision,
    RelayAdmissionPolicy.Reservation? Reservation);

/// <summary>
/// Reservations for validated downstream PCP relays only. The caller owns the
/// connection and must retain its reservation until connection cleanup completes.
/// No sockets, local media consumers, or existing sessions are controlled here.
/// </summary>
public sealed class RelayAdmissionPolicy : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ChannelId, ChannelState> _channels = [];
    private readonly Dictionary<long, Reservation> _reservations = [];
    private long _reservedCount;
    private bool _disposed;

    public RelayAdmissionPolicy(RelayAdmissionLimits? limits = null)
    {
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
        RelayChannelKind kind)
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
            var owner = new Channel(this, channelId, sessionId, kind);
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

            var reservation = new Reservation(this, owner, relayId);
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
            ChannelSessionId sessionId, RelayChannelKind kind)
        {
            _policy = policy;
            ChannelId = channelId;
            SessionId = sessionId;
            Kind = kind;
        }

        public ChannelId ChannelId { get; }
        public ChannelSessionId SessionId { get; }
        public RelayChannelKind Kind { get; }
        internal bool Retired { get; set; }

        /// <summary>Outstanding reservations for this channel across all generations.</summary>
        public long ReservedCount => _policy.ReadChannelCount(ChannelId);

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

        internal Reservation(RelayAdmissionPolicy policy, Channel owner, long relayId)
        {
            _policy = policy;
            Owner = owner;
            RelayId = relayId;
        }

        internal Channel Owner { get; }
        public long RelayId { get; }
        public ChannelId ChannelId => Owner.ChannelId;
        public ChannelSessionId SessionId => Owner.SessionId;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            _policy.Release(this);
            GC.SuppressFinalize(this);
        }
    }
}
