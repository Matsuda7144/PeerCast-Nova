namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Fixed, privacy-safe terminal fault categories. Values never contain peer,
/// channel, metadata, payload, position, or exception details.
/// </summary>
public enum ChannelFaultCategory
{
    ConnectionFailed,
    TimedOut,
    RemoteUnavailable,
    RemoteRejected,
    RemoteEnded,
    ProtocolViolation,
    ConsumerFailed,
    ResourceLimitExceeded,
    InternalFailure,
}
