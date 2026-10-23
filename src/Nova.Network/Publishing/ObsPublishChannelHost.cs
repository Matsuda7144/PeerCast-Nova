using System.Net;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>
/// One generation owning local ingest, existing relay fanout/server, HTTP source
/// and identity-bound recording registration. A supplied HTTP host and registry
/// are borrowed; otherwise this host owns its HTTP listener and private registry.
/// The stream key names an input route, not the PeerCast channel identity.
/// Construction never binds. Explicit Start may wait for a publisher until
/// cancellation/disposal; that termination is not PublisherStopped or a clean
/// media drain. A test's wait deadline is not a production arrival deadline.
/// </summary>
public sealed class ObsPublishChannelHost : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ObsPublishChannelOptions _options;
    private readonly PcpHttpFlvPlaybackHost _http;
    private readonly bool _ownsHttp;
    private readonly PcpRelayFanout _fanout;
    private readonly PcpDownstreamRelayServer _relay;
    private readonly PcpRelayFanoutRecordingStreamSourceLeaseFactory _recording;
    private readonly RecordingStreamSourceLeaseRegistry _registry;
    private readonly ChannelStatusTracker _tracker;
    private readonly ObsRtmpIngestHost _ingest;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<bool> _headAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PcpHttpFlvPlaybackSession? _httpSource;
    private HttpRelayChild? _httpSink;
    private PcpRelayChildRegistration? _httpChild;
    private IDisposable? _registration;
    private Task<ObsPublishChannelResult>? _completion;
    private Task? _disposeTask;
    private long _acceptedPackets;
    private long _acceptedBytes;
    private long _heads;
    private long _data;
    private bool _registered;
    private ObsPublishChannelResult? _result;
    private string? _httpStreamUrl;
    private readonly object _statusGate = new();
    private ObsPublishStatusSnapshot? _lastPublishingStatus;
    private long _publishingRevision;

    public ObsPublishChannelHost(
        ObsPublishChannelOptions? options = null,
        ChannelId? channelId = null,
        PcpHttpFlvPlaybackHost? httpHost = null,
        RecordingStreamSourceLeaseRegistry? recordingRegistry = null)
    {
        var supplied = options ?? new();
        ArgumentNullException.ThrowIfNull(supplied.Ingest);
        ArgumentNullException.ThrowIfNull(supplied.Http);
        ArgumentNullException.ThrowIfNull(supplied.Fanout);
        supplied.Validate();
        _options = supplied with
        {
            Ingest = supplied.Ingest with { },
            Http = supplied.Http with
            {
                Bootstrap = supplied.Http.Bootstrap with { },
                Fanout = supplied.Http.Fanout with { },
                Transport = supplied.Http.Transport with { },
            },
            Fanout = supplied.Fanout with { },
        };
        _options.Validate();
        ChannelId = channelId ?? new ChannelId(Guid.NewGuid());
        SessionId = new ChannelSessionId(Guid.NewGuid());
        _tracker = new ChannelStatusTracker(ChannelId, SessionId);
        _registry = recordingRegistry ?? new RecordingStreamSourceLeaseRegistry(1);
        _http = httpHost ?? new PcpHttpFlvPlaybackHost(_options.Http);
        _ownsHttp = httpHost is null;
        _fanout = new PcpRelayFanout(_options.Fanout, replayHeadForNewChildren: true);
        _recording = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(ChannelId, SessionId, _fanout);
        _relay = new PcpDownstreamRelayServer(
            new IPEndPoint(IPAddress.Loopback, _options.RelayPort), _fanout, ChannelId, SessionId.Value);
        _ingest = new ObsRtmpIngestHost(ChannelId, SessionId, new PublishConsumer(this), _options.Ingest);
    }

    public ChannelId ChannelId { get; }
    public ChannelSessionId SessionId { get; }
    public ChannelStatusSnapshot Snapshot => _tracker.Snapshot;
    public Task<bool> HeadAvailable => _headAvailable.Task;
    public IPEndPoint IngestEndPoint => _ingest.LocalEndPoint;
    public IPEndPoint RelayEndPoint => _relay.LocalEndPoint;
    public string HttpStreamUrl => _http.StreamUrlFor(ChannelId);
    public PcpDownstreamRelayServerSnapshot RelaySnapshot => _relay.Snapshot;
    public RecordingStreamSourceLeaseRegistry RecordingRegistry => _registry;
    internal PcpRelayFanout Fanout => _fanout;

    public bool IsRegistered
    {
        get { lock (_gate) { return _registered; } }
    }

    public ObsPublishStatusSnapshot PublishingStatus
    {
        get
        {
            lock (_statusGate)
            {
                var channel = _tracker.Snapshot;
                var ingest = _ingest.ReadStatus();
                var result = Volatile.Read(ref _result);
                // A terminal result dominates a snapshot captured just before cleanup.
                var state = result is null ? channel.State : ChannelSessionState.Stopped;
                var active = state is not (ChannelSessionState.Stopping or ChannelSessionState.Stopped);
                var head = state == ChannelSessionState.Receiving &&
                    _headAvailable.Task.IsCompletedSuccessfully && _headAvailable.Task.Result;
                var port = ingest.Port ?? (_options.Ingest.Port > 0 ? _options.Ingest.Port : (int?)null);
                var current = new ObsPublishStatusSnapshot(ChannelId, SessionId, _publishingRevision, state,
                    active && ingest.Listening, active && ingest.PublisherConnected, head,
                    port is null ? null : $"rtmp://127.0.0.1:{port}/{_options.Ingest.Application}",
                    _options.Ingest.StreamName, head ? Volatile.Read(ref _httpStreamUrl) : null,
                    result?.EndReason, result?.Rejection?.Stage, result?.Rejection?.Reason);
                // Serialize observed revisions across tracker, TCP and terminal facts.
                // TCP accept/listener changes need not advance ChannelStatusTracker.
                // These are observation revisions, not an atomic network-event log.
                if (_lastPublishingStatus is null || current != _lastPublishingStatus)
                {
                    _publishingRevision = checked(_publishingRevision + 1);
                    _lastPublishingStatus = current with { Revision = _publishingRevision };
                }
                return _lastPublishingStatus;
            }
        }
    }

    public Task<ObsPublishChannelResult> Completion
    {
        get
        {
            lock (_gate)
            {
                return _completion ?? throw new InvalidOperationException("Publish host has not started.");
            }
        }
    }

    public void Start(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (_completion is not null)
                throw new InvalidOperationException("A publish host supports one generation.");
            _completion = RunAsync(cancellationToken);
        }
    }

    private async Task<ObsPublishChannelResult> RunAsync(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        ObsRtmpIngestResult? ingestResult = null;
        Task<PcpDownstreamRelayServerResult>? relayTask = null;
        ObsPublishChannelEndReason reason = ObsPublishChannelEndReason.StartupFailed;
        var relayDrain = PcpRelayDrainOutcome.NotRequired;
        var httpDrained = false;
        var clean = true;
        var started = false;
        _tracker.TransitionTo(ChannelSessionState.Connecting);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (_ownsHttp) _http.Start();
            _httpSource = (PcpHttpFlvPlaybackSession)_http.Create(ChannelId, SessionId);
            Volatile.Write(ref _httpStreamUrl, _http.StreamUrlFor(ChannelId));
            _httpSink = new HttpRelayChild(_httpSource, ChannelId, SessionId);
            _httpChild = _fanout.AddChild(_httpSink);
            if (!_registry.TryRegister(ChannelId, _recording, out _registration))
                throw new InvalidOperationException("The channel is already registered or the registry is full.");
            lock (_gate) { _registered = true; }
            _tracker.TransitionTo(ChannelSessionState.Negotiating);
            _tracker.UpdateMetadata(new ChannelMetadataSnapshot(
                name: _options.ChannelName, contentType: "FLV", streamExtension: "flv"));
            relayTask = _relay.RunAsync(lifetime.Token);
            _ingest.Start(lifetime.Token);
            started = true;
            var first = await Task.WhenAny(_ingest.Completion, relayTask, _httpChild.Completion).ConfigureAwait(false);
            if (first == _ingest.Completion)
            {
                ingestResult = await _ingest.Completion.ConfigureAwait(false);
                reason = MapReason(ingestResult.EndReason);
            }
            else
            {
                reason = lifetime.IsCancellationRequested ? ObsPublishChannelEndReason.HostCancelled :
                    first == relayTask ? ObsPublishChannelEndReason.DownstreamFailed :
                    ObsPublishChannelEndReason.ConsumerFaulted;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            reason = ObsPublishChannelEndReason.HostCancelled;
        }
        catch (Exception)
        {
            reason = started ? ObsPublishChannelEndReason.InternalFailure : ObsPublishChannelEndReason.StartupFailed;
        }
        finally
        {
            lock (_gate)
            {
                _registered = false;
                _recording.StopAccepting();
                _registration?.Dispose();
                _registration = null;
            }
            if (reason is not (ObsPublishChannelEndReason.PublisherStopped or
                ObsPublishChannelEndReason.Disconnected or ObsPublishChannelEndReason.HostCancelled))
            {
                _tracker.RecordFault(new ChannelFaultSnapshot(
                    reason == ObsPublishChannelEndReason.ProtocolRejected ? ChannelFaultCategory.ProtocolViolation :
                    reason == ObsPublishChannelEndReason.ConsumerFaulted ? ChannelFaultCategory.ConsumerFailed :
                    ChannelFaultCategory.InternalFailure,
                    started ? ChannelFaultStage.Receiving : ChannelFaultStage.Connecting, 1, 0));
            }
            _tracker.TransitionTo(ChannelSessionState.Stopping);
            _headAvailable.TrySetResult(false);
            try
            {
                if (reason != ObsPublishChannelEndReason.PublisherStopped)
                    await lifetime.CancelAsync().ConfigureAwait(false);
                await _ingest.DisposeAsync().ConfigureAwait(false);
                if (started)
                    ingestResult ??= await _ingest.Completion.ConfigureAwait(false);
                if (reason == ObsPublishChannelEndReason.PublisherStopped)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    var drained = await _fanout.WaitForIdleAsync(_options.DrainTimeout, lifetime.Token).ConfigureAwait(false);
                    relayDrain = drained ? PcpRelayDrainOutcome.Completed : PcpRelayDrainOutcome.TimedOut;
                    if (drained && _httpSource is not null)
                        httpDrained = await _httpSource.CompleteAsync(_options.DrainTimeout, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                relayDrain = PcpRelayDrainOutcome.Canceled;
                reason = ObsPublishChannelEndReason.HostCancelled;
            }
            catch (Exception)
            {
                clean = false;
            }
            // Every cleanup is attempted even if another resource fails.
            async Task ReleaseAsync(Func<ValueTask> release)
            {
                try { await release().ConfigureAwait(false); }
                catch (Exception) { clean = false; }
            }
            await ReleaseAsync(() => new ValueTask(lifetime.CancelAsync())).ConfigureAwait(false);
            await ReleaseAsync(() => _recording.CompleteAsync(
                httpDrained && relayDrain == PcpRelayDrainOutcome.Completed ? null :
                new IOException("Publish generation ended without a completed drain."))).ConfigureAwait(false);
            await ReleaseAsync(_relay.DisposeAsync).ConfigureAwait(false);
            await ReleaseAsync(_fanout.DisposeAsync).ConfigureAwait(false);
            if (_httpSource is not null) await ReleaseAsync(_httpSource.DisposeAsync).ConfigureAwait(false);
            if (_ownsHttp) await ReleaseAsync(_http.DisposeAsync).ConfigureAwait(false);
            _tracker.TransitionTo(ChannelSessionState.Stopped);
        }
        PcpDownstreamRelayServerResult? relayResult = null;
        if (relayTask is not null)
        {
            try { relayResult = await relayTask.ConfigureAwait(false); }
            catch (Exception) { clean = false; }
        }
        clean &= _relay.Snapshot.CleanupIncompleteCount == 0;
        var result = new ObsPublishChannelResult(reason, _acceptedPackets, _acceptedBytes, _heads, _data,
            relayDrain, httpDrained, clean, ingestResult, relayResult)
        { Rejection = ingestResult?.Rejection };
        Volatile.Write(ref _result, result);
        return result;
    }

    private async ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken cancellationToken)
    {
        if (packet.ChannelId != ChannelId || packet.SessionId != SessionId ||
            packet.Sequence != _acceptedPackets || packet.StreamPosition != _acceptedBytes ||
            packet.Discontinuity is not null || packet.Type is not (0x64616568U or 0x61746164U) ||
            (packet.Type == 0x64616568U ? _heads != 0 : _heads != 1))
            throw new InvalidDataException("Packet is outside the current publish generation.");
        var accepted = _httpSink!.Expect(packet.Sequence);
        await _fanout.ConsumeAsync(packet, cancellationToken).ConfigureAwait(false);
        // The mandatory existing relay child must accept into HTTP's bounded
        // consumer before upstream accounting advances. Browser I/O is separate.
        await accepted.WaitAsync(cancellationToken).ConfigureAwait(false);
        _acceptedPackets++;
        _acceptedBytes += packet.Payload.Length;
        if (packet.Type == 0x64616568U)
        {
            _heads++;
            _tracker.TransitionTo(ChannelSessionState.Receiving);
            _recording.StartAccepting();
            _headAvailable.TrySetResult(true);
        }
        else _data++;
        _tracker.AdvanceStreamPosition(_acceptedBytes);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposeTask ??= DisposeCoreAsync();
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        // Starting with an already canceled lifetime executes the same full
        // cleanup for a never-started host, without leaving an owned fanout.
        _completion ??= RunAsync(_stop.Token);
        await _completion.ConfigureAwait(false);
        _stop.Dispose();
    }

    private static ObsPublishChannelEndReason MapReason(ObsRtmpIngestEndReason reason) => reason switch
    {
        ObsRtmpIngestEndReason.PublisherStopped => ObsPublishChannelEndReason.PublisherStopped,
        ObsRtmpIngestEndReason.Disconnected => ObsPublishChannelEndReason.Disconnected,
        ObsRtmpIngestEndReason.ProtocolRejected => ObsPublishChannelEndReason.ProtocolRejected,
        ObsRtmpIngestEndReason.ConsumerRejected => ObsPublishChannelEndReason.ConsumerFaulted,
        ObsRtmpIngestEndReason.Cancelled => ObsPublishChannelEndReason.HostCancelled,
        ObsRtmpIngestEndReason.TimedOut => ObsPublishChannelEndReason.TimedOut,
        ObsRtmpIngestEndReason.TransportFailed => ObsPublishChannelEndReason.TransportFailed,
        _ => ObsPublishChannelEndReason.InternalFailure,
    };

    private sealed class PublishConsumer(ObsPublishChannelHost owner) : IPcpChannelPacketConsumer
    {
        public ValueTask ConsumeAsync(PcpChannelPacket packet, CancellationToken token) => owner.ConsumeAsync(packet, token);
    }

    private sealed class HttpRelayChild(
        PcpHttpFlvPlaybackSession source, ChannelId channelId, ChannelSessionId sessionId) : IPcpRelayChildSink
    {
        private TaskCompletionSource? _accepted;
        private long _sequence;

        public Task Expect(long sequence)
        {
            _sequence = sequence;
            var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _accepted, accepted);
            return accepted.Task;
        }

        public async ValueTask SendAsync(PcpRelayPacket packet, CancellationToken cancellationToken)
        {
            var accepted = Volatile.Read(ref _accepted) ?? throw new InvalidOperationException("Unsolicited HTTP relay packet.");
            try
            {
                if (packet.Sequence != _sequence) throw new InvalidDataException("HTTP relay packet is out of order.");
                await source.ConsumeAsync(new PcpChannelPacket(
                    channelId, sessionId, packet.Sequence, packet.Type, packet.WirePosition,
                    packet.StreamPosition, packet.Payload, packet.Discontinuity), cancellationToken).ConfigureAwait(false);
                accepted.TrySetResult();
            }
            catch (Exception exception)
            {
                accepted.TrySetException(exception);
                throw;
            }
        }
    }
}
