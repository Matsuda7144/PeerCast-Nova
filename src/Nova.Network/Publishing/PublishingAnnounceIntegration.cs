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
}

/// <summary>Application-lifetime BCID; one cleanup-proven generation at a time. Construction performs no I/O.</summary>
public sealed class PublishingAnnounceOwner : IAsyncDisposable
{
    private readonly PcpAnnounceOwner _owner;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;
    private long _generation;
    private bool _disposed;
    private readonly object _gate = new();
    private PublishingAnnounceSession? _current;
    public PublishingAnnounceOwner() : this(new PcpAnnounceProductionConnector(),
        (host, token) => Dns.GetHostAddressesAsync(host, token))
    { }
    internal PublishingAnnounceOwner(IPcpAnnounceConnector connector, Func<string, CancellationToken, Task<IPAddress[]>> resolver, TimeProvider? timeProvider = null)
    { _owner = new(connector, new PcpAnnounceOptions { HandshakeTimeout = TimeSpan.FromSeconds(20) }, timeProvider); _resolve = resolver; }

    public async ValueTask DisposeAsync()
    {
        PublishingAnnounceSession? session;
        lock (_gate) { _disposed = true; session = _current; }
        if (session is not null) await session.StopAsync(PcpAnnounceStopReason.Dispose).ConfigureAwait(false);
        await _owner.DisposeAsync().ConfigureAwait(false);
    }
    internal PublishingAnnounceSession Start(PublishingAnnounceOptions options, ChannelId channel, ChannelSessionId session,
        ChannelMetadataSnapshot metadata, IPEndPoint bind, IPEndPoint advertised, Func<bool> ready, Func<PublishingAnnounceRuntimeSnapshot> runtime)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_current is not null && !_current.CleanupProven)
                throw new InvalidOperationException("Previous announce cleanup is unproven.");
            _current = new(_owner, Interlocked.Increment(ref _generation), options, channel, session, metadata,
                bind, advertised, ready, runtime, _resolve);
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
    private Task<bool>? _termination;
    private bool _closing, _failed, _deadline;
    internal bool CleanupProven => !_deadline && (_dns is null || _dns.IsCompleted) && _run.IsCompleted && (_worker?.CleanupProven ?? true);
    internal PublishingAnnounceStatus Status => new(_deadline ? "CleanupIncomplete" : _failed ? "Failed" :
        _worker?.State.ToString() ?? (_closing ? "Stopping" : "Connecting"),
        _worker?.TrackerReport is { } report && _capability is { } capability && report.CanAnnounce(capability),
        _worker?.Announced == true,
        _worker?.StopNoticeSent == true, CleanupProven);

    internal PublishingAnnounceSession(PcpAnnounceOwner owner, long generation, PublishingAnnounceOptions options,
        ChannelId channel, ChannelSessionId session, ChannelMetadataSnapshot metadata, IPEndPoint bind,
        IPEndPoint advertised, Func<bool> ready, Func<PublishingAnnounceRuntimeSnapshot> runtime, Func<string, CancellationToken, Task<IPAddress[]>> resolver)
    {
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
                lock (_gate)
                {
                    if (_closing || !ready()) return;
                    var identity = new PcpAnnounceIdentity(generation, channel.Value, session.Value, session.Value, owner.BroadcastId);
                    _capability = new(identity, Guid.NewGuid(), bind, advertised, endpoint, () => !_closing && ready());
                    _worker = owner.Start(generation, channel.Value, session.Value, session.Value,
                        PcpAnnounceEndpoint.Parse($"pcp://{endpoint.Address}:{endpoint.Port}"), new(metadata, advertised),
                        PcpAnnounceAccess.ForProduction(endpoint), capability: _capability, runtime: runtime);
                    _worker.NotifyRuntimeChanged();
                }
                await _worker.Completion.ConfigureAwait(false);
            }
            catch (Exception) { _failed = true; }
            finally { _capability?.Invalidate(); }
        });
    }
    internal bool UpdateMetadata(ChannelMetadataSnapshot metadata, IPEndPoint endpoint)
    {
        var worker = Volatile.Read(ref _worker);
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
        if (_worker is { } worker) await worker.StopAsync(reason).ConfigureAwait(false);
        try { await _run.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
        catch (TimeoutException) { _deadline = true; return false; }
        if (!CleanupProven) _deadline = true;
        if (CleanupProven) _stop.Dispose();
        return CleanupProven;
    }
}
