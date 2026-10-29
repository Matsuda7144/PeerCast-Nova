using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>One generation's observational HOST state. Capacity is advisory; connection admission remains authoritative.</summary>
internal sealed record PublishingAnnounceRuntimeSnapshot(ChannelId ChannelId, ChannelSessionId SessionId,
    long Revision, bool Receiving, bool RelayAvailable, uint RelayCount, uint UptimeSeconds, bool CapabilityValid)
{
    internal bool DirectAvailable { get; }
    internal uint DirectCount { get; }

    internal static PublishingAnnounceRuntimeSnapshot Create(ObsPublishStatusSnapshot status, bool live,
        bool admissionAvailable, bool tcpAvailable, bool fanoutAvailable, int establishedRelays, uint uptime)
    {
        var valid = live && status.WanRelay?.ListenerReady == true;
        var receiving = valid && status.State == ChannelSessionState.Receiving && status.HeadAvailable && status.PublisherConnected;
        return new(status.ChannelId, status.SessionId, status.Revision, receiving,
            receiving && admissionAvailable && tcpAvailable && fanoutAvailable,
            checked((uint)establishedRelays), uptime, valid);
    }
}
