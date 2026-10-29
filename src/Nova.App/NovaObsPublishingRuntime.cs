using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;
using PeerCast.Nova.Network.Policies;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal interface INovaObsPublishingStatusSource
{
    ObsPublishStatusSnapshot Snapshot { get; }
}

internal interface INovaObsPublishingRuntime : INovaObsPublishingStatusSource, IAsyncDisposable
{
    void Start(CancellationToken cancellationToken);
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
    internal SystemNovaObsPublishingRuntimeFactory(GenesisGuiNextApplicationState? state = null,
        IObsPublishLanInterfaces? interfaces = null, bool allowLan = true)
    { _state = state; _interfaces = interfaces ?? SystemObsPublishLanInterfaces.Instance; _allowLan = allowLan; }

    internal GenesisDownstreamRelayOwnerRegistry.Owner Register(ChannelId channel, ChannelSessionId session, PcpRelayFanout fanout)
    {
        if (!_allowLan || _state is null || _state.Connection.LoadFailed)
            throw new ObsPublishLanException(ObsPublishLanFailure.AdmissionUnavailable);
        if (_state.NetworkPolicies.Bandwidth.Limits.Ipv4Kbps is not null)
            throw new ObsPublishLanException(ObsPublishLanFailure.BandwidthUnsupported);
        try { return _state.DownstreamRelays.Register(channel, session, RelayChannelKind.Broadcast, fanout); }
        catch { throw new ObsPublishLanException(ObsPublishLanFailure.AdmissionUnavailable); }
    }
    private NovaObsPublishingRuntime Create(ObsPublishChannelOptions options) =>
        NovaObsPublishingRuntime.ForOwner(new ObsPublishChannelHost(options, interfaces: _interfaces, registerRelay: Register, announceOwner: _state?.AnnounceOwner));

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

    public Task<ObsPublishChannelResult> Completion => _host.Completion;

    public Task<ObsPublishLocalTerminationResult> RequestLocalTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _host.RequestLocalTerminationAsync(timeout, cancellationToken);

    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
