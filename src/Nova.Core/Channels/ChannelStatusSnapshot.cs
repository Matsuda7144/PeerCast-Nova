namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Immutable control-plane view of a channel session. It never owns media
/// payload bytes, sockets, or disposable resources.
/// </summary>
public sealed class ChannelStatusSnapshot
{
    internal ChannelStatusSnapshot(
        ChannelId channelId,
        ChannelSessionId sessionId,
        ChannelSessionState state,
        ChannelMetadataSnapshot metadata,
        long? streamPosition,
        ChannelFaultSnapshot? fault,
        long revision)
    {
        ChannelId = channelId;
        SessionId = sessionId;
        State = state;
        Metadata = metadata;
        StreamPosition = streamPosition;
        Fault = fault;
        Revision = revision;
    }

    public ChannelId ChannelId { get; }

    public ChannelSessionId SessionId { get; }

    public ChannelSessionState State { get; }

    public ChannelMetadataSnapshot Metadata { get; }

    public long? StreamPosition { get; }

    /// <summary>
    /// Fixed terminal fault information, or null while no terminal fault has
    /// been observed. It contains no free-form or peer-controlled values.
    /// </summary>
    public ChannelFaultSnapshot? Fault { get; }

    /// <summary>
    /// Monotonically increases whenever the observable snapshot changes.
    /// </summary>
    public long Revision { get; }
}
