using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.App;

internal sealed partial class GenesisDownstreamRelayOwnerRegistry
{
    internal PcpDownstreamDisconnectRequest RequestDisconnect(PcpDownstreamDisconnectTarget target)
    {
        WeakReference<IPcpRelayTreeSource>? weak;
        lock (_gate)
        {
            if (_disposed || !_owners.TryGetValue(target.ChannelId, out var owner) ||
                owner.Retired || owner.SessionId != target.OwnerId)
                return new(PcpDownstreamDisconnectOutcome.OwnerChanged);
            weak = owner.TreeSource;
        }
        // The Network owner re-enters the generation gate for atomic acceptance.
        return weak?.TryGetTarget(out var source) == true && source is IPcpDownstreamRelayControl control
            ? control.RequestDisconnect(target) : new(PcpDownstreamDisconnectOutcome.NotOwnedDownstream);
    }

    internal sealed partial class Owner : IPcpDownstreamRelayCommandGate
    {
        public PcpDownstreamDisconnectRequest AcceptDisconnect(PcpDownstreamDisconnectTarget target,
            Func<PcpDownstreamDisconnectRequest> accept)
        {
            lock (_registry._gate)
            {
                if (target.ChannelId != ChannelId) return new(PcpDownstreamDisconnectOutcome.ChannelMismatch);
                if (_registry._disposed || Retired || target.OwnerId != SessionId || !Scope.IsCurrent ||
                    !_registry._owners.TryGetValue(ChannelId, out var current) || !ReferenceEquals(current, this))
                    return new(PcpDownstreamDisconnectOutcome.OwnerChanged);
                // Only Network validation/marking; cancellation and cleanup run later.
                return accept();
            }
        }
    }
}
