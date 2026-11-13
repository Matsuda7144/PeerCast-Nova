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
        lock (_gate) return _publicHost ??= (_publicProvider ?? throw new GenesisPublicRelayStartException())
            .GetSharedRelay(_publicSettings, ResolvePublicOwner);
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
        private Task<GenesisSharedPublicRelay.Lease>? _publicAcquisition;
        private readonly CancellationTokenSource _publicStop = new();
        private Task? _publicCancellation;
        private readonly TaskCompletionSource _publicReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool PublicRequired => Kind == RelayChannelKind.Viewing && _composition._publicSettings.WanRelayEnabled;
        internal Task PublicReady => PublicRequired ? _publicReady.Task : Task.CompletedTask;
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
