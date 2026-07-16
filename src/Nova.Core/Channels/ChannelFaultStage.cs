namespace PeerCast.Nova.Core.Channels;

/// <summary>
/// Protocol-independent stage at which a terminal channel fault occurred.
/// </summary>
public enum ChannelFaultStage
{
    Connecting,
    Negotiating,
    Receiving,
    Consuming,
}
