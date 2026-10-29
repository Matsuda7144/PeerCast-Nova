using System.Net;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>An immutable connection-scoped statement of publication intent, never independent reachability proof.</summary>
internal sealed record PublishingAnnounceCapability(
    PcpAnnounceIdentity Identity, Guid ConnectionId, IPEndPoint BindEndpoint,
    IPEndPoint AdvertisedEndpoint, IPEndPoint TrackerEndpoint, Func<bool> IsCurrent)
{
    private int _closed;
    internal bool IsValid => Volatile.Read(ref _closed) == 0 && IsCurrent();
    internal void Invalidate() => Interlocked.Exchange(ref _closed, 1);
    internal PublishingTrackerReport Evaluate(PeerCast.Nova.Protocol.Pcp.PcpOlehInfo observation, DateTimeOffset observedAt)
    {
        if (!IsValid || ConnectionId == Guid.Empty || Identity.Generation < 1 ||
            Identity.ChannelId == Guid.Empty || Identity.SenderSessionId == Guid.Empty ||
            Identity.SenderSessionId != Identity.PublishingSessionId ||
            Identity.BroadcastId == Guid.Empty || Identity.BroadcastId == Identity.SenderSessionId ||
            !PublishingIpv4.IsPublicAddress(AdvertisedEndpoint.Address.ToString()) || AdvertisedEndpoint.Port == 0 ||
            !PublishingIpv4.IsPublicAddress(TrackerEndpoint.Address.ToString()))
            return new(this, observation.SessionId, observedAt, PublishingTrackerReportKind.InvalidCapability);
        var kind = observation.SessionId == Guid.Empty || observation.SessionId == Identity.SenderSessionId ||
            observation.SessionId == Identity.BroadcastId ? PublishingTrackerReportKind.InvalidTrackerIdentity :
            observation.RemotePort is null ? PublishingTrackerReportKind.PortMissing :
            observation.RemotePort == 0 ? PublishingTrackerReportKind.PortClosed :
            observation.RemotePort != AdvertisedEndpoint.Port ? PublishingTrackerReportKind.PortMismatch :
            observation.RemoteAddress is null || !observation.RemoteAddress.Equals(AdvertisedEndpoint.Address)
                ? PublishingTrackerReportKind.AddressMismatch : PublishingTrackerReportKind.TrackerReportedOpen;
        return new(this, observation.SessionId, observedAt, kind);
    }
}

internal enum PublishingTrackerReportKind
{
    InvalidCapability, InvalidTrackerIdentity, PortMissing, PortClosed, PortMismatch, AddressMismatch, TrackerReportedOpen,
}

internal sealed record PublishingTrackerReport(PublishingAnnounceCapability Capability, Guid TrackerSessionId,
    DateTimeOffset ObservedAt, PublishingTrackerReportKind Kind)
{
    internal bool CanAnnounce(PublishingAnnounceCapability current) => ReferenceEquals(current, Capability) &&
        current.IsValid && Kind == PublishingTrackerReportKind.TrackerReportedOpen;
    internal bool IndependentReachabilityVerified { get; }
}
