using System.Net;
using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Publishing;

public sealed record PublishingAnnounceOptions(bool Enabled = false, string Name = "", string Endpoint = "")
{
    public void Validate()
    {
        if (Name.Length > 128 || Name.Any(char.IsControl)) throw new ArgumentException("Invalid YP name.");
        if (Enabled || Endpoint.Length != 0) _ = PcpAnnounceEndpoint.Parse(Endpoint);
        if (Enabled && string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("YP name required.");
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
}

/// <summary>Application-lifetime BCID; one cleanup-proven generation at a time. Construction performs no I/O.</summary>
public sealed class PublishingAnnounceOwner : IAsyncDisposable
{
    private readonly PcpAnnounceOwner _owner;
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
        _owner = new(connector, new PcpAnnounceOptions { HandshakeTimeout = TimeSpan.FromSeconds(20) }, timeProvider, broadcastId);
        _resolve = resolver;
        _reconnectBaseDelay = reconnectBaseDelay ?? TimeSpan.FromSeconds(5);
    }

    public async ValueTask DisposeAsync()
    {
        PublishingAnnounceSession? session;
        lock (_gate) { _disposed = true; session = _current; }
        if (session is not null) await session.StopAsync(PcpAnnounceStopReason.Dispose).ConfigureAwait(false);
        await _owner.DisposeAsync().ConfigureAwait(false);
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
            _current = new(_owner, () => Interlocked.Increment(ref _generation), options, channel, session, metadata,
                bind, advertised, ready, runtime, _resolve, _reconnectBaseDelay, onPushRelayRequest);
            return _current;
        }
    }
}

internal sealed class PublishingAnnounceSession : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Task _run;
    private Task<IPEndPoint>? _dns;
    private PcpAnnounceWorker? _worker;
    private PublishingAnnounceCapability? _capability;
    private ChannelMetadataSnapshot _latestMetadata;
    private Task<bool>? _termination;
    private bool _closing, _failed, _deadline, _reconnecting;
    internal bool CleanupProven => !_deadline && (_dns is null || _dns.IsCompleted) && _run.IsCompleted && (_worker?.CleanupProven ?? true);
    internal PublishingAnnounceStatus Status => new(
        _deadline ? "CleanupIncomplete" : _failed ? "Failed" :
        Volatile.Read(ref _reconnecting) ? "Reconnecting" :
        _worker?.State.ToString() ?? (_closing ? "Stopping" : "Connecting"),
        _worker?.TrackerReport is { } report && _capability is { } capability && report.CanAnnounce(capability),
        _worker?.Announced == true,
        _worker?.StopNoticeSent == true, CleanupProven)
    {
        PushRequestsReceived = _worker?.PushRequestsAccepted ?? 0,
    };

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

    internal PublishingAnnounceSession(PcpAnnounceOwner owner, Func<long> nextGeneration, PublishingAnnounceOptions options,
        ChannelId channel, ChannelSessionId session, ChannelMetadataSnapshot metadata, IPEndPoint bind,
        IPEndPoint advertised, Func<bool> ready, Func<PublishingAnnounceRuntimeSnapshot> runtime,
        Func<string, CancellationToken, Task<IPAddress[]>> resolver, TimeSpan reconnectBaseDelay,
        Action<PeerCast.Nova.Protocol.Pcp.PcpPushRelayRequest>? onPushRelayRequest = null)
    {
        _latestMetadata = metadata;
        _run = Task.Run(async () =>
        {
            try
            {
                if (!options.Enabled || !ready()) return;
                options.Validate();
                using var dnsDeadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                dnsDeadline.CancelAfter(TimeSpan.FromSeconds(10));
                _dns = PcpAnnounceProductionConnector.ResolveAsync(PcpAnnounceEndpoint.Parse(options.Endpoint), resolver, dnsDeadline.Token);
                _ = _dns.ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
                var endpoint = await _dns.WaitAsync(TimeSpan.FromSeconds(10), _stop.Token).ConfigureAwait(false);
                var consecutiveFailures = 0;
                while (!_stop.IsCancellationRequested)
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

                    if (_closing || _stop.IsCancellationRequested || !ready() || !ShouldReconnect(result))
                    {
                        break;
                    }

                    Volatile.Write(ref _reconnecting, true);
                    var delay = ComputeReconnectDelay(consecutiveFailures > 0 ? consecutiveFailures - 1 : 0, reconnectBaseDelay);
                    await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_closing || _stop.IsCancellationRequested) { }
            catch (Exception) { _failed = true; }
            finally
            {
                Volatile.Write(ref _reconnecting, false);
                _capability?.Invalidate();
            }
        });
    }
    internal bool UpdateMetadata(ChannelMetadataSnapshot metadata, IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(metadata);
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
    public async ValueTask DisposeAsync() => await StopAsync(PcpAnnounceStopReason.Dispose).ConfigureAwait(false);
    internal Task<bool> StopAsync(PcpAnnounceStopReason reason)
    {
        lock (_gate)
        {
            _closing = true;
            _capability?.Invalidate();
            return _termination ??= StopCoreAsync(reason);
        }
    }
    private async Task<bool> StopCoreAsync(PcpAnnounceStopReason reason)
    {
        await Task.Yield();
        _stop.Cancel();
        PcpAnnounceWorker? worker;
        lock (_gate) worker = _worker;
        if (worker is not null) await worker.StopAsync(reason).ConfigureAwait(false);
        try { await _run.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
        catch (TimeoutException) { _deadline = true; return false; }
        if (!CleanupProven) _deadline = true;
        if (CleanupProven) _stop.Dispose();
        return CleanupProven;
    }
}
