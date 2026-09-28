using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.Core.Recording;

/// <summary>
/// Immutable channel observation supplied to the recording rule engine.
/// </summary>
public sealed record RecordingCandidate
{
    public RecordingCandidate(
        ChannelId channelId,
        ChannelMetadataSnapshot metadata)
    {
        ChannelId = channelId
            ?? throw new ArgumentNullException(nameof(channelId));
        Metadata = metadata
            ?? throw new ArgumentNullException(nameof(metadata));
    }

    public ChannelId ChannelId { get; }

    public ChannelMetadataSnapshot Metadata { get; }
}
