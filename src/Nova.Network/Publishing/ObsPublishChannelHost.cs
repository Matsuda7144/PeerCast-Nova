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
public sealed partial class ObsPublishChannelHost : IAsyncDisposable
{
    private readonly PublishingAnnounceOwner? _announceOwner;
    private readonly TimeProvider _announceTime;
    private long? _announceStart, _announceStopped;
    private CancellationToken _announceCallerCancellation;
    private readonly object _announceStateGate = new();
    private PublishingAnnounceRuntimeSnapshot? _lastAnnounceRuntime;
    private PublishingAnnounceSession? _announce;
    private bool _announceFailed;
    public PublishingAnnounceStatus AnnounceStatus => _announce?.Status ?? new(_announceFailed ? "Failed" : _options.Announce.Enabled ? "WaitingForListener" : "Disabled", false, false, false, true);
    private readonly object _gate = new();
    private readonly ObsPublishChannelOptions _options;
    private readonly PcpHttpFlvPlaybackHost _http;
    private readonly bool _ownsHttp;
    private readonly PcpRelayFanout _fanout;
    private readonly PcpDownstreamRelayServer _relay;
    private readonly IObsPublishLanInterfaces _interfaces;
    private readonly Func<ChannelId, ChannelSessionId, PcpRelayFanout, IPcpDownstreamRelayRegistration>? _registerRelay;
    private IPcpDownstreamRelayRegistration? _relayRegistration;
    private readonly bool _useLoopbackAdmission;
    private ObsPublishLanBinding? _lanBinding;
    private ObsPublishWanBinding? _wanBinding;
    private ObsPublishWanFailure _wanFailure;
    private readonly IObsPublishInterfaceChanges _interfaceChanges;
    private ObsPublishWanNetworkLifetime? _wanNetwork;
    private Task<bool>? _wanNetworkCleanup;
    private ObsPublishLanFailure _lanFailure;
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
        RecordingStreamSourceLeaseRegistry? recordingRegistry = null,
        IObsPublishLanInterfaces? interfaces = null,
        Func<ChannelId, ChannelSessionId, PcpRelayFanout, IPcpDownstreamRelayRegistration>? registerRelay = null,
        IObsPublishInterfaceChanges? interfaceChanges = null, PublishingAnnounceOwner? announceOwner = null,
        bool useLoopbackAdmission = false)
        : this(options, channelId, httpHost, recordingRegistry, null, null, interfaces, registerRelay, interfaceChanges,
            announceOwner: announceOwner, useLoopbackAdmission: useLoopbackAdmission)
    {
    }

    internal ObsPublishChannelHost(
        ObsPublishChannelOptions? options, ChannelId? channelId,
        PcpHttpFlvPlaybackHost? httpHost, RecordingStreamSourceLeaseRegistry? recordingRegistry,
        Func<IAsyncDisposable?>? acquireRecordingOwner,
        PcpRecordingSourceCleanupObserver? recordingCleanupObserver,
        IObsPublishLanInterfaces? interfaces = null,
        Func<ChannelId, ChannelSessionId, PcpRelayFanout, IPcpDownstreamRelayRegistration>? registerRelay = null,
        IObsPublishInterfaceChanges? interfaceChanges = null,
        Func<IPEndPoint, IPEndPoint>? bindForTests = null,
        PcpWanIngressOptions? wanIngressForTests = null, PublishingAnnounceOwner? announceOwner = null, TimeProvider? announceTime = null,
        bool useLoopbackAdmission = false)
    {
        _announceTime = announceTime ?? TimeProvider.System;
        _announceOwner = announceOwner;
        _interfaceChanges = interfaceChanges ?? SystemObsPublishInterfaceChanges.Instance;
        _interfaces = interfaces ?? SystemObsPublishLanInterfaces.Instance;
        _registerRelay = registerRelay;
        _useLoopbackAdmission = useLoopbackAdmission;
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
        InitialMetadata = _options.CreateMetadata();
        ChannelId = channelId ?? new ChannelId(Guid.NewGuid());
        SessionId = new ChannelSessionId(Guid.NewGuid());
        _tracker = new ChannelStatusTracker(ChannelId, SessionId);
        _registry = recordingRegistry ?? new RecordingStreamSourceLeaseRegistry(1);
        _http = httpHost ?? new PcpHttpFlvPlaybackHost(_options.Http);
        _ownsHttp = httpHost is null;
        _fanout = new PcpRelayFanout(_options.Fanout, replayHeadForNewChildren: true);
        _recording = new PcpRelayFanoutRecordingStreamSourceLeaseFactory(ChannelId, SessionId, _fanout,
            acquireOwnerLease: acquireRecordingOwner)
        { CleanupObserver = recordingCleanupObserver };
        var endpoint = new IPEndPoint(_options.Wan.Enabled ? IPAddress.Parse(_options.Wan.Address) : _options.Lan.Enabled ? IPAddress.Parse(_options.Lan.Address) : IPAddress.Loopback,
            _options.Wan.Enabled ? _options.Wan.Port : _options.Lan.Enabled ? _options.Lan.Port : _options.RelayPort);
        var wanControl = _options.Wan.Enabled ? wanIngressForTests ?? new PcpWanIngressOptions() : null;
        _relay = new PcpDownstreamRelayServer(
            bindForTests?.Invoke(endpoint) ?? endpoint, _fanout, ChannelId, SessionId.Value,
            initialMetadata: InitialMetadata, admission: _options.Lan.Enabled || _options.Wan.Enabled || _useLoopbackAdmission ? new LanAdmission(this) : null,
            acceptRemoteAddress: _options.Wan.Enabled ? address => CanAcceptWan && wanControl!.SourceAllowed(address)
                : _options.Lan.Enabled ? address => Volatile.Read(ref _lanBinding)?.Accepts(address) == true : null,
            wanOptions: wanControl);
        _ingest = new ObsRtmpIngestHost(ChannelId, SessionId, new PublishConsumer(this), _options.Ingest);
    }

    public ChannelId ChannelId { get; }
    public ChannelSessionId SessionId { get; }
    public ChannelMetadataSnapshot InitialMetadata { get; }
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
                    result?.EndReason, result?.Rejection?.Stage, result?.Rejection?.Reason)
                { LanRelay = ReadLanStatus(active), WanRelay = ReadWanStatus(active), Announce = AnnounceStatus with { InboundPingObserved = _relay.Snapshot.WanIngress?.PingResponses > 0 } };
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
            _announceCallerCancellation = cancellationToken;
            _announceStart = _announceTime.GetTimestamp();
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
            if (_options.Lan.Enabled || _options.Wan.Enabled)
            {
                // Revalidate at explicit Start before allocating any listener.
                if (_options.Wan.Enabled) _wanBinding = _options.Wan.Resolve(_interfaces);
                else _lanBinding = _options.Lan.Resolve(_interfaces);
            }
            if (_options.Lan.Enabled || _options.Wan.Enabled || _useLoopbackAdmission)
            {
                _relayRegistration = _registerRelay?.Invoke(ChannelId, SessionId, _fanout)
                    ?? throw new ObsPublishLanException(ObsPublishLanFailure.AdmissionUnavailable);
                if (_relayRegistration.ChannelId != ChannelId || _relayRegistration.SessionId != SessionId)
                    throw new ObsPublishLanException(ObsPublishLanFailure.AdmissionUnavailable);
                lifetime.Token.ThrowIfCancellationRequested();
            }
            if (_ownsHttp) _http.Start();
            _httpSource = (PcpHttpFlvPlaybackSession)_http.Create(ChannelId, SessionId);
            Volatile.Write(ref _httpStreamUrl, _http.StreamUrlFor(ChannelId));
            _httpSink = new HttpRelayChild(_httpSource, ChannelId, SessionId);
            _httpChild = _fanout.AddChild(_httpSink);
            if (!_registry.TryRegister(ChannelId, _recording, out _registration))
                throw new InvalidOperationException("The channel is already registered or the registry is full.");
            lock (_gate) { _registered = true; }
            _tracker.TransitionTo(ChannelSessionState.Negotiating);
            _tracker.UpdateMetadata(InitialMetadata);
            relayTask = _relay.RunAsync(lifetime.Token);
            _relayRunTask = relayTask;
            if (_wanBinding is not null) _wanNetwork = new(_wanBinding, _interfaces, _interfaceChanges);
            _ingest.Start(lifetime.Token);
            started = true;
            if (_options.Announce.Enabled && _announceOwner is not null && _options.Wan.Enabled && ReadWanStatus(true)?.ListenerReady == true &&
                _options.Wan.AdvertisedAddress.Length != 0 && _options.Wan.AdvertisedPort > 0)
            {
                try
                {
                    _announce = _announceOwner.Start(_options.Announce, ChannelId, SessionId, InitialMetadata, RelayEndPoint,
                    new IPEndPoint(IPAddress.Parse(_options.Wan.AdvertisedAddress), _options.Wan.AdvertisedPort),
                    () => !lifetime.IsCancellationRequested && CanAcceptWan && ReadWanStatus(true)?.ListenerReady == true &&
                        (_relayRegistration as IPcpDownstreamRelayCapacity)?.IsCurrent == true, ReadAnnounceRuntime);
                }
                catch { _announceFailed = true; /* Announce failure does not stop ingest or relay. */ }
            }
            Task[] endings = [_ingest.Completion, relayTask, _httpChild.Completion, _localFinishRequested.Task];
            var first = await Task.WhenAny(_wanNetwork is null ? endings : endings.Append(_wanNetwork.Invalidated)).ConfigureAwait(false);
            if (_wanNetwork?.Invalidated.IsCompleted == true) _wanFailure = ObsPublishWanFailure.NetworkChanged;
            await ObserveTerminationAsync(ObsPublishTerminationPoint.BeforeEndSelection).ConfigureAwait(false);
            if (first == _ingest.Completion)
            {
                ingestResult = await _ingest.Completion.ConfigureAwait(false);
                reason = MapReason(ingestResult.EndReason);
            }
            else
            {
                bool forcedOwnerStop;
                lock (_gate)
                {
                    // Independent forced cleanup may finish a child before cancellation
                    // reaches this linked lifetime. Require actual owner-stop evidence;
                    // accept/sink failures do not qualify, and cleanup is still joined below.
                    forcedOwnerStop = _forceRequested &&
                        ((first == relayTask && relayTask.IsCompletedSuccessfully &&
                            relayTask.Result.EndReason == PcpDownstreamRelayServerEndReason.Stopped) ||
                        (first == _httpChild.Completion &&
                            _httpChild.Snapshot is { EndReason: PcpRelayChildEndReason.FanoutStopped, CleanupFailed: false }));
                }
                reason = first == _localFinishRequested.Task ? ObsPublishChannelEndReason.LocalTerminated :
                    first == _wanNetwork?.Invalidated ? ObsPublishChannelEndReason.TransportFailed :
                    lifetime.IsCancellationRequested || forcedOwnerStop ? ObsPublishChannelEndReason.HostCancelled :
                    first == relayTask ? ObsPublishChannelEndReason.DownstreamFailed :
                    ObsPublishChannelEndReason.ConsumerFaulted;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            reason = ObsPublishChannelEndReason.HostCancelled;
        }
        catch (ObsPublishLanException exception)
        {
            if (_options.Wan.Enabled) _wanFailure = exception.Failure == ObsPublishLanFailure.BandwidthUnsupported
                ? ObsPublishWanFailure.BandwidthUnsupported : ObsPublishWanFailure.AdmissionUnavailable;
            else _lanFailure = exception.Failure;
            reason = ObsPublishChannelEndReason.StartupFailed;
        }
        catch (ObsPublishWanException exception)
        {
            _wanFailure = exception.Failure;
            reason = ObsPublishChannelEndReason.StartupFailed;
        }
        catch (Exception)
        {
            if (_options.Lan.Enabled && !started) _lanFailure = ObsPublishLanFailure.BindFailed;
            if (_options.Wan.Enabled && !started) _wanFailure = ObsPublishWanFailure.BindFailed;
            reason = started ? ObsPublishChannelEndReason.InternalFailure : ObsPublishChannelEndReason.StartupFailed;
        }
        finally
        {
            LocalFinish? local;
            lock (_gate)
            {
                _ending = true;
                if (_announceStart is not null) _announceStopped ??= _announceTime.GetTimestamp();
                local = _localFinish;
                _registered = false;
                _recording.StopAccepting();
                _registration?.Dispose();
                _registration = null;
            }
            if (_announce is not null)
                clean &= await _announce.StopAsync(reason == ObsPublishChannelEndReason.PublisherStopped ? PcpAnnounceStopReason.ObsNormalEos : PcpAnnounceStopReason.Finish).ConfigureAwait(false);
            try { _relayRegistration?.Dispose(); } // Retire without freeing live reservations.
            catch { clean = false; }
            await ObserveTerminationAsync(ObsPublishTerminationPoint.RecordingAdmissionClosed).ConfigureAwait(false);
            if (local is not null)
            {
                var terminal = await FinishLocallyAsync(local, started, lifetime.Token).ConfigureAwait(false);
                ingestResult = terminal.Ingress?.FinalResult;
                reason = terminal.Outcome switch
                {
                    ObsPublishLocalTerminationOutcome.LocalCompleted => ObsPublishChannelEndReason.LocalTerminated,
                    ObsPublishLocalTerminationOutcome.TimedOut => ObsPublishChannelEndReason.TimedOut,
                    ObsPublishLocalTerminationOutcome.Cancelled => ObsPublishChannelEndReason.HostCancelled,
                    ObsPublishLocalTerminationOutcome.DeliveryFailed => ObsPublishChannelEndReason.ConsumerFaulted,
                    _ => ObsPublishChannelEndReason.InternalFailure,
                };
                clean = terminal.CleanupCompleted;
                relayDrain = terminal.Relay?.Outcome == PcpRelayQuiescenceOutcome.Drained
                    ? PcpRelayDrainOutcome.Completed : PcpRelayDrainOutcome.NotRequired;
                httpDrained = terminal.Http?.Outcome == PcpRelayCompletionOutcome.Completed;
                _localFinishResult = terminal;
            }
            else
            {
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
                    if ((_options.Lan.Enabled || _options.Wan.Enabled) && _relayRunTask is not null)
                        await _relay.StopAcceptingAsync().ConfigureAwait(false);
                    if (reason != ObsPublishChannelEndReason.PublisherStopped)
                        await lifetime.CancelAsync().ConfigureAwait(false);
                    clean &= await ReleaseOwned(ObsPublishTerminationPoint.IngressCleanup, _ingest.DisposeAsync).ConfigureAwait(false);
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
                async Task ReleaseAsync(ObsPublishTerminationPoint point, Func<ValueTask> release)
                {
                    clean &= await ReleaseOwned(point, release).ConfigureAwait(false);
                }
                await ReleaseAsync(ObsPublishTerminationPoint.LifetimeCancellation, () => new ValueTask(lifetime.CancelAsync())).ConfigureAwait(false);
                await ReleaseAsync(ObsPublishTerminationPoint.RecordingNotification, () => _recording.CompleteAsync(
                    httpDrained && relayDrain == PcpRelayDrainOutcome.Completed ? null :
                    new IOException("Publish generation ended without a completed drain."))).ConfigureAwait(false);
                await ReleaseAsync(ObsPublishTerminationPoint.RelayCleanup, _relay.DisposeAsync).ConfigureAwait(false);
                await ReleaseAsync(ObsPublishTerminationPoint.FanoutCleanup, _fanout.DisposeAsync).ConfigureAwait(false);
                if (_httpSource is not null) await ReleaseAsync(ObsPublishTerminationPoint.HttpSourceCleanup, _httpSource.DisposeAsync).ConfigureAwait(false);
                if (_ownsHttp) await ReleaseAsync(ObsPublishTerminationPoint.HttpListenerCleanup, _http.DisposeAsync).ConfigureAwait(false);
                _tracker.TransitionTo(ChannelSessionState.Stopped);
            }
        }
        PcpDownstreamRelayServerResult? relayResult = null;
        if (relayTask is not null)
        {
            try
            {
                if (_localFinishResult is null || relayTask.IsCompleted)
                    relayResult = await relayTask.ConfigureAwait(false);
                else clean = false;
            }
            catch (Exception) { clean = false; }
        }
        clean &= _relay.Snapshot.CleanupIncompleteCount == 0 && RelayReservationsReleased;
        clean &= await StopWanNetworkAsync().ConfigureAwait(false);
        if (_localFinishResult is null && _forceRequested)
        {
            // All independent releases were already started. A noncooperative
            // cancellation may keep this terminal task pending, never successful.
            if (_forcedCancellationTask is not null) clean &= await _forcedCancellationTask.ConfigureAwait(false);
            if (_forcedNotificationTask is not null) clean &= await _forcedNotificationTask.ConfigureAwait(false);
            clean &= _recording.Evidence.CleanupCompleted;
        }
        if (_localFinishResult is not null)
        {
            _localFinishResult = await FinalizeLocalResultAsync(_localFinishResult, clean, lifetime.Token).ConfigureAwait(false);
            reason = _localFinishResult.Outcome switch
            {
                ObsPublishLocalTerminationOutcome.LocalCompleted => ObsPublishChannelEndReason.LocalTerminated,
                ObsPublishLocalTerminationOutcome.TimedOut => ObsPublishChannelEndReason.TimedOut,
                ObsPublishLocalTerminationOutcome.Cancelled => ObsPublishChannelEndReason.HostCancelled,
                ObsPublishLocalTerminationOutcome.DeliveryFailed => ObsPublishChannelEndReason.ConsumerFaulted,
                _ => ObsPublishChannelEndReason.InternalFailure,
            };
            clean = _localFinishResult.CleanupCompleted;
        }
        var result = new ObsPublishChannelResult(reason, _acceptedPackets, _acceptedBytes, _heads, _data,
            relayDrain, httpDrained, clean, ingestResult, relayResult)
        { Rejection = ingestResult?.Rejection, LocalTermination = _localFinishResult };
        Volatile.Write(ref _result, result);
        if (_localFinishResult is not null)
        {
            _localFinish!.Completion.TrySetResult(_localFinishResult);
            _localFinish.Deadline.Dispose();
        }
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
            lock (_gate) { if (!_ending) _recording.StartAccepting(); }
            _headAvailable.TrySetResult(true);
            NotifyAnnounceRuntimeChanged();
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
        await Task.Yield();
        bool committed;
        lock (_gate)
        {
            committed = _localSuccessCommitted ||
                (_completion?.IsCompletedSuccessfully == true && _result?.ShutdownCompleted == true);
        }
        if (!committed)
        {
            _ = BeginLocalCleanup(force: true);
            // Never-started disposal must not race an as-yet-unscheduled cancel
            // into implicit startup. No owner callback exists before Start.
            if (_completion is null)
            {
                await _forcedCancellationTask!.ConfigureAwait(false);
                _completion = RunAsync(_stop.Token);
            }
        }
        try
        {
            if (_completion is not null) await _completion.ConfigureAwait(false);
            if (_localCleanupTask is not null)
            {
                var cleanup = await ObserveLocalCleanupAsync(CancellationToken.None).ConfigureAwait(false);
                if (!cleanup.Completed) throw new InvalidOperationException("Publishing owner cleanup is incomplete.");
            }
        }
        finally
        {
            // The CTS remains owned until its actual callbacks have terminated,
            // even if another cleanup stage faults first.
            try { if (_forcedCancellationTask is not null) await _forcedCancellationTask.ConfigureAwait(false); }
            finally { _stop.Dispose(); }
        }
    }

    private bool RelayReservationsReleased => _relayRegistration is null || _relayRegistration.ReservationCount == 0;
    private void NotifyAnnounceRuntimeChanged() => Volatile.Read(ref _announce)?.NotifyRuntimeChanged();
    internal PublishingAnnounceRuntimeSnapshot ReadAnnounceRuntime()
    {
        var live = CanAcceptWan && !_announceCallerCancellation.IsCancellationRequested && _announceStart is not null;
        uint uptime;
        lock (_gate)
        {
            if (!live && _announceStart is not null) _announceStopped ??= _announceTime.GetTimestamp();
            uptime = _announceStart is { } start ? (uint)Math.Clamp(
                _announceTime.GetElapsedTime(start, _announceStopped ?? _announceTime.GetTimestamp()).TotalSeconds,
                0, uint.MaxValue) : 0;
        }
        var relay = _relay.ReadRuntimeCapacity();
        var canReserve = false;
        var currentAdmission = false;
        try
        {
            var capacity = _relayRegistration as IPcpDownstreamRelayCapacity;
            currentAdmission = capacity?.IsCurrent == true;
            canReserve = capacity?.CanReserve == true;
        }
        catch { /* Unknown capacity is not permission to announce availability. */ }
        var value = PublishingAnnounceRuntimeSnapshot.Create(PublishingStatus, live && currentAdmission, canReserve,
            relay.CanAccept, _fanout.CanAddChild, relay.RelayCount, uptime) with
        { Revision = 0 };
        lock (_announceStateGate)
        {
            if (_lastAnnounceRuntime is not null && value == (_lastAnnounceRuntime with { Revision = 0 })) return _lastAnnounceRuntime;
            return _lastAnnounceRuntime = value with { Revision = checked((_lastAnnounceRuntime?.Revision ?? 0) + 1) };
        }
    }
    private bool CanAcceptWan => !Volatile.Read(ref _ending) && Volatile.Read(ref _disposeTask) is null &&
        !_localFinishRequested.Task.IsCompleted && !_stop.IsCancellationRequested && _wanNetwork?.Invalidated.IsCompleted != true;

    private Task<bool> StopWanNetworkAsync()
    {
        lock (_gate) return _wanNetworkCleanup ??= StopCoreAsync();
        async Task<bool> StopCoreAsync()
        {
            try
            {
                if (_wanNetwork is not null) await _wanNetwork.DisposeAsync().AsTask().WaitAsync(_options.DrainTimeout).ConfigureAwait(false);
                return true;
            }
            catch { return false; }
        }
    }

    private ObsPublishWanStatus? ReadWanStatus(bool active)
    {
        if (!_options.Wan.Enabled) return null;
        var incomplete = _result is { ShutdownCompleted: false };
        var ready = active && _wanFailure == ObsPublishWanFailure.None && _wanNetwork?.Invalidated.IsCompleted != true &&
            _relay.Snapshot.State == PcpDownstreamRelayServerState.Running;
        var bound = ready ? _relay.LocalEndPoint : null;
        ready &= bound?.Address.ToString() == _options.Wan.Address && bound.Port == _options.Wan.Port;
        return new(true, ready, ready ? bound!.Address.ToString() : null, ready ? bound!.Port : null,
            _options.Wan.AdvertisedAddress.Length == 0 ? null : _options.Wan.AdvertisedAddress,
            _options.Wan.AdvertisedPort == 0 ? null : _options.Wan.AdvertisedPort, _wanFailure, incomplete);
    }

    private ObsPublishLanStatus ReadLanStatus(bool active)
    {
        var ready = _options.Lan.Enabled && active && _relay.Snapshot.State == PcpDownstreamRelayServerState.Running;
        var endpoint = ready ? _relay.LocalEndPoint : null;
        return new(_options.Lan.Enabled, ready, endpoint?.Address.ToString(), endpoint?.Port, _lanFailure);
    }

    private sealed class LanAdmission(ObsPublishChannelHost owner) : IPcpDownstreamRelayAdmission
    {
        public ChannelId ChannelId => owner.ChannelId;
        public bool TryReserve(long connectionId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IDisposable? reservation, CancellationToken cancellationToken = default)
        {
            reservation = null;
            if (owner._options.Wan.Enabled && !owner.CanAcceptWan) return false;
            return owner._relayRegistration?.TryReserve(connectionId, out reservation, cancellationToken) == true;
        }
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
