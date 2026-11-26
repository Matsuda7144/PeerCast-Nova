using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.PortMapping;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal interface INovaObsPublishingStatusSource
{
    ObsPublishStatusSnapshot Snapshot { get; }
}

internal interface INovaObsPublishingRuntime : INovaObsPublishingStatusSource, IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);
    bool TryUpdateLiveMetadata(ChannelMetadataSnapshot metadata) => false;
}

internal interface INovaObsPublishingLifecycleRuntime : INovaObsPublishingRuntime
{
    Task<ObsPublishChannelResult> Completion { get; }
}

internal interface INovaObsPublishingFinishRuntime : INovaObsPublishingLifecycleRuntime
{
    Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>
/// An opt-in ownership contract, not a status assertion. Identity must remain
/// immutable for this owner, or be invalidated under Proof.Gate BEFORE mutation.
/// A runtime unable to guarantee this must not implement the capability.
/// </summary>
internal interface INovaObsPublishingGenerationRuntime : INovaObsPublishingFinishRuntime
{
    NovaObsPublishingGenerationProof GenerationProof { get; }
}

internal sealed class NovaObsPublishingGenerationProof(Guid channelId, Guid sessionId)
{
    internal object Gate { get; } = new();
    internal Guid ChannelId { get; } = channelId;
    internal Guid SessionId { get; } = sessionId;
    private bool _valid = true;
    internal bool IsValid { get { lock (Gate) return _valid; } }
    internal void Invalidate() { lock (Gate) _valid = false; }
    internal void Observe(NovaObsPublishingReadOnlySnapshot snapshot)
    {
        if (snapshot.Availability == NovaObsPublishingAvailability.Unavailable ||
            snapshot.ChannelId != ChannelId || snapshot.SessionId != SessionId) Invalidate();
    }
}

internal interface INovaObsPublishingRuntimeFactory
{
    INovaObsPublishingRuntime Create();
    INovaObsPublishingRuntime Create(PublishingSettingsSnapshot settings) => Create();
}

/// <summary>Application-owned resources, outside the observational projection/window.</summary>
internal sealed class SystemNovaObsPublishingRuntimeFactory : INovaObsPublishingRuntimeFactory
{
    public static SystemNovaObsPublishingRuntimeFactory Instance { get; } = new();

    private readonly GenesisGuiNextApplicationState? _state;
    private readonly IObsPublishLanInterfaces _interfaces;
    private readonly bool _allowLan;
    private readonly Func<NatPortMappingManager?>? _portMappingProvider;
    internal SystemNovaObsPublishingRuntimeFactory(GenesisGuiNextApplicationState? state = null,
        IObsPublishLanInterfaces? interfaces = null, bool allowLan = true,
        Func<NatPortMappingManager?>? portMappingProvider = null)
    { _state = state; _interfaces = interfaces ?? SystemObsPublishLanInterfaces.Instance; _allowLan = allowLan; _portMappingProvider = portMappingProvider; }

    internal GenesisDownstreamRelayOwnerRegistry.Owner Register(ChannelId channel, ChannelSessionId session, PcpRelayFanout fanout) =>
        Register(channel, session, fanout, null, publicRelay: true);

    internal GenesisDownstreamRelayOwnerRegistry.Owner Register(ChannelId channel, ChannelSessionId session, PcpRelayFanout fanout,
        int? bitrateKbps, Func<int?>? readBitrateKbps = null) =>
        Register(channel, session, fanout, bitrateKbps, publicRelay: true, readBitrateKbps);

    private GenesisDownstreamRelayOwnerRegistry.Owner Register(ChannelId channel, ChannelSessionId session, PcpRelayFanout fanout,
        int? bitrateKbps, bool publicRelay, Func<int?>? readBitrateKbps = null)
    {
        if (_state is null || publicRelay && (!_allowLan || _state.Connection.LoadFailed))
            throw new ObsPublishLanException(ObsPublishLanFailure.AdmissionUnavailable);
        try { return _state.DownstreamRelays.Register(channel, session, RelayChannelKind.Broadcast, fanout, readBitrateKbps ?? (() => bitrateKbps)); }
        catch { throw new ObsPublishLanException(ObsPublishLanFailure.AdmissionUnavailable); }
    }
    internal NovaObsPublishingRuntime Create(ObsPublishChannelOptions options)
    {
        ObsPublishChannelHost? created = null;
        created = new ObsPublishChannelHost(options, interfaces: _interfaces,
            registerRelay: (channel, session, fanout) => Register(channel, session, fanout, options.BitrateKbps,
                options.Lan.Enabled || options.Wan.Enabled, () => created?.EffectiveBitrateKbps ?? options.BitrateKbps),
            announceOwner: _state?.AnnounceOwner,
            useLoopbackAdmission: _state is not null, portMapping: _portMappingProvider?.Invoke());
        return NovaObsPublishingRuntime.ForOwner(created);
    }

    public INovaObsPublishingRuntime Create() => Create(new ObsPublishChannelOptions());
    public INovaObsPublishingRuntime Create(PublishingSettingsSnapshot settings) => Create(settings.Values.ToOptions());
}

internal sealed class NovaObsPublishingRuntime : INovaObsPublishingGenerationRuntime
{
    private readonly ObsPublishChannelHost _host;

    public NovaObsPublishingRuntime(ObsPublishChannelOptions options) : this(new ObsPublishChannelHost(options)) { }

    private NovaObsPublishingRuntime(ObsPublishChannelHost host)
    {
        _host = host;
        // #323 identities are readonly for the actual owner lifetime; no Snapshot
        // callback is needed to obtain or guarantee their stability.
        GenerationProof = new(host.ChannelId.Value, host.SessionId.Value);
    }
    public NovaObsPublishingGenerationProof GenerationProof { get; }
    internal static NovaObsPublishingRuntime ForOwner(ObsPublishChannelHost host) => new(host);


    public ObsPublishStatusSnapshot Snapshot => _host.PublishingStatus;

    public void Start(CancellationToken cancellationToken) => _host.Start(cancellationToken);

    public bool TryUpdateLiveMetadata(ChannelMetadataSnapshot metadata) =>
        _host.TryUpdateLiveMetadata(metadata);

    public Task<ObsPublishChannelResult> Completion => _host.Completion;

    public Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _host.RequestLocalTerminationAsync(timeout, cancellationToken);

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
