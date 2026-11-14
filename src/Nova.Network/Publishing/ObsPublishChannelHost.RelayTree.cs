using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Network.Publishing;

public sealed partial class ObsPublishChannelHost : IPcpRelayTreeSource, IPcpLocalRelayTreeSource, IPcpDownstreamRelayControl
{
    public PcpDownstreamDisconnectRequest RequestDisconnect(PcpDownstreamDisconnectTarget target) => _relay.RequestDisconnect(target);
    PcpLocalRelayTreeObservation IPcpLocalRelayTreeSource.ObserveLocalRelayTree() =>
        ((IPcpLocalRelayTreeSource)_relay).ObserveLocalRelayTree();
    PcpRelayTreeObservation IPcpRelayTreeSource.ObserveRelayTree() => _relay.ObserveRelayTree() with
    { LocalInputConnected = Volatile.Read(ref _result) is null && _ingest.ReadStatus().PublisherConnected };
}
