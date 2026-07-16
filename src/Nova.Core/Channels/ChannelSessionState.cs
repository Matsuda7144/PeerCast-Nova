namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Control-plane state of a stable local channel session.
/// </summary>
public enum ChannelSessionState
{
    Created,
    Connecting,
    Negotiating,
    Receiving,
    Reconnecting,
    Faulted,
    Stopping,
    Stopped,
}
