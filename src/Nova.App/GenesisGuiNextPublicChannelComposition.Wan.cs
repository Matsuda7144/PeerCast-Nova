using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;

namespace PeerCast.Nova.App;

internal sealed class GenesisPublicRelayStartException() : IOException("WAN Relayの待受を開始できません。設定を確認してください。");

internal sealed partial class GenesisGuiNextPublicChannelComposition
{
    private GenesisGuiNextConnectionPreferences _publicSettings = GenesisGuiNextConnectionPreferences.Default;
    private INovaSharedPublicRelayProvider? _publicProvider;
    private GenesisSharedPublicRelay? _publicHost;
    internal ushort? AdvertisedPublicPort =>
        _publicSettings.WanRelayEnabled && !_publicSettings.UseIpv6 && _publicSettings.ListenPort is >= 1 and <= 65535
            ? (ushort)_publicSettings.ListenPort
            : null;
    internal async Task<bool> StartPublicAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return false;
            if (!_publicSettings.WanRelayEnabled) return true;
        }
        try
        {
            await GetPublicHost().StartAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            // A bind failure is retained in the shared host's safe snapshot.
            // Catalog, settings and private playback remain available.
            return false;
        }
    }
    internal async Task<bool> ApplyLivePublicSettingsAsync(GenesisGuiNextConnectionPreferences settings)
    {
        settings.Validate();
        GenesisSharedPublicRelay? hostToDispose = null;
        Owner[] currentOwners;
        bool stopExistingRoutes;
        lock (_gate)
        {
            if (_disposeTask is not null) return false;
            stopExistingRoutes = !settings.WanRelayEnabled ||
                settings.ListenPort != _publicSettings.ListenPort ||
                settings.UseIpv6 != _publicSettings.UseIpv6;
            if (_publicHost is not null && stopExistingRoutes)
            {
                hostToDispose = _publicHost;
                _publicHost = null;
            }
            _publicSettings = settings;
            currentOwners = [.. _owners];
        }

        if (stopExistingRoutes)
        {
            foreach (var owner in currentOwners)
                await owner.StopPublicRouteAsync().ConfigureAwait(false);
        }

        if (hostToDispose is not null)
            await hostToDispose.DisposeAsync().ConfigureAwait(false);

        if (!settings.WanRelayEnabled) return true;

        var started = await StartPublicAsync().ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposeTask is not null) return false;
            currentOwners = [.. _owners];
        }

        foreach (var owner in currentOwners)
            owner.StartPublicRouteIfEnabled();

        return started;
    }
    internal void ConfigurePublic(GenesisGuiNextConnectionPreferences settings, INovaSharedPublicRelayProvider? provider = null)
    {
        settings.Validate();
        lock (_gate)
        {
            if (_owners.Any(o => o.ActivationStarted)) throw new InvalidOperationException("Public settings are captured before activation.");
            _publicSettings = settings;
            _publicProvider = provider;
        }
    }
    private GenesisSharedPublicRelay GetPublicHost()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposeTask is not null, this);
            return _publicHost ??= (_publicProvider ?? throw new GenesisPublicRelayStartException())
                .GetSharedRelay(_publicSettings, ResolvePublicOwner);
        }
    }
    private PcpRelayCoordinator? ResolvePublicOwner(ChannelId channel, PcpRelayFanout fanout)
    {
        lock (_gate) return _current.TryGetValue(channel, out var owner) && ReferenceEquals(owner.Fanout, fanout) &&
            owner.IsCurrent && owner.ActivationStarted ? owner.ViewingRuntime : null;
    }
    internal string PublicStatusText
    {
        get
        {
            var snapshot = _publicHost?.Snapshot;
            if (!_publicSettings.WanRelayEnabled) return "WAN Relay: OFF";
            if (snapshot is null) return "WAN Relay: 開始待ち";
            return $"WAN Relay: {snapshot.ListenerText} / port {snapshot.Port} / {snapshot.MappingText} / {snapshot.Reachability}";
        }
    }
    internal GenesisSharedPublicRelaySnapshot? PublicSnapshot => _publicHost?.Snapshot;

    internal sealed partial class Owner
    {
        private IAsyncDisposable? _publicRoute;
        private GenesisSharedPublicRelay.Lease? _publicLease;
        private Task<GenesisSharedPublicRelay.Lease?>? _publicAcquisition;
        private CancellationTokenSource _publicStop = new();
        private Task? _publicCancellation;
        private TaskCompletionSource _publicReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool PublicRequired => Kind == RelayChannelKind.Viewing && _composition._publicSettings.WanRelayEnabled;
        internal Task PublicReady => PublicRequired ? _publicReady.Task : Task.CompletedTask;

        internal void StartPublicRouteIfEnabled()
        {
            if (!PublicRequired || ViewingRuntime is null) return;
            lock (_gate)
            {
                if (_retired || _disposeTask is not null || _publicAcquisition is not null) return;
                _publicAcquisition = AcquirePublicRouteAsync();
            }
        }

        internal async Task StopPublicRouteAsync()
        {
            CancellationTokenSource? previousStop;
            Task<GenesisSharedPublicRelay.Lease?>? acquiring;
            GenesisSharedPublicRelay.Lease? lease;
            IAsyncDisposable? route;
            lock (_gate)
            {
                acquiring = _publicAcquisition;
                lease = _publicLease;
                route = _publicRoute;
                if (acquiring is null && lease is null && route is null) return;
                _publicAcquisition = null;
                _publicLease = null;
                _publicRoute = null;
                previousStop = _publicStop;
                _publicStop = new CancellationTokenSource();
                _publicReady.TrySetCanceled(CancellationToken.None);
                _publicReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            if (previousStop is not null)
            {
                await previousStop.CancelAsync().ConfigureAwait(false);
                previousStop.Dispose();
            }

            if (route is not null)
            {
                try { await route.DisposeAsync().ConfigureAwait(false); } catch { }
            }

            if (acquiring is not null)
            {
                try
                {
                    if (await acquiring.ConfigureAwait(false) is { } acquired && !ReferenceEquals(acquired, lease))
                        await acquired.DisposeAsync().ConfigureAwait(false);
                }
                catch { }
            }

            if (lease is not null)
            {
                try { await lease.DisposeAsync().ConfigureAwait(false); } catch { }
            }
        }

        private async Task<GenesisSharedPublicRelay.Lease?> AcquirePublicRouteAsync()
        {
            try
            {
                if (await ViewingRuntime!.WaitForReadinessAsync(_publicStop.Token).ConfigureAwait(false) !=
                    PcpRelayCoordinatorReadinessOutcome.Ready)
                {
                    return null;
                }

                var lease = await _composition.GetPublicHost()
                    .AcquireAsync(BindPublicRoute, _publicStop.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_retired)
                    {
                        _publicReady.TrySetCanceled(CancellationToken.None);
                        return lease;
                    }

                    _publicReady.TrySetResult();
                }

                return lease;
            }
            catch (OperationCanceledException)
            {
                _publicReady.TrySetCanceled(CancellationToken.None);
                return null;
            }
            catch (Exception)
            {
                // WAN 公開に失敗してもローカルの視聴・録画自体は継続する。
                _publicReady.TrySetCanceled(CancellationToken.None);
                return null;
            }
        }

        private IAsyncDisposable BindPublicRoute(PcpSharedDownstreamRelayListener routes)
        {
            lock (_gate)
            {
                if (_retired || _registration?.IsCurrent != true) throw new OperationCanceledException();
                return _publicRoute = routes.Register(ChannelId, Fanout);
            }
        }
    }
}
