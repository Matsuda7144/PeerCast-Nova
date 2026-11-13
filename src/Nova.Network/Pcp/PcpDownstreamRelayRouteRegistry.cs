using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Pcp;

/// <summary>
/// Thread-safe application-agnostic routing table from one PeerCast channel id
/// to its existing bounded relay fan-out. A resolved route is leased so channel
/// shutdown can first stop new routing and then await in-flight route users before
/// disposing the caller-owned fan-out.
/// </summary>
internal sealed class PcpDownstreamRelayRouteRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, RouteEntry> _routes = [];

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _routes.Count;
            }
        }
    }

    public PcpDownstreamRelayRouteRegistration Register(
        ChannelId channelId,
        PcpRelayFanout fanout)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(fanout);

        var entry = new RouteEntry(fanout);
        lock (_sync)
        {
            if (!_routes.TryAdd(channelId.Value, entry))
            {
                throw new InvalidOperationException(
                    "A downstream relay route is already registered for this channel.");
            }
        }

        return new PcpDownstreamRelayRouteRegistration(
            this,
            channelId.Value,
            entry);
    }

    public bool TryAcquire(
        Guid channelId,
        out PcpDownstreamRelayRouteLease? lease)
    {
        lock (_sync)
        {
            if (!_routes.TryGetValue(channelId, out var entry) ||
                !entry.IsRegistered)
            {
                lease = null;
                return false;
            }

            entry.ActiveLeaseCount = checked(entry.ActiveLeaseCount + 1);
            lease = new PcpDownstreamRelayRouteLease(this, entry);
            return true;
        }
    }

    private ValueTask UnregisterAsync(Guid channelId, RouteEntry entry)
    {
        Task drained;
        lock (_sync)
        {
            if (_routes.TryGetValue(channelId, out var current) &&
                ReferenceEquals(current, entry))
            {
                _routes.Remove(channelId);
            }

            entry.IsRegistered = false;
            if (entry.ActiveLeaseCount == 0)
            {
                entry.Drained.TrySetResult();
            }

            drained = entry.Drained.Task;
        }

        return new ValueTask(drained);
    }

    private void Release(RouteEntry entry)
    {
        lock (_sync)
        {
            if (entry.ActiveLeaseCount <= 0)
            {
                throw new InvalidOperationException(
                    "A downstream relay route lease was released more than once.");
            }

            entry.ActiveLeaseCount--;
            if (!entry.IsRegistered && entry.ActiveLeaseCount == 0)
            {
                entry.Drained.TrySetResult();
            }
        }
    }

    internal sealed class PcpDownstreamRelayRouteRegistration : IAsyncDisposable
    {
        private PcpDownstreamRelayRouteRegistry? _registry;
        private readonly Guid _channelId;
        private readonly RouteEntry _entry;
        private readonly object _gate = new();
        private Task? _disposal;

        internal PcpDownstreamRelayRouteRegistration(
            PcpDownstreamRelayRouteRegistry registry,
            Guid channelId,
            RouteEntry entry)
        {
            _registry = registry;
            _channelId = channelId;
            _entry = entry;
        }

        /// <summary>
        /// Stops new route acquisition immediately and waits for all previously
        /// acquired leases to drain. The route owner can dispose its fan-out only
        /// after this operation completes.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_disposal is null)
                {
                    var registry = Interlocked.Exchange(ref _registry, null);
                    _disposal = registry?.UnregisterAsync(_channelId, _entry).AsTask() ?? Task.CompletedTask;
                }
                return new(_disposal);
            }
        }
    }

    internal sealed class PcpDownstreamRelayRouteLease : IDisposable
    {
        private PcpDownstreamRelayRouteRegistry? _registry;
        private readonly RouteEntry _entry;

        internal PcpDownstreamRelayRouteLease(
            PcpDownstreamRelayRouteRegistry registry,
            RouteEntry entry)
        {
            _registry = registry;
            _entry = entry;
        }

        public PcpRelayFanout Fanout
        {
            get
            {
                ObjectDisposedException.ThrowIf(
                    Volatile.Read(ref _registry) is null,
                    this);
                return _entry.Fanout;
            }
        }

        public void Dispose()
        {
            var registry = Interlocked.Exchange(ref _registry, null);
            registry?.Release(_entry);
        }
    }

    internal sealed class RouteEntry
    {
        public RouteEntry(PcpRelayFanout fanout)
        {
            Fanout = fanout;
        }

        public PcpRelayFanout Fanout { get; }

        public bool IsRegistered { get; set; } = true;

        public int ActiveLeaseCount { get; set; }

        public TaskCompletionSource Drained { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
