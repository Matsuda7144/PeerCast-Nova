using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Transport;

namespace PeerCast.Nova.App;

/// <summary>
/// GUI Next's public-channel lifetime, separate from its playback/recording
/// loopback owners. Construction and preparation perform no network I/O.
/// The explicit GUI Relay command activates loopback/port 0. A separate opt-in
/// startup setting also registers viewing owners with the shared public listener.
/// </summary>
internal sealed partial class GenesisGuiNextPublicChannelComposition : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly GenesisDownstreamRelayOwnerRegistry _registry;
    private readonly HashSet<Owner> _owners = [];
    private readonly Dictionary<ChannelId, Owner> _current = [];
    private Task? _disposeTask;

    public GenesisGuiNextPublicChannelComposition(GenesisDownstreamRelayOwnerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    internal GenesisDownstreamRelayOwnerRegistry Registry => _registry;
    public int OwnerCount { get { lock (_gate) { return _owners.Count; } } }
    public bool IsDisposed { get { lock (_gate) { return _disposeTask is not null; } } }

    /// <summary>
    /// The explicit receive/relay operation supplies Viewing classification.
    /// This reuses the same coordinator as Daily public relay, not Daily's
    /// UI/settings/NAT wrapper or the GUI Next media loopback composition.
    /// </summary>
    public async ValueTask<Owner> PrepareViewingAsync(GenesisChannelItem channel,
        IPEndPoint downstreamEndPoint, PcpRelayCoordinatorOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var endpoint = CopyEndpoint(downstreamEndPoint);
        cancellationToken.ThrowIfCancellationRequested();
        if (!DailyPcpPlaybackSourceParser.TryParse(channel.Source, out var source) || source is null)
            throw new ArgumentException("An explicit upstream PCP channel source is required.", nameof(channel));

        var tracker = new ChannelStatusTracker(source.ChannelId, new ChannelSessionId(Guid.NewGuid()));
        var owner = new Owner(this, source.ChannelId, tracker.Snapshot.SessionId, RelayChannelKind.Viewing, endpoint);
        var coordinator = new PcpRelayCoordinator(new TcpTransportConnector(), tracker, endpoint,
            options, admission: owner);
        owner.Configure(coordinator, coordinator.DownstreamFanout,
            token => coordinator.RunAsync(source.Host, source.Port, cancellationToken: token),
            () => coordinator.LocalEndPoint, () => coordinator.DownstreamSnapshot, coordinator, tracker);
        return await RegisterAsync(owner, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Only a local producer owner can supply this identity and exact fan-out.
    /// No producer/publishing lifecycle is invented here. The producer keeps
    /// fan-out ownership; this composition owns only the downstream server.
    /// </summary>
    public async ValueTask<Owner> PrepareBroadcastAsync(ChannelId channelId, ChannelSessionId sessionId,
        PcpRelayFanout producerFanout, IPEndPoint downstreamEndPoint,
        TcpTransportOptions? transportOptions = null, PcpDownstreamChildOptions? childOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channelId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(producerFanout);
        var endpoint = CopyEndpoint(downstreamEndPoint);
        cancellationToken.ThrowIfCancellationRequested();
        var owner = new Owner(this, channelId, sessionId, RelayChannelKind.Broadcast, endpoint);
        var server = new PcpDownstreamRelayServer(endpoint, producerFanout, channelId, sessionId.Value,
            transportOptions, childOptions, admission: owner);
        owner.Configure(server, producerFanout, token => server.RunAsync(token),
            () => server.LocalEndPoint, () => server.Snapshot);
        return await RegisterAsync(owner, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<Owner> RegisterAsync(Owner owner, CancellationToken cancellationToken)
    {
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
                cancellationToken.ThrowIfCancellationRequested();
                var registration = _registry.Register(owner.ChannelId, owner.SessionId, owner.Kind, owner.Fanout,
                    () => owner.ViewingRuntime?.DeclaredBitrateKbps);
                owner.Bind(registration);
                if (_current.TryGetValue(owner.ChannelId, out var previous)) previous.Retire();
                _current[owner.ChannelId] = owner;
                _owners.Add(owner);
                return owner;
            }
        }
        catch
        {
            await owner.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void Remove(Owner owner)
    {
        lock (_gate)
        {
            _owners.Remove(owner);
            if (_current.TryGetValue(owner.ChannelId, out var current) && ReferenceEquals(current, owner))
                _current.Remove(owner.ChannelId);
        }
    }

    private static IPEndPoint CopyEndpoint(IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        // The channel's original listener remains ephemeral; fixed WAN port
        // ownership belongs to the shared public listener, never each channel.
        if (endpoint.AddressFamily != AddressFamily.InterNetwork || endpoint.Port != 0)
            throw new ArgumentException("Only explicitly selected IPv4 ephemeral endpoints are supported.", nameof(endpoint));
        return new IPEndPoint(endpoint.Address, endpoint.Port);
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            // Close every prepared owner's activation/admission synchronously.
            // A queued cleanup continuation must not leave a late-bind window.
            foreach (var owner in _owners) owner.Retire();
            return new ValueTask(_disposeTask = DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        // Publish the shared disposal task before any synchronous completion or
        // owner removal can re-enter the composition.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        Owner[] owners;
        lock (_gate) { owners = [.. _owners]; }
        List<Exception>? failures = null;
        foreach (var owner in owners)
        {
            try { await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        if (failures is { Count: > 0 })
            throw new AggregateException("Public channel owner shutdown failed.", failures);
    }

    internal sealed partial class Owner : IPcpDownstreamRelayAdmission, IPcpDownstreamRelayCapacity, IPcpDownstreamRelayTraffic, IPcpDownstreamRelayPacing, IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly GenesisGuiNextPublicChannelComposition _composition;
        private readonly IPEndPoint _requestedEndPoint;
        private GenesisDownstreamRelayOwnerRegistry.Owner? _registration;
        private IAsyncDisposable? _runtime;
        private Func<CancellationToken, Task>? _activate;
        private Func<IPEndPoint>? _endpoint;
        private Func<PcpDownstreamRelayServerSnapshot>? _snapshot;
        private Task? _runTask;
        private Task? _disposeTask;
        private bool _retired;
        private readonly TaskCompletionSource _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Owner(GenesisGuiNextPublicChannelComposition composition, ChannelId channelId,
            ChannelSessionId sessionId, RelayChannelKind kind, IPEndPoint endpoint)
        {
            _composition = composition;
            ChannelId = channelId;
            SessionId = sessionId;
            Kind = kind;
            _requestedEndPoint = endpoint;
        }

        public ChannelId ChannelId { get; }
        public ChannelSessionId SessionId { get; }
        public RelayChannelKind Kind { get; }
        public IPEndPoint RequestedEndPoint => new(_requestedEndPoint.Address, _requestedEndPoint.Port);
        public IPEndPoint LocalEndPoint => _endpoint!();
        public PcpDownstreamRelayServerSnapshot DownstreamSnapshot => _snapshot!();
        public bool ActivationStarted { get { lock (_gate) { return _runTask is not null; } } }
        public bool IsRetired { get { lock (_gate) { return _retired; } } }
        public Task Retirement => _retirement.Task;
        public bool IsDisposed { get { lock (_gate) { return _disposeTask?.IsCompleted == true; } } }
        internal PcpRelayFanout Fanout { get; private set; } = null!;
        // Existing media attachment APIs are reused only in managed exclusion
        // tests; GUI Play/Recording continue to use their original owners.
        internal PcpRelayCoordinator? ViewingRuntime { get; private set; }
        private ChannelStatusTracker? _upstreamTracker;
        public ChannelStatusSnapshot? UpstreamStatus => _upstreamTracker?.Snapshot;
        public int ReservationCount => _registration?.ReservationCount ?? 0;
        public PcpDownstreamTrafficMeter? Traffic => _registration?.Traffic;
        public PcpDownstreamPacer? Pacing => _registration?.Pacing;
        internal string CapacityStatusText => _composition.Registry.CapacityStatusText(_registration);
        public bool IsCurrent { get { lock (_gate) return !_retired && _registration?.IsCurrent == true; } }
        public bool CanReserve { get { lock (_gate) return !_retired && _registration?.CanReserve == true; } }
        // Disposal completion alone is insufficient: bounded runtime shutdown
        // can finish while a real TCP/callback lease remains outstanding.
        private bool DisposalSucceeded { get { lock (_gate) { return _disposeTask?.IsCompletedSuccessfully == true; } } }
        public bool CleanupConfirmed => DisposalSucceeded && ReservationCount == 0 &&
            (ViewingRuntime?.Completion is not { } completion ||
                completion.IsCompletedSuccessfully && completion.Result.ShutdownCompleted);

        internal void Configure(IAsyncDisposable runtime, PcpRelayFanout fanout,
            Func<CancellationToken, Task> activate, Func<IPEndPoint> endpoint,
            Func<PcpDownstreamRelayServerSnapshot> snapshot, PcpRelayCoordinator? viewingRuntime = null,
            ChannelStatusTracker? upstreamTracker = null)
        {
            _runtime = runtime;
            Fanout = fanout;
            _activate = activate;
            _endpoint = endpoint;
            _snapshot = snapshot;
            ViewingRuntime = viewingRuntime;
            _upstreamTracker = upstreamTracker;
            if (viewingRuntime is not null && (viewingRuntime.SmartRelayEnabled || PublicRequired))
                _ = _retirement.Task.ContinueWith(_ =>
                {
                    try { viewingRuntime.RequestStop(); }
                    catch (ObjectDisposedException) { }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        internal void Bind(GenesisDownstreamRelayOwnerRegistry.Owner registration)
        {
            _registration = registration;
            if (_runtime is IPcpRelayTreeSource source) registration.ObserveSource(source);
        }

        public bool TryReserve(long connectionId, [NotNullWhen(true)] out IDisposable? reservation,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_retired || _registration is null)
                {
                    reservation = null;
                    return false;
                }
                return _registration.TryReserve(connectionId, out reservation, cancellationToken);
            }
        }

        /// <summary>
        /// Explicit activation only. The GUI command authorizes loopback/port 0.
        /// </summary>
        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
                if (_retired || _runTask is not null)
                    throw new InvalidOperationException("Only the current prepared owner can activate once.");
                cancellationToken.ThrowIfCancellationRequested();
                _runTask = RunAndRetireAsync(cancellationToken);
                return _runTask;
            }
        }

        private async Task RunAndRetireAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (PublicRequired) await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                Task running;
                lock (_gate)
                {
                    if (_retired) return;
                    running = _activate!(cancellationToken);
                }
                if (PublicRequired)
                {
                    try
                    {
                        if (await ViewingRuntime!.WaitForReadinessAsync(cancellationToken).ConfigureAwait(false) != PcpRelayCoordinatorReadinessOutcome.Ready)
                        {
                            if (IsRetired) throw new OperationCanceledException(cancellationToken);
                            throw new IOException("Local owner did not start.");
                        }
                        CancellationTokenSource publicLifetime;
                        lock (_gate)
                        {
                            if (_retired) throw new OperationCanceledException(cancellationToken);
                            publicLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _publicStop.Token);
                            // Record the complete startup before invoking the provider:
                            // Stop must join even a delayed provider, not just route acquisition.
                            _publicAcquisition = Task.Run(() => _composition.GetPublicHost()
                                .AcquireAsync(BindPublicRoute, publicLifetime.Token), CancellationToken.None);
                        }
                        using (publicLifetime) { _publicLease = await _publicAcquisition.ConfigureAwait(false); }
                        lock (_gate)
                        {
                            if (_retired) throw new OperationCanceledException(cancellationToken);
                            _publicReady.TrySetResult();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _publicReady.TrySetCanceled(cancellationToken);
                        if (IsRetired || cancellationToken.IsCancellationRequested)
                        { await running.ConfigureAwait(false); return; }
                        throw;
                    }
                    catch (Exception)
                    {
                        var error = new GenesisPublicRelayStartException();
                        _publicReady.TrySetException(error);
                        throw error;
                    }
                }
                await running.ConfigureAwait(false);
            }
            finally { await DisposeAsync().ConfigureAwait(false); }
        }

        public void Retire()
        {
            lock (_gate)
            {
                if (_retired) return;
                _retired = true;
                _publicCancellation = _publicStop.CancelAsync();
                if (_publicRoute is { } route) _ = route.DisposeAsync().AsTask();
                _publicReady.TrySetCanceled(CancellationToken.None);
                _registration?.Dispose();
                _retirement.TrySetResult();
            }
        }

        public ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                Retire();
                return new ValueTask(_disposeTask ??= DisposeCoreAsync());
            }
        }

        private async Task DisposeCoreAsync()
        {
            // Never call the collection callback while holding the owner lock.
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            Exception? failure = null;
            try { if (_runtime is not null) await _runtime.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            try { if (_publicCancellation is not null) await _publicCancellation.ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            if (_publicAcquisition is { } acquiring)
            {
                try { _publicLease = await acquiring.ConfigureAwait(false); }
                catch { if (_composition.PublicSnapshot?.CleanupFailed == true) failure ??= new IOException("Public startup cleanup failed."); }
            }
            try { if (_publicLease is not null) await _publicLease.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            finally { _publicStop.Dispose(); _composition.Remove(this); }
            if (failure is not null) throw new InvalidOperationException("Channel owner cleanup failed.", failure);
        }
    }
}
