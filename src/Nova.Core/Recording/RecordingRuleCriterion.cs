namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Individual criteria that can reject a recording candidate.
/// </summary>
public enum RecordingRuleCriterion
{
    ChannelId,
    Name,
    Genre,
    Description,
    ContentType,
    StreamType,
    Comment,
    MinimumBitrateKbps,
    MaximumBitrateKbps,
}
