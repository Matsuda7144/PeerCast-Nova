using System.Net;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Publishing;

public sealed record PublishingAnnounceTarget(string Name, string Endpoint)
{
    public void Validate()
    {
        if (Name.Length > 128 || Name.Any(char.IsControl)) throw new ArgumentException("Invalid YP name.");
        if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("YP name required.");
        _ = PcpAnnounceEndpoint.Parse(Endpoint);
    }
}

public sealed record PublishingAnnounceOptions(bool Enabled = false, string Name = "", string Endpoint = "")
{
    public const int MaximumTargets = 8;
    public IReadOnlyList<PublishingAnnounceTarget>? AdditionalTargets { get; init; }

    public bool Equals(PublishingAnnounceOptions? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        if (Enabled != other.Enabled ||
            !string.Equals(Name, other.Name, StringComparison.Ordinal) ||
            !string.Equals(Endpoint, other.Endpoint, StringComparison.Ordinal))
        {
            return false;
        }

        var left = AdditionalTargets ?? [];
        var right = other.AdditionalTargets ?? [];
        return left.Count == right.Count && left.SequenceEqual(right);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Enabled);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Endpoint, StringComparer.Ordinal);
        if (AdditionalTargets is not null)
        {
            foreach (var target in AdditionalTargets)
            {
                hash.Add(target);
            }
        }
        return hash.ToHashCode();
    }

    public IReadOnlyList<PublishingAnnounceTarget> GetAllTargets()
    {
        var targets = new List<PublishingAnnounceTarget>();
        var seenEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            var normalized = PcpAnnounceEndpoint.Parse(Endpoint).Uri.AbsoluteUri;
            if (seenEndpoints.Add(normalized))
            {
                targets.Add(new(Name, Endpoint));
            }
        }

        if (AdditionalTargets is not null)
        {
            foreach (var extra in AdditionalTargets)
            {
                ArgumentNullException.ThrowIfNull(extra);
                var normalized = PcpAnnounceEndpoint.Parse(extra.Endpoint).Uri.AbsoluteUri;
                if (seenEndpoints.Add(normalized))
                {
                    targets.Add(extra);
                    if (targets.Count >= MaximumTargets)
                    {
                        break;
                    }
                }
            }
        }

        return targets;
    }

    public void Validate()
    {
        if (Name.Length > 128 || Name.Any(char.IsControl)) throw new ArgumentException("Invalid YP name.");
        if (Enabled || Endpoint.Length != 0) _ = PcpAnnounceEndpoint.Parse(Endpoint);
        if (Enabled && string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("YP name required.");
        if (AdditionalTargets is not null)
        {
            if (AdditionalTargets.Count >= MaximumTargets)
                throw new ArgumentException($"Additional YP targets cannot exceed {MaximumTargets - 1}.");
            foreach (var target in AdditionalTargets)
            {
                ArgumentNullException.ThrowIfNull(target);
                target.Validate();
            }
        }
    }
}

public sealed record PublishingAnnounceStatus(string State, bool TrackerReportedOpen, bool AnnounceSent,
    bool StopNoticeSent, bool CleanupProven)
{
    public bool InboundPingObserved { get; init; }
    public bool IndependentReachabilityVerified { get; }
    public bool ListingConfirmed { get; }
    public bool ListingRemovalConfirmed { get; }
    public long PushRequestsReceived { get; init; }
    public int AnnouncedTargetCount { get; init; }
    public int TotalTargetCount { get; init; }
}

/// <summary>Application-lifetime BCID; one cleanup-proven generation per YP target. Construction performs no I/O.</summary>
public sealed class PublishingAnnounceOwner : IAsyncDisposable
{
    private readonly PcpAnnounceOwner _owner;
    private readonly List<PcpAnnounceOwner> _extraOwners = [];
    private readonly IPcpAnnounceConnector _connector;
    private readonly TimeProvider? _timeProvider;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private readonly TimeSpan _reconnectBaseDelay;
    private long _generation;
    private bool _disposed;
    private readonly object _gate = new();
    private PublishingAnnounceSession? _current;
    public Guid BroadcastId => _owner.BroadcastId;
    public PublishingAnnounceOwner() : this(broadcastId: null)
    { }
    public PublishingAnnounceOwner(Guid? broadcastId) : this(new PcpAnnounceProductionConnector(),
        (host, token) => Dns.GetHostAddressesAsync(host, token), broadcastId: broadcastId)
    { }
    internal PublishingAnnounceOwner(IPcpAnnounceConnector connector, Func<string, CancellationToken, Task<IPAddress[]>> resolver, TimeProvider? timeProvider = null, TimeSpan? reconnectBaseDelay = null, Guid? broadcastId = null)
    {
        _connector = connector;
        _timeProvider = timeProvider;
        _owner = new(connector, new PcpAnnounceOptions { HandshakeTimeout = TimeSpan.FromSeconds(20) }, timeProvider, broadcastId);
        _resolve = resolver;
        _reconnectBaseDelay = reconnectBaseDelay ?? TimeSpan.FromSeconds(5);
    }

    public async ValueTask DisposeAsync()
    {
        PublishingAnnounceSession? session;
        PcpAnnounceOwner[] extra;
        lock (_gate) { _disposed = true; session = _current; extra = [.. _extraOwners]; }
        if (session is not null) await session.StopAsync(PcpAnnounceStopReason.Dispose).ConfigureAwait(false);
        await _owner.DisposeAsync().ConfigureAwait(false);
        foreach (var extraOwner in extra)
        {
            await extraOwner.DisposeAsync().ConfigureAwait(false);
        }
    }

    internal PcpAnnounceOwner GetTargetOwner(int targetIndex)
    {
        if (targetIndex == 0)
        {
            return _owner;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (_extraOwners.Count < targetIndex)
            {
                _extraOwners.Add(new(
                    _connector,
                    new PcpAnnounceOptions { HandshakeTimeout = TimeSpan.FromSeconds(20) },
                    _timeProvider,
                    BroadcastId));
            }
            return _extraOwners[targetIndex - 1];
        }
    }

    internal PublishingAnnounceSession Start(PublishingAnnounceOptions options, ChannelId channel, ChannelSessionId session,
        ChannelMetadataSnapshot metadata, IPEndPoint bind, IPEndPoint advertised, Func<bool> ready, Func<PublishingAnnounceRuntimeSnapshot> runtime,
        Action<PeerCast.Nova.Protocol.Pcp.PcpPushRelayRequest>? onPushRelayRequest = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not null && !_current.CleanupProven)
                throw new InvalidOperationException("Previous announce cleanup is unproven.");
            _current = new(this, () => Interlocked.Increment(ref _generation), options, channel, session, metadata,
                bind, advertised, ready, runtime, _resolve, _reconnectBaseDelay, onPushRelayRequest);
            return _current;
        }
    }
}

internal sealed class PublishingAnnounceSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly List<TargetRunner> _runners = [];
    private ChannelMetadataSnapshot _latestMetadata;
    private Task<bool>? _termination;
    private bool _closing;

    internal bool CleanupProven => _runners.Count == 0 || _runners.All(static runner => runner.CleanupProven);

    internal PublishingAnnounceStatus Status
    {
        get
        {
            if (_runners.Count == 0)
            {
                return new(_closing ? "Stopping" : "Disabled", false, false, false, true)
                {
                    TotalTargetCount = 0,
                    AnnouncedTargetCount = 0,
                };
            }

            var snapshots = _runners.Select(static runner => runner.ReadStatus()).ToArray();
            var primary = snapshots[0];
            var announcedCount = snapshots.Count(static s => s.Announced);
            var openCount = snapshots.Count(static s => s.TrackerReportedOpen);
            var cleanupProven = snapshots.All(static s => s.CleanupProven);
            var pushTotal = snapshots.Sum(static s => s.PushRequestsReceived);
            var state = snapshots.Any(static s => s.Deadline) ? "CleanupIncomplete"
                : snapshots.Any(static s => s.Announced) ? "AnnounceSent"
                : snapshots.Any(static s => s.TrackerReportedOpen) ? "Connected"
                : snapshots.Any(static s => s.Reconnecting) ? "Reconnecting"
                : snapshots.All(static s => s.Failed) ? "Failed"
                : primary.State;

            return new(
                state,
                openCount > 0,
                announcedCount > 0,
                snapshots.All(static s => s.StopNoticeSent),
                cleanupProven)
            {
                PushRequestsReceived = pushTotal,
                AnnouncedTargetCount = announcedCount,
                TotalTargetCount = snapshots.Length,
            };
        }
    }

    internal static bool ShouldReconnect(PcpAnnounceResult result) =>
        result.CleanupProven &&
        result.Failure is PcpAnnounceFailure.Connection or PcpAnnounceFailure.Transport or PcpAnnounceFailure.Timeout;

    internal static TimeSpan ComputeReconnectDelay(int attempt, TimeSpan baseDelay)
    {
        var effectiveBase = baseDelay <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : baseDelay;
        var multiplier = 1 << Math.Clamp(attempt, 0, 3);
        var scaled = TimeSpan.FromTicks(effectiveBase.Ticks * multiplier);
        var maximum = TimeSpan.FromSeconds(30);
        return scaled > maximum ? maximum : scaled;
    }

    internal PublishingAnnounceSession(PublishingAnnounceOwner owner, Func<long> nextGeneration, PublishingAnnounceOptions options,
        ChannelId channel, ChannelSessionId session, ChannelMetadataSnapshot metadata, IPEndPoint bind,
        IPEndPoint advertised, Func<bool> ready, Func<PublishingAnnounceRuntimeSnapshot> runtime,
        Func<string, CancellationToken, Task<IPAddress[]>> resolver, TimeSpan reconnectBaseDelay,
        Action<PeerCast.Nova.Protocol.Pcp.PcpPushRelayRequest>? onPushRelayRequest = null)
    {
        _latestMetadata = metadata;
        var targets = options.Enabled && ready() ? options.GetAllTargets() : [];
        if (options.Enabled && ready())
        {
            options.Validate();
        }

        for (var index = 0; index < targets.Count; index++)
        {
            var targetOwner = owner.GetTargetOwner(index);
            _runners.Add(new TargetRunner(
                this,
                targetOwner,
                nextGeneration,
                targets[index],
                channel,
                session,
                metadata,
                bind,
                advertised,
                ready,
                runtime,
                resolver,
                reconnectBaseDelay,
                onPushRelayRequest));
        }
    }

    internal bool UpdateMetadata(ChannelMetadataSnapshot metadata, IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        lock (_gate)
        {
            if (_closing) return false;
            _latestMetadata = metadata;
        }

        var updated = false;
        foreach (var runner in _runners)
        {
            if (runner.UpdateMetadata(metadata, endpoint))
            {
                updated = true;
            }
        }
        return updated;
    }

    internal void NotifyRuntimeChanged()
    {
        foreach (var runner in _runners)
        {
            runner.NotifyRuntimeChanged();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(PcpAnnounceStopReason.Dispose).ConfigureAwait(false);

    internal Task<bool> StopAsync(PcpAnnounceStopReason reason)
    {
        lock (_gate)
        {
            _closing = true;
            return _termination ??= StopCoreAsync(reason);
        }
    }

    private async Task<bool> StopCoreAsync(PcpAnnounceStopReason reason)
    {
        await Task.Yield();
        _stop.Cancel();
        if (_runners.Count > 0)
        {
            var results = await Task.WhenAll(_runners.Select(runner => runner.StopAsync(reason))).ConfigureAwait(false);
            var clean = results.All(static ok => ok);
            if (clean) _stop.Dispose();
            return clean;
        }
        _stop.Dispose();
        return true;
    }

    private sealed class TargetRunner
    {
        private readonly PublishingAnnounceSession _session;
        private readonly object _gate = new();
        private readonly Task _run;
        private Task<IPEndPoint>? _dns;
        private PcpAnnounceWorker? _worker;
        private PublishingAnnounceCapability? _capability;
        private ChannelMetadataSnapshot _latestMetadata;
        private bool _closing, _failed, _deadline, _reconnecting;

        internal TargetRunner(
            PublishingAnnounceSession sessionOwner,
            PcpAnnounceOwner owner,
            Func<long> nextGeneration,
            PublishingAnnounceTarget target,
            ChannelId channel,
            ChannelSessionId session,
            ChannelMetadataSnapshot metadata,
            IPEndPoint bind,
            IPEndPoint advertised,
            Func<bool> ready,
            Func<PublishingAnnounceRuntimeSnapshot> runtime,
            Func<string, CancellationToken, Task<IPAddress[]>> resolver,
            TimeSpan reconnectBaseDelay,
            Action<PeerCast.Nova.Protocol.Pcp.PcpPushRelayRequest>? onPushRelayRequest)
        {
            _session = sessionOwner;
            _latestMetadata = metadata;
            _run = Task.Run(async () =>
            {
                try
                {
                    using var dnsDeadline = CancellationTokenSource.CreateLinkedTokenSource(_session._stop.Token);
                    dnsDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                    _dns = PcpAnnounceProductionConnector.ResolveAsync(PcpAnnounceEndpoint.Parse(target.Endpoint), resolver, dnsDeadline.Token);
                    _ = _dns.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
                    var endpoint = await _dns.WaitAsync(TimeSpan.FromSeconds(10), _session._stop.Token).ConfigureAwait(false);
                    var consecutiveFailures = 0;
                    while (!_session._stop.IsCancellationRequested)
                    {
                        PcpAnnounceWorker worker;
                        lock (_gate)
                        {
                            if (_closing || !ready()) return;
                            var workerGeneration = nextGeneration();
                            var identity = new PcpAnnounceIdentity(workerGeneration, channel.Value, session.Value, session.Value, owner.BroadcastId);
                            _capability = new(identity, Guid.NewGuid(), bind, advertised, endpoint, () => !_closing && ready());
                            worker = owner.Start(workerGeneration, channel.Value, session.Value, session.Value,
                                PcpAnnounceEndpoint.Parse($"pcp://{endpoint.Address}:{endpoint.Port}"), new(_latestMetadata, advertised),
                                PcpAnnounceAccess.ForProduction(endpoint), capability: _capability, runtime: runtime,
                                onPushRelayRequest: onPushRelayRequest);
                            _worker = worker;
                            Volatile.Write(ref _reconnecting, false);
                            worker.NotifyRuntimeChanged();
                        }

                        var result = await worker.Completion.ConfigureAwait(false);
                        _capability?.Invalidate();
                        if (result.AnnounceSent)
                        {
                            consecutiveFailures = 0;
                        }
                        else
                        {
                            consecutiveFailures++;
                        }

                        if (_closing || _session._stop.IsCancellationRequested || !ready() || !ShouldReconnect(result))
                        {
                            break;
                        }

                        Volatile.Write(ref _reconnecting, true);
                        var delay = ComputeReconnectDelay(consecutiveFailures > 0 ? consecutiveFailures - 1 : 0, reconnectBaseDelay);
                        await Task.Delay(delay, _session._stop.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_closing || _session._stop.IsCancellationRequested) { }
                catch (Exception) { _failed = true; }
                finally
                {
                    Volatile.Write(ref _reconnecting, false);
                    _capability?.Invalidate();
                }
            });
        }

        internal bool CleanupProven => !_deadline && (_dns is null || _dns.IsCompleted) && _run.IsCompleted && (_worker?.CleanupProven ?? true);

        internal TargetSnapshot ReadStatus()
        {
            var trackerOpen = _worker?.TrackerReport is { } report && _capability is { } capability && report.CanAnnounce(capability);
            var announced = _worker?.Announced == true;
            var stopNoticeSent = _worker?.StopNoticeSent == true;
            var reconnecting = Volatile.Read(ref _reconnecting);
            var state = _deadline ? "CleanupIncomplete"
                : _failed ? "Failed"
                : reconnecting ? "Reconnecting"
                : _worker?.State.ToString() ?? (_closing ? "Stopping" : "Connecting");
            return new TargetSnapshot(
                state,
                trackerOpen,
                announced,
                stopNoticeSent,
                CleanupProven,
                _worker?.PushRequestsAccepted ?? 0,
                _failed,
                _deadline,
                reconnecting);
        }

        internal bool UpdateMetadata(ChannelMetadataSnapshot metadata, IPEndPoint endpoint)
        {
            PcpAnnounceWorker? worker;
            lock (_gate)
            {
                if (_closing) return false;
                _latestMetadata = metadata;
                worker = _worker;
            }
            return worker is not null && worker.Update(worker.Identity, new(metadata, endpoint));
        }

        internal void NotifyRuntimeChanged() => Volatile.Read(ref _worker)?.NotifyRuntimeChanged();

        internal async Task<bool> StopAsync(PcpAnnounceStopReason reason)
        {
            lock (_gate)
            {
                _closing = true;
                _capability?.Invalidate();
            }
            PcpAnnounceWorker? worker;
            lock (_gate) worker = _worker;
            if (worker is not null) await worker.StopAsync(reason).ConfigureAwait(false);
            try { await _run.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
            catch (TimeoutException) { _deadline = true; return false; }
            if (!CleanupProven) _deadline = true;
            return CleanupProven;
        }
    }

    private sealed record TargetSnapshot(
        string State,
        bool TrackerReportedOpen,
        bool Announced,
        bool StopNoticeSent,
        bool CleanupProven,
        long PushRequestsReceived,
        bool Failed,
        bool Deadline,
        bool Reconnecting);
}
