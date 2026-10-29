using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Rtmp;

namespace PeerCast.Nova.Network.Publishing;

/// <summary>
/// Read-only control-plane values for one publish generation. No resources,
/// media, metadata bodies, exception text or credentials cross this boundary.
/// Connection fields contain configured localhost routes; do not disclose them.
/// This low-level DTO is not the sanitized application disclosure boundary.
/// Revision orders observed value changes, including listener/TCP facts; it is
/// stable for repeated unchanged reads, not an atomic cross-resource event log.
/// </summary>
public sealed record ObsPublishStatusSnapshot(
    ChannelId ChannelId,
    ChannelSessionId SessionId,
    long Revision,
    ChannelSessionState State,
    bool ListenerReady,
    bool PublisherConnected,
    bool HeadAvailable,
    string? RtmpServer,
    string PublishName,
    string? HttpStreamUrl,
    ObsPublishChannelEndReason? EndReason,
    ObsRtmpRejectionStage? RejectionStage,
    ObsRtmpRejectionReason? RejectionReason)
{
    public PublishingAnnounceStatus? Announce { get; init; }
    public ObsPublishLanStatus? LanRelay { get; init; }
    public ObsPublishWanStatus? WanRelay { get; init; }
}
